using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.Worker;

namespace LocalFileAgent.Infrastructure.Worker;

public sealed class WorkerClient : IWorkerClient
{
    private readonly string _workerExePath;
    private readonly string _pipeName;
    private NamedPipeServerStream? _pipeServer;
    private Process? _workerProcess;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public WorkerClient(string? workerExePath = null, string? pipeName = null)
    {
        _workerExePath = workerExePath ?? FindWorkerExe();
        _pipeName = pipeName ?? $"lfa_worker_{Guid.NewGuid():N}";
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pipeServer != null)
            {
                return;
            }

            _pipeServer = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous
            );

            if (!File.Exists(_workerExePath))
            {
                throw new FileNotFoundException($"Worker executable not found at '{_workerExePath}'");
            }

            var psi = new ProcessStartInfo
            {
                FileName = _workerExePath,
                Arguments = $"--pipe {_pipeName}",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _workerProcess = Process.Start(psi);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

            // Wait for worker to connect
            await _pipeServer.WaitForConnectionAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var msg = new IpcWireMessage("ping", Guid.NewGuid().ToString("N"), null);
            await IpcProtocol.WriteMessageAsync(_pipeServer!, msg, cancellationToken).ConfigureAwait(false);

            var resp = await IpcProtocol.ReadMessageAsync(_pipeServer!, cancellationToken).ConfigureAwait(false);
            return resp?.Type == "pong";
        }
        catch
        {
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<WorkerParseResponse> ParseFileAsync(WorkerParseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureConnected();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var payload = JsonSerializer.Serialize(request);
            var msg = new IpcWireMessage("parse_request", request.RequestId, payload);

            await IpcProtocol.WriteMessageAsync(_pipeServer!, msg, cancellationToken).ConfigureAwait(false);

            var respMsg = await IpcProtocol.ReadMessageAsync(_pipeServer!, cancellationToken).ConfigureAwait(false);
            if (respMsg?.PayloadJson != null)
            {
                return JsonSerializer.Deserialize<WorkerParseResponse>(respMsg.PayloadJson)
                       ?? new WorkerParseResponse(request.RequestId, false, Array.Empty<ExtractedPage>(), "Failed to deserialize worker response");
            }

            return new WorkerParseResponse(request.RequestId, false, Array.Empty<ExtractedPage>(), "Empty worker response");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_pipeServer != null)
        {
            try
            {
                var shutdownMsg = new IpcWireMessage("shutdown", null, null);
                await IpcProtocol.WriteMessageAsync(_pipeServer, shutdownMsg, CancellationToken.None).ConfigureAwait(false);
            }
            catch { }

            await _pipeServer.DisposeAsync().ConfigureAwait(false);
            _pipeServer = null;
        }

        if (_workerProcess != null)
        {
            try
            {
                if (!_workerProcess.HasExited)
                {
                    _workerProcess.Kill();
                }
            }
            catch { }

            try
            {
                _workerProcess.Dispose();
            }
            catch { }

            _workerProcess = null;
        }

        _lock.Dispose();
    }

    private void EnsureConnected()
    {
        if (_pipeServer is not { IsConnected: true })
        {
            throw new InvalidOperationException("WorkerClient is not connected. Call StartAsync first.");
        }
    }

    private static string FindWorkerExe()
    {
        var baseDir = AppContext.BaseDirectory;
        var direct = Path.Combine(baseDir, "LocalFileAgent.Worker.exe");
        if (File.Exists(direct)) return direct;

        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        var debugWorker = Path.Combine(repoRoot, "src", "LocalFileAgent.Worker", "bin", "Debug", "net10.0-windows10.0.19041.0", "LocalFileAgent.Worker.exe");
        if (File.Exists(debugWorker)) return debugWorker;

        var releaseWorker = Path.Combine(repoRoot, "src", "LocalFileAgent.Worker", "bin", "Release", "net10.0-windows10.0.19041.0", "LocalFileAgent.Worker.exe");
        if (File.Exists(releaseWorker)) return releaseWorker;

        return direct;
    }
}

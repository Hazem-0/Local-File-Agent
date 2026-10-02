using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LocalFileAgent.Domain.Worker;

public sealed record WorkerParseRequest(
    string RequestId,
    string FilePath,
    string Extension,
    int PageCap = 50,
    int TimeoutMs = 30000
);

public sealed record ExtractedPage(
    int PageNumber,
    string Text,
    string SourceKind,
    float Confidence = 1.0f
);

public sealed record WorkerParseResponse(
    string RequestId,
    bool Success,
    IReadOnlyList<ExtractedPage> Pages,
    string? ErrorMessage = null,
    double DurationMs = 0.0
);

public sealed record IpcWireMessage(
    string Type,
    string? RequestId,
    string? PayloadJson
);

public static class IpcProtocol
{
    public static async Task WriteMessageAsync(Stream stream, IpcWireMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);

        var json = JsonSerializer.Serialize(message);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        var lengthBytes = BitConverter.GetBytes(payloadBytes.Length);

        await stream.WriteAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IpcWireMessage?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var lengthBytes = new byte[4];
        var read = 0;
        while (read < 4)
        {
            var r = await stream.ReadAsync(lengthBytes.AsMemory(read, 4 - read), cancellationToken).ConfigureAwait(false);
            if (r == 0) return null; // Stream closed
            read += r;
        }

        var length = BitConverter.ToInt32(lengthBytes);
        if (length <= 0 || length > 64 * 1024 * 1024) // 64 MB guard
        {
            return null;
        }

        var payloadBytes = new byte[length];
        read = 0;
        while (read < length)
        {
            var r = await stream.ReadAsync(payloadBytes.AsMemory(read, length - read), cancellationToken).ConfigureAwait(false);
            if (r == 0) return null;
            read += r;
        }

        var json = Encoding.UTF8.GetString(payloadBytes);
        return JsonSerializer.Deserialize<IpcWireMessage>(json);
    }
}

public interface IWorkerClient : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
    Task<WorkerParseResponse> ParseFileAsync(WorkerParseRequest request, CancellationToken cancellationToken = default);
}

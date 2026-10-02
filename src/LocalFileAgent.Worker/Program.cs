using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Domain.Worker;
using LocalFileAgent.Text;

namespace LocalFileAgent.Worker;

public static class Program
{
    private static readonly EncodingDetector EncodingDet = new();
    private static readonly ArabicOrderFixer OrderFix = new();
    private static readonly ArabicTextNormalizer Normalizer = new();

    public static async Task<int> Main(string[] args)
    {
        LogDebug($"Worker Main started with args: {string.Join(" ", args)}");
        var pipeName = ParsePipeArg(args);
        if (string.IsNullOrEmpty(pipeName))
        {
            LogDebug("Error: pipeName is empty");
            return 1;
        }

        try
        {
            LogDebug($"Connecting to pipe: {pipeName}...");
            using var clientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await clientStream.ConnectAsync(10000).ConfigureAwait(false);
            LogDebug("Connected to pipe successfully!");

            while (true)
            {
                var msg = await IpcProtocol.ReadMessageAsync(clientStream).ConfigureAwait(false);
                if (msg == null)
                {
                    LogDebug("Pipe closed by server (null message). Exiting.");
                    break;
                }

                if (msg.Type == "shutdown")
                {
                    LogDebug("Received shutdown message. Exiting.");
                    break;
                }

                if (msg.Type == "ping")
                {
                    var pong = new IpcWireMessage("pong", msg.RequestId, null);
                    await IpcProtocol.WriteMessageAsync(clientStream, pong).ConfigureAwait(false);
                    continue;
                }

                if (msg.Type == "parse_request" && msg.PayloadJson != null)
                {
                    var req = JsonSerializer.Deserialize<WorkerParseRequest>(msg.PayloadJson);
                    if (req != null)
                    {
                        var resp = await ProcessParseRequestAsync(req).ConfigureAwait(false);
                        var respMsg = new IpcWireMessage("parse_response", req.RequestId, JsonSerializer.Serialize(resp));
                        await IpcProtocol.WriteMessageAsync(clientStream, respMsg).ConfigureAwait(false);
                    }
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            LogDebug($"Worker Main exception: {ex}");
            return 1;
        }
    }

    private static void LogDebug(string message)
    {
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "lfa_worker_debug.log");
            using var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(fs, Encoding.UTF8);
            writer.WriteLine($"{DateTime.UtcNow:HH:mm:ss.fff} [PID {Environment.ProcessId}] {message}");
        }
        catch { }
    }

    private static async Task<WorkerParseResponse> ProcessParseRequestAsync(WorkerParseRequest req)
    {
        var sw = Stopwatch.StartNew();
        if (!File.Exists(req.FilePath))
        {
            return new WorkerParseResponse(req.RequestId, false, Array.Empty<ExtractedPage>(), "File not found", sw.Elapsed.TotalMilliseconds);
        }

        var ext = Path.GetExtension(req.FilePath).ToLowerInvariant();

        try
        {
            // Text and CSV files
            if (ext is ".txt" or ".md" or ".csv" or ".json" or ".xml")
            {
                var bytes = await File.ReadAllBytesAsync(req.FilePath).ConfigureAwait(false);
                var detected = EncodingDet.Decode(bytes);
                var text = detected.Text;

                // Fix reversed Arabic text if detected
                var order = OrderFix.Analyze(text);
                if (order.IsReversed)
                {
                    text = OrderFix.Fix(text, order);
                }

                var page = new ExtractedPage(1, text, "text_layer", detected.Confidence);
                sw.Stop();
                return new WorkerParseResponse(req.RequestId, true, new[] { page }, null, sw.Elapsed.TotalMilliseconds);
            }

            // Image files (Windows OCR Tier 1)
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff")
            {
                var ocrEngine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ar-SA"))
                                ?? OcrEngine.TryCreateFromUserProfileLanguages();

                if (ocrEngine == null)
                {
                    sw.Stop();
                    return new WorkerParseResponse(req.RequestId, false, Array.Empty<ExtractedPage>(), "Windows OCR not available", sw.Elapsed.TotalMilliseconds);
                }

                var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(req.FilePath));
                using var stream = await file.OpenAsync(FileAccessMode.Read);
                var decoder = await BitmapDecoder.CreateAsync(stream);
                using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

                var result = await ocrEngine.RecognizeAsync(softwareBitmap);
                var page = new ExtractedPage(1, result.Text, "ocr_win", 0.90f);

                sw.Stop();
                return new WorkerParseResponse(req.RequestId, true, new[] { page }, null, sw.Elapsed.TotalMilliseconds);
            }

            // Fallback for other formats (will be expanded in M5/M5b)
            sw.Stop();
            return new WorkerParseResponse(req.RequestId, true, Array.Empty<ExtractedPage>(), null, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new WorkerParseResponse(req.RequestId, false, Array.Empty<ExtractedPage>(), ex.Message, sw.Elapsed.TotalMilliseconds);
        }
    }

    private static string? ParsePipeArg(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--pipe", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }
}

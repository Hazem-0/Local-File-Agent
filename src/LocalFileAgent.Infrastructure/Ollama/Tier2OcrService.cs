using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Text;

namespace LocalFileAgent.Infrastructure.Ollama;

public sealed record Tier2OcrOptions
{
    public string ModelName { get; init; } = "gemma4:e2b";
    public string Prompt { get; init; } = "Extract all text visible in this document image with highest accuracy. Maintain exact reading order. Preserve Arabic words, letters, and numbers as written. Output only the extracted text without any introduction or commentary.";
    public float Temperature { get; init; } = 0.1f;
}

public sealed class Tier2OcrService : ITier2OcrService
{
    private readonly IOllamaClient _ollamaClient;
    private readonly ITextQualityGate _qualityGate;
    private readonly IArabicOrderFixer _orderFixer;
    private readonly Tier2OcrOptions _options;

    public Tier2OcrService(
        IOllamaClient ollamaClient,
        ITextQualityGate qualityGate,
        IArabicOrderFixer orderFixer,
        Tier2OcrOptions? options = null)
    {
        _ollamaClient = ollamaClient ?? throw new ArgumentNullException(nameof(ollamaClient));
        _qualityGate = qualityGate ?? throw new ArgumentNullException(nameof(qualityGate));
        _orderFixer = orderFixer ?? throw new ArgumentNullException(nameof(orderFixer));
        _options = options ?? new Tier2OcrOptions();
    }

    public async Task<OcrExtractionResult?> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        if (!File.Exists(imagePath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
        var base64 = Convert.ToBase64String(bytes);
        return await RecognizeBase64Async(base64, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OcrExtractionResult?> RecognizeBase64Async(string base64Image, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(base64Image))
        {
            return null;
        }

        try
        {
            var req = new GenerateRequest(
                Model: _options.ModelName,
                Prompt: _options.Prompt,
                ImagesBase64: new[] { base64Image },
                Temperature: _options.Temperature,
                KeepAlive: "2m"
            );

            var resp = await _ollamaClient.GenerateAsync(req, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resp.Response))
            {
                return null;
            }

            var rawText = resp.Response.Trim();

            // Guard 1: Hallucination and repetition loop cleanup
            var repReport = RepetitionDetector.Analyze(rawText);
            var cleanedText = repReport.IsRepetitive ? repReport.CleanedText : rawText;

            // Guard 2: BiDi character stream order fixing
            var orderReport = _orderFixer.Analyze(cleanedText);
            var fixedText = orderReport.IsReversed ? _orderFixer.Fix(cleanedText, orderReport) : cleanedText;

            // Guard 3: Text quality evaluation
            var quality = _qualityGate.Score(fixedText, new LanguageHint("ar"));

            var engineName = _options.ModelName.Contains("glm", StringComparison.OrdinalIgnoreCase) ? "ocr_glm" : "ocr_vlm";

            return new OcrExtractionResult(
                Text: fixedText,
                Confidence: Math.Max(quality.Score, 0.70f),
                Engine: engineName
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Graceful fallback on Ollama or model failure
            return null;
        }
    }
}

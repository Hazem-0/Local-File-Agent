using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Text;

namespace LocalFileAgent.Infrastructure.Ollama;

public sealed record VisualDescriberOptions
{
    public string ModelName { get; init; } = "gemma4:e2b";
    public string Prompt { get; init; } = "صف محتوى هذه الصورة بدقة وإيجاز باللغة العربية. اذكر نوع المستند أو الصورة والعناصر البصرية الرئيسية والنصوص الواضحة لتسهيل العثور عليها بالبحث.";
    public float Temperature { get; init; } = 0.2f;
}

public sealed class VisualDescriber : IVisualDescriber
{
    private static readonly System.Buffers.SearchValues<char> SentenceDelimiters =
        System.Buffers.SearchValues.Create(['.', '،', '\n', '؛']);

    private readonly IOllamaClient _ollamaClient;
    private readonly VisualDescriberOptions _options;

    public VisualDescriber(IOllamaClient ollamaClient, VisualDescriberOptions? options = null)
    {
        _ollamaClient = ollamaClient ?? throw new ArgumentNullException(nameof(ollamaClient));
        _options = options ?? new VisualDescriberOptions();
    }

    public async Task<VisualDescriptionResult?> DescribeImageAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        if (!File.Exists(imagePath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
        var base64 = Convert.ToBase64String(bytes);
        return await DescribeImageBase64Async(base64, cancellationToken).ConfigureAwait(false);
    }

    public async Task<VisualDescriptionResult?> DescribeImageBase64Async(string base64Image, CancellationToken cancellationToken = default)
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

            // Guard against repetition loops
            var repReport = RepetitionDetector.Analyze(rawText);
            var cleanedText = repReport.IsRepetitive ? repReport.CleanedText : rawText;

            // Extract first sentence as concise caption, full text as summary
            var firstSentenceEnd = cleanedText.AsSpan().IndexOfAny(SentenceDelimiters);
            var caption = firstSentenceEnd > 0 ? cleanedText.Substring(0, firstSentenceEnd).Trim() : cleanedText;

            return new VisualDescriptionResult(
                Caption: caption,
                Summary: cleanedText,
                Confidence: 0.85f
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}

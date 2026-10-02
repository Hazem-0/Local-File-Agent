using System;
using System.Collections.Generic;
using LocalFileAgent.Domain.Text;

namespace LocalFileAgent.Text;

public sealed class ArabicChunker : IChunker
{
    private static readonly char[] ArabicSentenceDelimiters = new[] { '.', '؟', '!', '؛', '،', '\n', '\r' };

    public IReadOnlyList<Chunk> Chunk(string text, ChunkingOptions options, int pageNumber = 1, string sourceKind = "text_layer", float confidence = 1.0f)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<Chunk>();
        }

        var trimmed = text.Trim();
        if (trimmed.Length <= options.MaxChunkSize)
        {
            return new[] { new Chunk(trimmed, 0, 0, trimmed.Length, pageNumber, sourceKind, confidence) };
        }

        var chunks = new List<Chunk>();
        var currentStart = 0;
        var ordinal = 0;

        while (currentStart < trimmed.Length)
        {
            var remainingLength = trimmed.Length - currentStart;
            if (remainingLength <= options.MaxChunkSize)
            {
                var finalChunkText = trimmed.Substring(currentStart, remainingLength).Trim();
                if (!string.IsNullOrEmpty(finalChunkText))
                {
                    chunks.Add(new Chunk(finalChunkText, ordinal++, currentStart, trimmed.Length, pageNumber, sourceKind, confidence));
                }
                break;
            }

            // Look for a suitable sentence/punctuation break near TargetChunkSize
            var searchStart = Math.Min(currentStart + options.TargetChunkSize, trimmed.Length - 1);
            var maxAllowedEnd = Math.Min(currentStart + options.MaxChunkSize, trimmed.Length);

            var breakIndex = -1;
            // Search forwards up to MaxChunkSize for sentence boundary
            for (var i = searchStart; i < maxAllowedEnd; i++)
            {
                if (IsBoundary(trimmed[i]))
                {
                    breakIndex = i + 1; // include the delimiter in the chunk
                    break;
                }
            }

            // If not found forward, search backward from TargetChunkSize down to half of TargetChunkSize
            if (breakIndex == -1)
            {
                var minSearch = currentStart + (options.TargetChunkSize / 2);
                for (var i = searchStart; i >= minSearch; i--)
                {
                    if (IsBoundary(trimmed[i]))
                    {
                        breakIndex = i + 1;
                        break;
                    }
                }
            }

            // Fallback: search for space boundary
            if (breakIndex == -1)
            {
                for (var i = searchStart; i < maxAllowedEnd; i++)
                {
                    if (char.IsWhiteSpace(trimmed[i]))
                    {
                        breakIndex = i;
                        break;
                    }
                }
            }

            // Ultimate fallback: hard cut at MaxChunkSize
            if (breakIndex == -1 || breakIndex <= currentStart)
            {
                breakIndex = maxAllowedEnd;
            }

            var chunkText = trimmed.Substring(currentStart, breakIndex - currentStart).Trim();
            if (!string.IsNullOrEmpty(chunkText))
            {
                chunks.Add(new Chunk(chunkText, ordinal++, currentStart, breakIndex, pageNumber, sourceKind, confidence));
            }

            // Move currentStart forward, subtracting overlap
            currentStart = Math.Max(currentStart + 1, breakIndex - options.Overlap);
        }

        return chunks;
    }

    private static bool IsBoundary(char ch)
    {
        return Array.IndexOf(ArabicSentenceDelimiters, ch) >= 0;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LocalFileAgent.Domain.Text;

namespace LocalFileAgent.Application.Search;

public sealed record SnippetRun(string Text, bool IsHighlighted);

public static class SnippetHighlighter
{
    private static readonly char[] Separators = [' ', '\t', '\r', '\n', '،', '؛', ',', '.', '!', '؟', ':', '-', '\"', '\''];

    public static IReadOnlyList<SnippetRun> Highlight(string? rawSnippet, string? query, ITextNormalizer normalizer)
    {
        if (string.IsNullOrEmpty(rawSnippet))
        {
            return Array.Empty<SnippetRun>();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return [new SnippetRun(rawSnippet, false)];
        }

        var normRaw = normalizer.Normalize(rawSnippet, NormalizationProfile.Search);
        var normText = normRaw.ProcessedText;
        var offsets = normRaw.OffsetMap;

        if (string.IsNullOrEmpty(normText) || offsets == null || offsets.Count != normText.Length)
        {
            return [new SnippetRun(rawSnippet, false)];
        }

        var normQuery = normalizer.Normalize(query, NormalizationProfile.Search).ProcessedText;
        var queryTokens = normQuery
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (queryTokens.Count == 0)
        {
            // If all tokens were 1 character, take them anyway
            queryTokens = normQuery
                .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.Length >= 1)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (queryTokens.Count == 0)
            {
                return [new SnippetRun(rawSnippet, false)];
            }
        }

        var matchedRanges = new List<(int RawStart, int RawEnd)>();

        foreach (var token in queryTokens)
        {
            var searchIndex = 0;
            while (searchIndex < normText.Length)
            {
                var idx = normText.IndexOf(token, searchIndex, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    break;
                }

                var normStart = idx;
                var normEnd = idx + token.Length - 1;

                if (normStart < offsets.Count && normEnd < offsets.Count)
                {
                    var rawStart = offsets[normStart];
                    var rawEnd = offsets[normEnd];

                    if (rawStart <= rawEnd && rawEnd < rawSnippet.Length)
                    {
                        // Include any trailing non-spacing marks (tashkeel/tatweel) attached to the last char
                        while (rawEnd + 1 < rawSnippet.Length &&
                               (char.GetUnicodeCategory(rawSnippet[rawEnd + 1]) == UnicodeCategory.NonSpacingMark ||
                                rawSnippet[rawEnd + 1] == '\u0640'))
                        {
                            rawEnd++;
                        }

                        matchedRanges.Add((rawStart, rawEnd));
                    }
                }

                searchIndex = idx + 1;
            }
        }

        if (matchedRanges.Count == 0)
        {
            return [new SnippetRun(rawSnippet, false)];
        }

        // Sort and merge overlapping / adjacent ranges
        matchedRanges.Sort((a, b) => a.RawStart.CompareTo(b.RawStart));
        var merged = new List<(int RawStart, int RawEnd)>();
        var currentMerged = matchedRanges[0];

        for (var i = 1; i < matchedRanges.Count; i++)
        {
            var next = matchedRanges[i];
            if (next.RawStart <= currentMerged.RawEnd + 1)
            {
                currentMerged = (currentMerged.RawStart, Math.Max(currentMerged.RawEnd, next.RawEnd));
            }
            else
            {
                merged.Add(currentMerged);
                currentMerged = next;
            }
        }
        merged.Add(currentMerged);

        // Build runs
        var runs = new List<SnippetRun>();
        var currentIndex = 0;

        foreach (var (rawStart, rawEnd) in merged)
        {
            if (rawStart > currentIndex)
            {
                runs.Add(new SnippetRun(rawSnippet[currentIndex..rawStart], false));
            }

            runs.Add(new SnippetRun(rawSnippet[rawStart..(rawEnd + 1)], true));
            currentIndex = rawEnd + 1;
        }

        if (currentIndex < rawSnippet.Length)
        {
            runs.Add(new SnippetRun(rawSnippet[currentIndex..], false));
        }

        return runs;
    }
}

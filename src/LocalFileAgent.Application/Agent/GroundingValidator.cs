using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LocalFileAgent.Domain.Agent;
using LocalFileAgent.Domain.Search;

namespace LocalFileAgent.Application.Agent;

public sealed partial class GroundingValidator : IGroundingValidator
{
    // Regex for Windows absolute paths: either quoted "C:\path..." or unquoted without whitespace
    [GeneratedRegex(@"""([a-zA-Z]:\\[^""\r\n<>|]+)""|([a-zA-Z]:\\(?:[^\\/:*?""<>|\s\r\n]+\\)*[^\\/:*?""<>|\s\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsPathRegex();

    // Regex for citation brackets like [1], [2], [ص 3]
    [GeneratedRegex(@"\[(?:مصدر\s*|صورة\s*|ملف\s*)?(\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex CitationBracketRegex();

    public GroundingValidationResult Validate(
        string generatedText,
        IReadOnlyList<SearchResultItem> toolHits)
    {
        if (string.IsNullOrWhiteSpace(generatedText))
        {
            return new GroundingValidationResult(
                IsValid: true,
                HallucinatedPaths: Array.Empty<string>(),
                VerifiedCitations: Array.Empty<SearchResultItem>(),
                SanitizedResponseText: string.Empty
            );
        }

        toolHits ??= Array.Empty<SearchResultItem>();

        var validFilePaths = new HashSet<string>(toolHits.Select(h => h.FilePath), StringComparer.OrdinalIgnoreCase);
        var validFileNames = new HashSet<string>(toolHits.Select(h => h.FileName), StringComparer.OrdinalIgnoreCase);

        var hallucinatedPaths = new List<string>();
        var verifiedCitations = new HashSet<SearchResultItem>();

        // 1. Validate full file paths mentioned in text
        var pathMatches = WindowsPathRegex().Matches(generatedText);
        foreach (Match m in pathMatches)
        {
            var raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            var path = raw.Trim('\u2066', '\u2067', '\u2068', '\u2069', ' ', '"', '\'')
                          .TrimEnd('.', '،', '؛', ',', ')', ']');

            if (!validFilePaths.Contains(path))
            {
                // Only consider it a path hallucination if it has a file extension or looks like a file path
                if (Path.HasExtension(path))
                {
                    hallucinatedPaths.Add(path);
                }
            }
            else
            {
                var hit = toolHits.FirstOrDefault(h => h.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (hit != null)
                {
                    verifiedCitations.Add(hit);
                }
            }
        }

        // 2. Validate bracket citation numbers like [1], [2]
        var citationMatches = CitationBracketRegex().Matches(generatedText);
        foreach (Match m in citationMatches)
        {
            if (int.TryParse(m.Groups[1].Value, out var index))
            {
                var zeroIndex = index - 1;
                if (zeroIndex >= 0 && zeroIndex < toolHits.Count)
                {
                    verifiedCitations.Add(toolHits[zeroIndex]);
                }
                else
                {
                    hallucinatedPaths.Add($"[Citation index out of range: {index}]");
                }
            }
        }

        // 3. Match file names mentioned in text
        foreach (var hit in toolHits)
        {
            if (generatedText.Contains(hit.FileName, StringComparison.OrdinalIgnoreCase))
            {
                verifiedCitations.Add(hit);
            }
        }

        // 4. Sanitize response text if hallucinations were detected
        var sanitizedText = generatedText;
        if (hallucinatedPaths.Count > 0)
        {
            foreach (var fake in hallucinatedPaths)
            {
                if (!fake.StartsWith('[') && sanitizedText.Contains(fake, StringComparison.OrdinalIgnoreCase))
                {
                    sanitizedText = sanitizedText.Replace(fake, "[مسار غير موثق محذوف]", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        var isValid = hallucinatedPaths.Count == 0;

        return new GroundingValidationResult(
            IsValid: isValid,
            HallucinatedPaths: hallucinatedPaths,
            VerifiedCitations: verifiedCitations.ToList(),
            SanitizedResponseText: sanitizedText
        );
    }
}

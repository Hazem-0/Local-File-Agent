using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LocalFileAgent.Domain.Text;

namespace LocalFileAgent.Text;

public sealed partial class QueryAnalyzer : IQueryAnalyzer
{
    private readonly ArabicTextNormalizer _normalizer = new();

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Arabic particles & dialect words
        "في", "من", "على", "الى", "إلى", "عن", "مع", "هذا", "هذه", "التي", "الذي", "ان", "أن",
        "عايز", "بتاع", "بتاعة", "بتاعت", "اللي", "ده", "دي", "فين", "ليه", "كام",
        // English stop words
        "the", "a", "an", "in", "on", "at", "for", "with", "from", "of", "to", "and", "or", "is", "find"
    };

    public QueryAnalysis Analyze(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new QueryAnalysis(string.Empty, string.Empty, DetectedLanguageKind.Arabic, 0f, 0f, false, Array.Empty<string>());
        }

        var normalized = _normalizer.Normalize(query, NormalizationProfile.Search).ProcessedText;

        var arabicLetters = 0;
        var latinLetters = 0;
        var totalLetters = 0;
        var containsDigits = false;

        foreach (var ch in query)
        {
            if (char.IsDigit(ch) || (ch >= '\u0660' && ch <= '\u0669') || (ch >= '\u06F0' && ch <= '\u06F9'))
            {
                containsDigits = true;
            }

            if (char.IsLetter(ch))
            {
                totalLetters++;
                if (ch >= '\u0600' && ch <= '\u06FF')
                {
                    arabicLetters++;
                }
                else if (char.IsAsciiLetter(ch))
                {
                    latinLetters++;
                }
            }
        }

        var arabicRatio = totalLetters == 0 ? 0f : (float)arabicLetters / totalLetters;
        var latinRatio = totalLetters == 0 ? 0f : (float)latinLetters / totalLetters;

        var language = DetermineLanguage(query, arabicRatio, latinRatio);
        var keyTerms = ExtractKeyTerms(normalized);

        return new QueryAnalysis(
            RawQuery: query,
            NormalizedQuery: normalized,
            Language: language,
            ArabicScriptRatio: arabicRatio,
            LatinScriptRatio: latinRatio,
            ContainsDigits: containsDigits,
            KeyTerms: keyTerms
        );
    }

    private static DetectedLanguageKind DetermineLanguage(string query, float arabicRatio, float latinRatio)
    {
        if (arabicRatio > 0.70f)
        {
            return DetectedLanguageKind.Arabic;
        }

        if (latinRatio > 0.70f)
        {
            if (IsArabizi(query))
            {
                return DetectedLanguageKind.Arabizi;
            }
            return DetectedLanguageKind.English;
        }

        if (arabicRatio > 0.15f && latinRatio > 0.15f)
        {
            return DetectedLanguageKind.Mixed;
        }

        return DetectedLanguageKind.Arabic;
    }

    private static bool IsArabizi(string text)
    {
        // Arabizi: Latin script with numbers representing Arabic phonemes:
        // 2 (ء/أ), 3 (ع), 5 (خ), 7 (ح), 8 (غ), or common Arabizi particles
        if (ArabiziDigitPattern().IsMatch(text))
        {
            return true;
        }

        var words = text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var arabiziParticleCount = 0;
        foreach (var word in words)
        {
            if (word is "el" or "elly" or "bta3" or "bta3et" or "msh" or "mesh" or "fe" or "fi" or "men" or "3la" or "3an")
            {
                arabiziParticleCount++;
            }
        }

        return arabiziParticleCount >= 2;
    }

    private static List<string> ExtractKeyTerms(string normalized)
    {
        var tokens = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var terms = new List<string>();

        foreach (var token in tokens)
        {
            if (token.Length >= 2 && !StopWords.Contains(token))
            {
                terms.Add(token);
            }
        }

        return terms;
    }

    [GeneratedRegex(@"\b[a-zA-Z]*[23578][a-zA-Z]+\b|\b[a-zA-Z]+[23578][a-zA-Z]*\b")]
    private static partial Regex ArabiziDigitPattern();
}

using System;
using System.Text.RegularExpressions;
using LocalFileAgent.Domain.Text;

namespace LocalFileAgent.Text;

public sealed partial class TextQualityGate : ITextQualityGate
{
    private const int DefaultMinChars = 50;
    private const float MinPrintableRatio = 0.90f;
    private const float MaxCidOrReplacementRatio = 0.05f;
    private const float MaxIsolatedSingleLetterRatio = 0.30f;

    public QualityScore Score(string text, LanguageHint hint)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new QualityScore(0f, false, "Text is empty or whitespace only");
        }

        var trimmed = text.Trim();

        // 1. Minimum character length check
        if (trimmed.Length < DefaultMinChars)
        {
            return new QualityScore(0.2f, false, $"Insufficient character count: {trimmed.Length} < {DefaultMinChars}");
        }

        // 2. Printable character ratio check
        var printableCount = 0;
        foreach (var ch in trimmed)
        {
            if (!char.IsControl(ch) || ch == '\r' || ch == '\n' || ch == '\t')
            {
                printableCount++;
            }
        }
        var printableRatio = (float)printableCount / trimmed.Length;
        if (printableRatio < MinPrintableRatio)
        {
            return new QualityScore(printableRatio, false, $"Low printable ratio: {printableRatio:P1} < {MinPrintableRatio:P1}");
        }

        // 3. Replacement, Private-Use Area (PUA), or (cid:N) glyph ratio check
        var cidMatches = CidPattern().Count(trimmed);
        var puaOrReplacementCount = 0;
        foreach (var ch in trimmed)
        {
            // PUA: U+E000..U+F8FF, Replacement: U+FFFD
            if ((ch >= '\uE000' && ch <= '\uF8FF') || ch == '\uFFFD')
            {
                puaOrReplacementCount++;
            }
        }
        var totalDefectiveTokens = puaOrReplacementCount + (cidMatches * 7); // approximate cid tag length
        var defectRatio = (float)totalDefectiveTokens / trimmed.Length;
        if (defectRatio >= MaxCidOrReplacementRatio)
        {
            return new QualityScore(Math.Max(0f, 1f - defectRatio), false, $"High cid/replacement glyph ratio: {defectRatio:P1} >= {MaxCidOrReplacementRatio:P1}");
        }

        // 4. Broken isolated single-letter extraction check (e.g., "ف ا ت و ر ة")
        var tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var arabicTokenCount = 0;
        var singleLetterArabicCount = 0;

        foreach (var token in tokens)
        {
            if (ContainsArabic(token))
            {
                arabicTokenCount++;
                if (token.Length == 1)
                {
                    singleLetterArabicCount++;
                }
            }
        }

        if (arabicTokenCount >= 10)
        {
            var singleLetterRatio = (float)singleLetterArabicCount / arabicTokenCount;
            if (singleLetterRatio >= MaxIsolatedSingleLetterRatio)
            {
                return new QualityScore(Math.Max(0f, 1f - singleLetterRatio), false, $"High isolated single letter ratio: {singleLetterRatio:P1} >= {MaxIsolatedSingleLetterRatio:P1}");
            }
        }

        // 5. Compute aggregate quality score
        var score = Math.Clamp(printableRatio - defectRatio, 0f, 1f);
        return new QualityScore(score, true, "Text layer passes quality gate");
    }

    private static bool ContainsArabic(string s)
    {
        foreach (var ch in s)
        {
            if (ch >= '\u0600' && ch <= '\u06FF')
            {
                return true;
            }
        }
        return false;
    }

    [GeneratedRegex(@"\(cid:\d+\)", RegexOptions.IgnoreCase)]
    private static partial Regex CidPattern();
}

using System;
using System.Collections.Generic;
using System.Text;
using LocalFileAgent.Domain.Text;

namespace LocalFileAgent.Text;

public sealed class ArabicOrderFixer : IArabicOrderFixer
{
    private static readonly (string Logical, string Reversed)[] FunctionWordPairs = new[]
    {
        ("في", "يف"),
        ("من", "نم"),
        ("على", "ىلع"),
        ("إلى", "ىلا"),
        ("الي", "يلا"),
        ("هذا", "اذه"),
        ("هذه", "هذه"), // Palindrome, omit
        ("التي", "يتلا"),
        ("الذي", "يذلا"),
        ("عن", "نع"),
        ("مع", "عم"),
        ("أن", "نأ"),
        ("ان", "نا"),
        ("كان", "ناك"),
        ("كل", "لك"),
        ("بين", "نيب"),
        ("بعد", "دعب"),
        ("قبل", "لبق")
    };

    public OrderReport Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new OrderReport(false, 0f, "Empty text");
        }

        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var logicalHits = 0;
        var reversedHits = 0;

        foreach (var token in tokens)
        {
            var cleanToken = CleanToken(token);
            foreach (var (logical, reversed) in FunctionWordPairs)
            {
                if (cleanToken.Equals(logical, StringComparison.Ordinal))
                {
                    logicalHits++;
                }
                else if (cleanToken.Equals(reversed, StringComparison.Ordinal))
                {
                    reversedHits++;
                }
            }
        }

        var totalHits = logicalHits + reversedHits;
        if (totalHits == 0)
        {
            return new OrderReport(false, 0f, "No decisive functional words found");
        }

        if (reversedHits > logicalHits)
        {
            var confidence = (float)reversedHits / totalHits;
            return new OrderReport(true, confidence, $"Reversed hits: {reversedHits}, Logical hits: {logicalHits}");
        }

        return new OrderReport(false, (float)logicalHits / totalHits, $"Logical hits: {logicalHits}, Reversed hits: {reversedHits}");
    }

    public string Fix(string text, OrderReport report)
    {
        if (!report.IsReversed || string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Split into lines to preserve line structure
        var lines = text.Split('\n');
        var sb = new StringBuilder(text.Length);

        for (var l = 0; l < lines.Length; l++)
        {
            var line = lines[l].TrimEnd('\r');
            var tokens = line.Split(' ');
            var reversedLineTokens = new List<string>(tokens.Length);

            foreach (var token in tokens)
            {
                if (ContainsArabic(token))
                {
                    // Reverse the characters in the Arabic token to restore logical order
                    var chars = token.ToCharArray();
                    Array.Reverse(chars);
                    reversedLineTokens.Add(new string(chars));
                }
                else
                {
                    reversedLineTokens.Add(token);
                }
            }

            // In some reverse-ordered text, the word sequence in the line is also reversed
            sb.Append(string.Join(" ", reversedLineTokens));
            if (l < lines.Length - 1)
            {
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    private static string CleanToken(string token)
    {
        var sb = new StringBuilder(token.Length);
        foreach (var ch in token)
        {
            if (char.IsLetter(ch))
            {
                // Unify hamzas for matching
                if (ch is 'أ' or 'إ' or 'آ')
                {
                    sb.Append('ا');
                }
                else if (ch == 'ى')
                {
                    sb.Append('ي');
                }
                else
                {
                    sb.Append(ch);
                }
            }
        }
        return sb.ToString();
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
}

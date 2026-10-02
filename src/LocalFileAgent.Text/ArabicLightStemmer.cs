using System;
using LocalFileAgent.Domain.Text;

namespace LocalFileAgent.Text;

public sealed class ArabicLightStemmer : IArabicStemmer
{
    private const int MinStemLength = 3;

    public string Stem(string normalizedToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedToken) || normalizedToken.Length <= MinStemLength)
        {
            return normalizedToken ?? string.Empty;
        }

        var s = normalizedToken;

        // Step 1: Strip 3-letter prefixes (وال, بال, كال, فال)
        if (s.Length >= 6)
        {
            if (s.StartsWith("وال", StringComparison.Ordinal) ||
                s.StartsWith("بال", StringComparison.Ordinal) ||
                s.StartsWith("كال", StringComparison.Ordinal) ||
                s.StartsWith("فال", StringComparison.Ordinal))
            {
                s = s[3..];
            }
        }

        // Step 2: Strip 2-letter prefixes (ال, لل)
        if (s.Length >= 5)
        {
            if (s.StartsWith("ال", StringComparison.Ordinal) ||
                s.StartsWith("لل", StringComparison.Ordinal))
            {
                s = s[2..];
            }
        }

        // Step 3: Strip leading 'و' if length >= 4
        if (s.Length >= 4 && s.StartsWith('و'))
        {
            s = s[1..];
        }

        // Step 4: Strip 2-letter suffixes (ها, ان, ات, ون, ين, يه, ية)
        if (s.Length >= 5)
        {
            if (s.EndsWith("ها", StringComparison.Ordinal) ||
                s.EndsWith("ان", StringComparison.Ordinal) ||
                s.EndsWith("ات", StringComparison.Ordinal) ||
                s.EndsWith("ون", StringComparison.Ordinal) ||
                s.EndsWith("ين", StringComparison.Ordinal) ||
                s.EndsWith("يه", StringComparison.Ordinal) ||
                s.EndsWith("ية", StringComparison.Ordinal))
            {
                s = s[..^2];
            }
        }

        // Step 5: Strip 1-letter suffixes (ه, ي)
        if (s.Length >= 4)
        {
            if (s.EndsWith('ه') || s.EndsWith('ي'))
            {
                s = s[..^1];
            }
        }

        return s;
    }
}

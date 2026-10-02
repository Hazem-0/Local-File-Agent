using System;
using System.Collections.Generic;
using System.Text;

namespace LocalFileAgent.Text;

public sealed record RepetitionReport(
    bool IsRepetitive,
    int ConsecutiveRepetitions,
    string CleanedText,
    string DiagnosticDetails
);

public static class RepetitionDetector
{
    public static RepetitionReport Analyze(string text, int maxAllowedConsecutive = 3)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new RepetitionReport(false, 0, text ?? string.Empty, "Empty text");
        }

        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 4)
        {
            return new RepetitionReport(false, 0, text, "Text too short to exhibit runaway loop");
        }

        // 1. Check single-token runaway repetition (e.g., word word word word...)
        var maxTokenReps = 1;
        var currentTokenReps = 1;
        var loopTokenIndex = -1;

        for (var i = 1; i < tokens.Length; i++)
        {
            if (string.Equals(tokens[i], tokens[i - 1], StringComparison.OrdinalIgnoreCase))
            {
                currentTokenReps++;
                if (currentTokenReps > maxTokenReps)
                {
                    maxTokenReps = currentTokenReps;
                }
                if (currentTokenReps > maxAllowedConsecutive && loopTokenIndex == -1)
                {
                    loopTokenIndex = i - maxAllowedConsecutive;
                }
            }
            else
            {
                currentTokenReps = 1;
            }
        }

        if (maxTokenReps > maxAllowedConsecutive && loopTokenIndex >= 0)
        {
            var cleanedSb = new StringBuilder();
            for (var i = 0; i < loopTokenIndex + 1; i++)
            {
                if (i > 0) cleanedSb.Append(' ');
                cleanedSb.Append(tokens[i]);
            }

            return new RepetitionReport(
                IsRepetitive: true,
                ConsecutiveRepetitions: maxTokenReps,
                CleanedText: cleanedSb.ToString(),
                DiagnosticDetails: $"Detected single-word repetition loop of length {maxTokenReps}"
            );
        }

        // 2. Check phrase n-gram runaway repetition (n = 2, 3, 4)
        for (var n = 2; n <= 4; n++)
        {
            if (tokens.Length < n * 3) continue;

            for (var i = 0; i <= tokens.Length - (n * 3); i++)
            {
                var phrase1 = GetNGram(tokens, i, n);
                var phrase2 = GetNGram(tokens, i + n, n);
                var phrase3 = GetNGram(tokens, i + (n * 2), n);

                if (string.Equals(phrase1, phrase2, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(phrase2, phrase3, StringComparison.OrdinalIgnoreCase))
                {
                    // Found 3 consecutive identical n-grams
                    var cleanedSb = new StringBuilder();
                    for (var j = 0; j < i + n; j++)
                    {
                        if (j > 0) cleanedSb.Append(' ');
                        cleanedSb.Append(tokens[j]);
                    }

                    return new RepetitionReport(
                        IsRepetitive: true,
                        ConsecutiveRepetitions: 3,
                        CleanedText: cleanedSb.ToString(),
                        DiagnosticDetails: $"Detected {n}-gram repetition loop ('{phrase1}')"
                    );
                }
            }
        }

        return new RepetitionReport(false, 1, text, "Clean text");
    }

    private static string GetNGram(string[] tokens, int start, int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(tokens[start + i]);
        }
        return sb.ToString();
    }
}

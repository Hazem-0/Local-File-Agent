using System;
using System.Collections.Generic;

namespace LocalFileAgent.Domain.Text;

public enum NormalizationProfile
{
    Display,
    Search
}

public sealed record NormalizedText(
    string OriginalText,
    string ProcessedText,
    IReadOnlyList<int>? OffsetMap = null
);

public interface ITextNormalizer
{
    NormalizedText Normalize(string text, NormalizationProfile profile);
}

public sealed record DetectedText(
    string Text,
    string EncodingName,
    float Confidence
);

public interface IEncodingDetector
{
    DetectedText Decode(ReadOnlySpan<byte> bytes);
}

public sealed record OrderReport(
    bool IsReversed,
    float Confidence,
    string DiagnosticDetails
);

public interface IArabicOrderFixer
{
    OrderReport Analyze(string text);
    string Fix(string text, OrderReport report);
}

public interface IArabicStemmer
{
    string Stem(string normalizedToken);
}

public sealed record QualityScore(
    float Score,
    bool Passed,
    string Reason
);

public sealed record LanguageHint(
    string? ExpectedLanguage = null
);

public interface ITextQualityGate
{
    QualityScore Score(string text, LanguageHint hint);
}

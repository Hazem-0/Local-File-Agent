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

public sealed record Chunk(
    string Text,
    int Ordinal,
    int StartCharIndex,
    int EndCharIndex,
    int PageNumber = 1,
    string SourceKind = "text_layer",
    float Confidence = 1.0f
);

public sealed record ChunkingOptions(
    int TargetChunkSize = 1200,
    int MaxChunkSize = 1600,
    int Overlap = 150
);

public interface IChunker
{
    IReadOnlyList<Chunk> Chunk(string text, ChunkingOptions options, int pageNumber = 1, string sourceKind = "text_layer", float confidence = 1.0f);
}

public enum DetectedLanguageKind
{
    Arabic,
    English,
    Mixed,
    Arabizi
}

public sealed record QueryAnalysis(
    string RawQuery,
    string NormalizedQuery,
    DetectedLanguageKind Language,
    float ArabicScriptRatio,
    float LatinScriptRatio,
    bool ContainsDigits,
    IReadOnlyList<string> KeyTerms
);

public interface IQueryAnalyzer
{
    QueryAnalysis Analyze(string query);
}

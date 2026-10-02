using FluentAssertions;
using LocalFileAgent.Domain.Text;
using Xunit;

namespace LocalFileAgent.Text.Tests;

public class QueryAnalyzerTests
{
    private readonly QueryAnalyzer _analyzer = new();

    [Fact]
    // Pure Arabic query
    public void Analyze_PureArabicQuery_IdentifiesArabic()
    {
        var query = "عايز فاتورة الكهرباء بتاعت الشهر اللي فات";
        var result = _analyzer.Analyze(query);

        result.Language.Should().Be(DetectedLanguageKind.Arabic);
        result.ArabicScriptRatio.Should().BeGreaterThan(0.8f);
        result.KeyTerms.Should().NotBeEmpty();
    }

    [Fact]
    // Pure English query
    public void Analyze_PureEnglishQuery_IdentifiesEnglish()
    {
        var query = "find annual financial report for 2025";
        var result = _analyzer.Analyze(query);

        result.Language.Should().Be(DetectedLanguageKind.English);
        result.LatinScriptRatio.Should().BeGreaterThan(0.8f);
        result.ContainsDigits.Should().BeTrue();
    }

    [Fact]
    // Mixed Arabic and English
    public void Analyze_MixedQuery_IdentifiesMixed()
    {
        var query = "ملف PDF يحتوي على Invoice رقم 123";
        var result = _analyzer.Analyze(query);

        result.Language.Should().Be(DetectedLanguageKind.Mixed);
        result.ArabicScriptRatio.Should().BeGreaterThan(0.2f);
        result.LatinScriptRatio.Should().BeGreaterThan(0.2f);
    }

    [Fact]
    // Arabizi query (Arabic transliterated in Latin script with digits like 2, 3, 5, 7, 8)
    public void Analyze_ArabiziQuery_IdentifiesArabizi()
    {
        var query = "3ayez el fatoura bta3et el shaher elly fat";
        var result = _analyzer.Analyze(query);

        result.Language.Should().Be(DetectedLanguageKind.Arabizi);
    }

    [Fact]
    // Digit normalization in query
    public void Analyze_QueryWithArabicDigits_NormalizesDigits()
    {
        var query = "عقد بيع لسنة ٢٠٢٤ في القاهرة";
        var result = _analyzer.Analyze(query);

        result.NormalizedQuery.Should().Contain("2024");
        result.ContainsDigits.Should().BeTrue();
    }
}

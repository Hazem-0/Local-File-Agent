using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LocalFileAgent.Application.Search;
using LocalFileAgent.Text;
using Xunit;

namespace LocalFileAgent.Application.Tests;

public class SnippetHighlighterTests
{
    private readonly ArabicTextNormalizer _normalizer = new();

    [Fact]
    public void Highlight_ExactArabicWord_HighlightsCorrectly()
    {
        var rawSnippet = "تم توقيع عقد الإيجار في مدينة نصر";
        var query = "عقد";

        var runs = SnippetHighlighter.Highlight(rawSnippet, query, _normalizer);

        runs.Should().NotBeEmpty();
        runs.Where(r => r.IsHighlighted).Select(r => r.Text).Should().Contain("عقد");
        string.Concat(runs.Select(r => r.Text)).Should().Be(rawSnippet);
    }

    [Fact]
    public void Highlight_WithTashkeelInRawText_MapsOffsetsAndPreservesTashkeel()
    {
        var rawSnippet = "هَذِهِ فَاتُورَةٌ ضَرِيبِيَّةٌ مُعْتَمَدَةٌ";
        var query = "فاتورة";

        var runs = SnippetHighlighter.Highlight(rawSnippet, query, _normalizer);

        runs.Should().NotBeEmpty();
        var highlighted = runs.FirstOrDefault(r => r.IsHighlighted);
        highlighted.Should().NotBeNull();
        highlighted!.Text.Should().Be("فَاتُورَةٌ");
        string.Concat(runs.Select(r => r.Text)).Should().Be(rawSnippet);
    }

    [Fact]
    public void Highlight_WithAlefAndDigitVariants_MatchesAccurately()
    {
        var rawSnippet = "سداد قيمة إيجار سنة 2025 بمبلغ محدد";
        var query = "ايجار ٢٠٢٥";

        var runs = SnippetHighlighter.Highlight(rawSnippet, query, _normalizer);

        var highlightedParts = runs.Where(r => r.IsHighlighted).Select(r => r.Text).ToList();
        highlightedParts.Should().Contain("إيجار");
        highlightedParts.Should().Contain("2025");
        string.Concat(runs.Select(r => r.Text)).Should().Be(rawSnippet);
    }

    [Fact]
    public void Highlight_WhenQueryNotFound_ReturnsSingleUnmarkedRun()
    {
        var rawSnippet = "تقرير مالي سنوي للشركة";
        var query = "سيارات";

        var runs = SnippetHighlighter.Highlight(rawSnippet, query, _normalizer);

        runs.Should().HaveCount(1);
        runs[0].IsHighlighted.Should().BeFalse();
        runs[0].Text.Should().Be(rawSnippet);
    }

    [Fact]
    public void Highlight_WhenEmptyOrNull_HandlesSafely()
    {
        SnippetHighlighter.Highlight(null, "بحث", _normalizer).Should().BeEmpty();
        SnippetHighlighter.Highlight("", "بحث", _normalizer).Should().BeEmpty();

        var runs = SnippetHighlighter.Highlight("نص عادي", "", _normalizer);
        runs.Should().HaveCount(1);
        runs[0].IsHighlighted.Should().BeFalse();
        runs[0].Text.Should().Be("نص عادي");
    }
}

using FluentAssertions;
using LocalFileAgent.Text;
using Xunit;

namespace LocalFileAgent.Text.Tests;

public class RepetitionDetectorTests
{
    [Fact]
    public void Analyze_NormalText_ReturnsNotRepetitive()
    {
        var text = "فاتورة ضريبية رسمية لتوريد أجهزة حاسوب صادرة من شركة النيل";
        var report = RepetitionDetector.Analyze(text);

        report.IsRepetitive.Should().BeFalse();
        report.CleanedText.Should().Be(text);
    }

    [Fact]
    public void Analyze_SingleWordRunawayLoop_DetectsAndCleans()
    {
        var text = "المبلغ الإجمالي خمسة آلاف جنيه فاتورة فاتورة فاتورة فاتورة فاتورة فاتورة";
        var report = RepetitionDetector.Analyze(text);

        report.IsRepetitive.Should().BeTrue();
        report.DiagnosticDetails.Should().Contain("single-word repetition loop");
        report.CleanedText.Should().Be("المبلغ الإجمالي خمسة آلاف جنيه فاتورة");
    }

    [Fact]
    public void Analyze_NGramPhraseLoop_DetectsAndCleans()
    {
        var text = "مستند رسمي رقم الفاتورة رقم الفاتورة رقم الفاتورة";
        var report = RepetitionDetector.Analyze(text);

        report.IsRepetitive.Should().BeTrue();
        report.DiagnosticDetails.Should().Contain("2-gram repetition loop");
        report.CleanedText.Should().Be("مستند رسمي رقم الفاتورة");
    }

    [Fact]
    public void Analyze_ShortText_HandlesSafely()
    {
        var report = RepetitionDetector.Analyze("فاتورة فقط");
        report.IsRepetitive.Should().BeFalse();
    }
}

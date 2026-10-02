using System;
using FluentAssertions;
using LocalFileAgent.Domain.Text;
using Xunit;

namespace LocalFileAgent.Text.Tests;

public class ArabicOrderFixerAndQualityGateTests
{
    private readonly ArabicOrderFixer _orderFixer = new();
    private readonly TextQualityGate _qualityGate = new();

    [Fact]
    // AR-3: Reversed text detection
    public void OrderFixer_DetectsReversedArabicText()
    {
        // Normal: "تقرير المبيعات في القاهرة من الشركة"
        // Reversed letters per word or reversed line:
        // "يف" instead of "في", "نم" instead of "من", "ىلع" instead of "على"
        var reversedText = "ريكرت تاعيبمل يف ةرهاقل نم ةكرشل";
        var report = _orderFixer.Analyze(reversedText);

        report.IsReversed.Should().BeTrue();
        report.Confidence.Should().BeGreaterThan(0.7f);
    }

    [Fact]
    // AR-3: Logical text is recognized as NOT reversed
    public void OrderFixer_RecognizesLogicalArabicText()
    {
        var logicalText = "تقرير المبيعات في القاهرة من الشركة إلى العميل مع فحص الحساب";
        var report = _orderFixer.Analyze(logicalText);

        report.IsReversed.Should().BeFalse();
    }

    [Fact]
    // AR-3: Reversing the order fixes the words
    public void OrderFixer_FixesReversedWords()
    {
        // Words with reversed characters: "يف" -> "في", "نم" -> "من", "ىلع" -> "على"
        var reversedInput = "يف نم ىلع اذه";
        var report = _orderFixer.Analyze(reversedInput);
        var fixedText = _orderFixer.Fix(reversedInput, report);

        fixedText.Should().Be("في من على هذا");
    }

    [Fact]
    // Quality Gate: Valid digital text passes
    public void QualityGate_ValidArabicDigitalText_Passes()
    {
        var text = "تم توقيع العقد النهائي بين الطرفين في مدينة القاهرة بتاريخ اليوم بحضور الشهود.";
        var result = _qualityGate.Score(text, new LanguageHint("ar"));

        result.Passed.Should().BeTrue();
        result.Score.Should().BeGreaterThanOrEqualTo(0.8f);
    }

    [Fact]
    // AR-3: Too short text fails minimum character check
    public void QualityGate_TooShortText_Fails()
    {
        var text = "نص قصير";
        var result = _qualityGate.Score(text, new LanguageHint("ar"));

        result.Passed.Should().BeFalse();
        result.Reason.Should().Contain("character count");
    }

    [Fact]
    // AR-3: High (cid:N) or PUA/replacement glyph ratio fails
    public void QualityGate_HighCidOrPuaRatio_Fails()
    {
        var text = "شركة (cid:12) (cid:13) (cid:14) (cid:15) (cid:16) (cid:17) (cid:18) (cid:19) للتجارة والتوزيع المعتمدة في مصر";
        var result = _qualityGate.Score(text, new LanguageHint("ar"));

        result.Passed.Should().BeFalse();
        result.Reason.Should().Contain("cid");
    }

    [Fact]
    // AR-3: Disconnected / isolated single character runs fail
    public void QualityGate_IsolatedSingleLetterRuns_Fails()
    {
        // Broken PDF extraction where Arabic letters are isolated with spaces: "ف ا ت و ر ة"
        var brokenText = "ف ا ت و ر ة   م ب ي ع ا ت   ش ر ك ة   ا ل ق ا ه ر ة   م ص ر   ت ق ر ي ر";
        var result = _qualityGate.Score(brokenText, new LanguageHint("ar"));

        result.Passed.Should().BeFalse();
        result.Reason.Should().Contain("isolated");
    }

    [Fact]
    // AR-3: Low printable ratio fails
    public void QualityGate_LowPrintableRatio_Fails()
    {
        var unprintable = "نص عربي يحتوي على تحكم \u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008 وغير قابل للقراءة بشكل سليم";
        var result = _qualityGate.Score(unprintable, new LanguageHint("ar"));

        result.Passed.Should().BeFalse();
    }
}

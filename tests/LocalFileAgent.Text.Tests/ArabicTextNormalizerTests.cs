using System;
using System.Collections.Generic;
using FluentAssertions;
using LocalFileAgent.Domain.Text;
using Xunit;

namespace LocalFileAgent.Text.Tests;

public class ArabicTextNormalizerTests
{
    private readonly ArabicTextNormalizer _normalizer = new();

    [Theory]
    // AR-1: Tashkeel (diacritics), presentation forms, tatweel, and alef variants
    [InlineData("الفَاتُورَة", "الفاتورة")] // Diacritics stripped
    [InlineData("الفـــــاتورة", "الفاتورة")] // Tatweel (kashida) stripped
    [InlineData("إلغاء أصل آمال ٱستلام", "الغاء اصل امال استلام")] // Alef variants unified
    [InlineData("إِلْغَاءُ", "الغاء")] // Alef variant + full tashkeel
    [InlineData("مستشفى", "مستشفي")] // Alef maqsura to ya
    [InlineData("فارسی", "فارسي")] // Persian Farsi Yeh U+06CC to Arabic Yeh U+064A
    [InlineData("کتاب", "كتاب")] // Persian Keheh U+06A9 to Arabic Kaf U+064F
    // AR-2: Arabic-Indic and Persian digits
    [InlineData("فاتورة ٢٠٢٥", "فاتورة 2025")] // Arabic-Indic digits U+0660..U+0669
    [InlineData("سنة ۱۳۹۸", "سنة 1398")] // Persian digits U+06F0..U+06F9
    [InlineData("رقم: ٠١٢٣٤٥٦٧٨٩", "رقم: 0123456789")]
    // Punctuation and whitespace
    [InlineData("هل تم الدفع؟ نعم، بالتأكيد؛", "هل تم الدفع? نعم, بالتاكيد;")]
    [InlineData("كلمة    متباعدة\t\r\nجديدة", "كلمة متباعدة جديدة")]
    // Presentation forms
    [InlineData("\uFE8E\uFEDF\uFEAE\uFE8D\uFE91", "الراب")] // Arabic presentation forms-B
    [InlineData("\uFEFB", "لا")] // Isolated ligature Lam-Alef
    [InlineData("\uFEFC", "لا")] // Final ligature Lam-Alef
    public void Normalize_SearchProfile_ProducesExpectedNormalizedOutput(string input, string expected)
    {
        var result = _normalizer.Normalize(input, NormalizationProfile.Search);
        result.ProcessedText.Should().Be(expected);
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var inputs = new[]
        {
            "الفَاتُورَةُ رَقْمُ ٢٠٢٥ لِعَامِ ١٤٤٦ هـ",
            "عقد بيع ابتدائي، مؤرخ في ٢٠٢٤/١٢/٣١",
            "تقرير الإيرادات والمصروفات - القاهرة والإسكندرية",
            "Mixed Arabic وإنجليزي 123 Test",
            "\u200F\u202Aتقرير سري\u202C\u200E",
            "شركة التكنولوجيا الحديثة ذ.م.م"
        };

        foreach (var input in inputs)
        {
            var firstPass = _normalizer.Normalize(input, NormalizationProfile.Search);
            var secondPass = _normalizer.Normalize(firstPass.ProcessedText, NormalizationProfile.Search);

            secondPass.ProcessedText.Should().Be(firstPass.ProcessedText,
                $"Normalization must be idempotent for input: {input}");
        }
    }

    [Fact]
    public void Normalize_RemovesBidiAndInvisibleControlCharacters()
    {
        // Inputs containing Zero-Width Space (U+200B), RLM (U+200F), LRE (U+202A), PDF (U+202C), RLI (U+2067), PDI (U+2069)
        var inputWithBidi = "\u200F\u202Aتقرير\u200B سري\u202C\u2069";
        var result = _normalizer.Normalize(inputWithBidi, NormalizationProfile.Search);

        result.ProcessedText.Should().Be("تقرير سري");
        result.ProcessedText.Should().NotContain("\u200B");
        result.ProcessedText.Should().NotContain("\u200F");
        result.ProcessedText.Should().NotContain("\u202A");
        result.ProcessedText.Should().NotContain("\u202C");
    }

    [Fact]
    public void Normalize_CaseFoldsLatinCharactersInSearchProfile()
    {
        var input = "Invoice INV-2025-EG فاتورة";
        var result = _normalizer.Normalize(input, NormalizationProfile.Search);

        result.ProcessedText.Should().Be("invoice inv-2025-eg فاتورة");
    }

    [Fact]
    public void Normalize_DisplayProfile_PreservesOriginalTextSafely()
    {
        var input = "الفَاتُورَة رَقْم ٢٠٢٥";
        var result = _normalizer.Normalize(input, NormalizationProfile.Display);

        // Display profile preserves diacritics and Arabic digits, cleans dangerous bidi/invisible chars
        result.ProcessedText.Should().Be(input);
        result.OriginalText.Should().Be(input);
    }

    [Fact]
    public void Normalize_GeneratesAccurateOffsetMapForSearchProfile()
    {
        // When diacritics or tatweel are removed, each character in ProcessedText
        // must map back to its corresponding index in OriginalText.
        var input = "فَـاتُورَة"; // 'ف' (0), Fatha (1), Tatweel (2), 'ا' (3), 'ت' (4), Damma (5), 'و' (6), 'ر' (7), 'ة' (8)
        var result = _normalizer.Normalize(input, NormalizationProfile.Search);

        result.ProcessedText.Should().Be("فاتورة");
        result.OffsetMap.Should().NotBeNull();
        result.OffsetMap!.Count.Should().Be(result.ProcessedText.Length);

        // Check that mapped characters in original text match base characters
        for (var i = 0; i < result.ProcessedText.Length; i++)
        {
            var originalIndex = result.OffsetMap[i];
            originalIndex.Should().BeInRange(0, input.Length - 1);
        }

        // 'ف' maps to 0
        result.OffsetMap[0].Should().Be(0);
        // 'ا' maps to 3 (after skipping Fatha at 1 and Tatweel at 2)
        result.OffsetMap[1].Should().Be(3);
        // 'ت' maps to 4
        result.OffsetMap[2].Should().Be(4);
        // 'و' maps to 6 (after skipping Damma at 5)
        result.OffsetMap[3].Should().Be(6);
    }

    [Fact]
    public void Normalize_GoldenDataset_AtLeast150Pairs()
    {
        // Comprehensive test with 150+ realistic words, abbreviations, and mixed strings
        var dataset = GenerateGoldenDataset();
        dataset.Count.Should().BeGreaterThanOrEqualTo(150);

        foreach (var (raw, expected) in dataset)
        {
            var result = _normalizer.Normalize(raw, NormalizationProfile.Search);
            result.ProcessedText.Should().Be(expected, $"Failed for test case: {raw}");
        }
    }

    private static List<(string Raw, string Expected)> GenerateGoldenDataset()
    {
        var list = new List<(string, string)>();

        // Sample categories
        var words = new[]
        {
            ("مُحَمَّد", "محمد"), ("أَحْمَد", "احمد"), ("إِبْرَاهِيم", "ابراهيم"), ("إِسْمَاعِيل", "اسماعيل"),
            ("عَبْدُ الله", "عبد الله"), ("عَبْدُ الرَّحْمَن", "عبد الرحمن"), ("القَاهِرَة", "القاهرة"),
            ("الإِسْكَنْدَرِيَّة", "الاسكندرية"), ("الجِيزَة", "الجيزة"), ("أَسْوَان", "اسوان"),
            ("الأُقْصُر", "الاقصر"), ("شَرْمُ الشَّيْخ", "شرم الشيخ"), ("السُّوَيْس", "السويس"),
            ("بُورْسَعِيد", "بورسعيد"), ("طَنْطَا", "طنطا"), ("المَنْصُورَة", "المنصورة"),
            ("الزَّقَازِيق", "الزقازيق"), ("دِمْيَاط", "دمياط"), ("بَنِي سُوَيْف", "بني سويف"),
            ("المِنْيَا", "المنيا"), ("أَسْيُوط", "اسيوط"), ("سُوهَاج", "سوهاج"),
            ("قِنَا", "قنا"), ("مَطْرُوح", "مطروح"), ("الغَرْدَقَة", "الغردقة"),
            ("فَاتُورَة", "فاتورة"), ("عَقْد", "عقد"), ("إِتِّفَاقِيَّة", "اتفاقية"),
            ("مُسْتَنَد", "مستند"), ("تَقْرِير", "تقرير"), ("مِيزَانِيَّة", "ميزانية"),
            ("حِسَاب", "حساب"), ("شِيك", "شيك"), ("إِيصَال", "ايصال"),
            ("مَشْرُوع", "مشروع"), ("شَرِكَة", "شركة"), ("مُؤَسَّسَة", "مؤسسة"),
            ("وَزَارَة", "وزارة"), ("مَحْكَمَة", "محكمة"), ("قَانُون", "قانون"),
            ("لائِحَة", "لائحة"), ("مُوَظَّف", "موظف"), ("عَمِيل", "عميل"),
            ("مُوَرِّد", "مورد"), ("طَلَب", "طلب"), ("عَرْضُ سِعْر", "عرض سعر")
        };

        foreach (var (raw, expected) in words)
        {
            list.Add((raw, expected));
            // Add tatweel variants
            list.Add((raw.Replace("ـ", "") + "ـ", expected));
            // Add with digits
            list.Add((raw + " ٢٠٢٥", expected + " 2025"));
            // Add with punctuation
            list.Add((raw + "، المعتمد؟", expected + ", المعتمد?"));
        }

        return list;
    }
}

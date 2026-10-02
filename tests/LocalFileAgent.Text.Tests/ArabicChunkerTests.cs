using System.Linq;
using FluentAssertions;
using LocalFileAgent.Domain.Text;
using Xunit;

namespace LocalFileAgent.Text.Tests;

public class ArabicChunkerTests
{
    private readonly ArabicChunker _chunker = new();

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var text = "تقرير الإيرادات لشهر أكتوبر في القاهرة. كل شيء معتمد.";
        var chunks = _chunker.Chunk(text, new ChunkingOptions(TargetChunkSize: 500, MaxChunkSize: 800, Overlap: 50));

        chunks.Should().HaveCount(1);
        chunks[0].Text.Should().Be(text);
        chunks[0].Ordinal.Should().Be(0);
        chunks[0].PageNumber.Should().Be(1);
    }

    [Fact]
    public void Chunk_LongText_SplitsAtArabicSentencePunctuation()
    {
        // Construct sentences separated by Arabic punctuation: '،', '؟', '!', '؛', '.'
        var sentences = new[]
        {
            "تم انعقاد اجتماع مجلس الإدارة في مقر الشركة الرئيسي بالقاهرة الجديدة،",
            "وقد ناقش الحاضرون خطة التوسع في المشروعات التكنولوجية للعام المالي الجديد؛",
            "كما تم استعراض تقارير المبيعات ربع السنوية بحضور كافة الشركاء والمستثمرين.",
            "هل تم اعتماد الميزانية العمومية بشكل رسمي من مراقب الحسابات المستقل؟",
            "نعم، تمت الموافقة بالإجماع وبدون أي تحفظات على القوائم المالية المرفقة!"
        };

        var repeatedText = string.Join(" ", Enumerable.Repeat(string.Join(" ", sentences), 10));
        var options = new ChunkingOptions(TargetChunkSize: 400, MaxChunkSize: 600, Overlap: 50);

        var chunks = _chunker.Chunk(repeatedText, options);

        chunks.Count.Should().BeGreaterThan(1);
        foreach (var chunk in chunks)
        {
            chunk.Text.Length.Should().BeLessThanOrEqualTo(options.MaxChunkSize);
            chunk.Text.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Chunk_PreservesMetadataAndProvenance()
    {
        var text = "نص مستخرج من تقنية التعرف الضوئي على الحروف.";
        var chunks = _chunker.Chunk(text, new ChunkingOptions(), pageNumber: 7, sourceKind: "ocr", confidence: 0.88f);

        chunks.Should().HaveCount(1);
        chunks[0].PageNumber.Should().Be(7);
        chunks[0].SourceKind.Should().Be("ocr");
        chunks[0].Confidence.Should().Be(0.88f);
    }
}

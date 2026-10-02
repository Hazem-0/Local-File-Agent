using System;
using System.Text;
using FluentAssertions;
using LocalFileAgent.Domain.Text;
using Xunit;

namespace LocalFileAgent.Text.Tests;

public class EncodingDetectorTests
{
    private readonly EncodingDetector _detector = new();

    static EncodingDetectorTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    // AR-6: UTF-8 with BOM
    public void Decode_Utf8WithBom_DetectsCorrectly()
    {
        var text = "تقرير الإيرادات لشهر أكتوبر ٢٠٢٥";
        var bytes = Encoding.UTF8.GetPreamble();
        var contentBytes = Encoding.UTF8.GetBytes(text);
        var allBytes = new byte[bytes.Length + contentBytes.Length];
        Buffer.BlockCopy(bytes, 0, allBytes, 0, bytes.Length);
        Buffer.BlockCopy(contentBytes, 0, allBytes, bytes.Length, contentBytes.Length);

        var result = _detector.Decode(allBytes);

        result.EncodingName.Should().Be("utf-8");
        result.Text.Should().Be(text);
        result.Confidence.Should().Be(1.0f);
    }

    [Fact]
    // AR-6: UTF-8 without BOM
    public void Decode_Utf8WithoutBom_DetectsCorrectly()
    {
        var text = "فاتورة ضريبية - شركة ووردو للبرمجيات";
        var bytes = Encoding.UTF8.GetBytes(text);

        var result = _detector.Decode(bytes);

        result.EncodingName.Should().Be("utf-8");
        result.Text.Should().Be(text);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.9f);
    }

    [Fact]
    // AR-6: UTF-16 Little Endian with BOM
    public void Decode_Utf16LeWithBom_DetectsCorrectly()
    {
        var text = "بيانات الموظفين والرواتب";
        var bytes = Encoding.Unicode.GetPreamble();
        var contentBytes = Encoding.Unicode.GetBytes(text);
        var allBytes = new byte[bytes.Length + contentBytes.Length];
        Buffer.BlockCopy(bytes, 0, allBytes, 0, bytes.Length);
        Buffer.BlockCopy(contentBytes, 0, allBytes, bytes.Length, contentBytes.Length);

        var result = _detector.Decode(allBytes);

        result.EncodingName.Should().StartWith("utf-16");
        result.Text.Should().Be(text);
        result.Confidence.Should().Be(1.0f);
    }

    [Fact]
    // AR-6: Windows-1256 (CP1256 - legacy Arabic Windows encoding)
    public void Decode_Windows1256_DetectsAndDecodesWithoutMojibake()
    {
        var text = "عقد بيع نهائي لقطعة أرض بالقاهرة";
        var cp1256 = Encoding.GetEncoding(1256);
        var bytes = cp1256.GetBytes(text);

        var result = _detector.Decode(bytes);

        result.EncodingName.Should().Be("windows-1256");
        result.Text.Should().Be(text);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.85f);
    }

    [Fact]
    // AR-6: ISO-8859-6 (Arabic Latin/ECMA-114)
    public void Decode_Iso8859_6_DetectsAndDecodesCorrectly()
    {
        var text = "محضر اجتماع مجلس الادارة";
        var iso = Encoding.GetEncoding("iso-8859-6");
        var bytes = iso.GetBytes(text);

        var result = _detector.Decode(bytes);

        result.EncodingName.ToLowerInvariant().Should().Contain("8859-6");
        result.Text.Should().Be(text);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.8f);
    }

    [Fact]
    // AR-6: Excel CSV in CP1256
    public void Decode_ExcelCsvExport_CP1256_DecodesCleanly()
    {
        var csvContent = "الاسم,الوظيفة,الراتب\nأحمد علي,مهندس برمجيات,25000\nمحمود حسن,مدير مشروعات,35000";
        var cp1256 = Encoding.GetEncoding(1256);
        var bytes = cp1256.GetBytes(csvContent);

        var result = _detector.Decode(bytes);

        result.EncodingName.Should().Be("windows-1256");
        result.Text.Should().Contain("أحمد علي");
        result.Text.Should().Contain("مهندس برمجيات");
    }

    [Fact]
    public void Decode_EmptySpan_ReturnsEmpty()
    {
        var result = _detector.Decode(ReadOnlySpan<byte>.Empty);

        result.Text.Should().BeEmpty();
        result.Confidence.Should().Be(1.0f);
    }
}

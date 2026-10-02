using System;
using System.Collections.Generic;
using FluentAssertions;
using LocalFileAgent.Application.Agent;
using LocalFileAgent.Domain.Search;
using Xunit;

namespace LocalFileAgent.Application.Tests;

public class GroundingValidatorTests
{
    [Fact]
    public void Validate_ValidCitations_ReturnsValidResult()
    {
        var validator = new GroundingValidator();

        var toolHits = new List<SearchResultItem>
        {
            new SearchResultItem(
                FilePath: "C:\\Users\\User\\Documents\\invoice_2025.pdf",
                FileName: "invoice_2025.pdf",
                PageNumber: 1,
                SourceKind: "text_layer",
                Score: 0.95f,
                Snippet: "فاتورة ضريبية بقيمة 500 ريال"
            ),
            new SearchResultItem(
                FilePath: "C:\\Users\\User\\Documents\\lease_contract.docx",
                FileName: "lease_contract.docx",
                PageNumber: 2,
                SourceKind: "text_layer",
                Score: 0.88f,
                Snippet: "عقد إيجار شقة سكنية"
            )
        };

        var generatedText = "بناءً على نتائج البحث، تم العثور على الفاتورة في الملف C:\\Users\\User\\Documents\\invoice_2025.pdf والمصدر [1]، وأيضاً عقد الإيجار lease_contract.docx.";

        var result = validator.Validate(generatedText, toolHits);

        result.IsValid.Should().BeTrue();
        result.HallucinatedPaths.Should().BeEmpty();
        result.VerifiedCitations.Should().HaveCount(2);
        result.SanitizedResponseText.Should().Be(generatedText);
    }

    [Fact]
    public void Validate_HallucinatedFilePath_IsDetectedAndSanitized()
    {
        var validator = new GroundingValidator();

        var toolHits = new List<SearchResultItem>
        {
            new SearchResultItem(
                FilePath: "C:\\Users\\User\\Documents\\invoice_2025.pdf",
                FileName: "invoice_2025.pdf",
                PageNumber: 1,
                SourceKind: "text_layer",
                Score: 0.95f,
                Snippet: "فاتورة ضريبية"
            )
        };

        // Notice the hallucinated secret path "C:\Windows\System32\passwords.txt"
        var generatedText = "وجدت الفاتورة في C:\\Users\\User\\Documents\\invoice_2025.pdf وأيضاً بيانات سرية في C:\\Windows\\System32\\passwords.txt.";

        var result = validator.Validate(generatedText, toolHits);

        result.IsValid.Should().BeFalse();
        result.HallucinatedPaths.Should().Contain("C:\\Windows\\System32\\passwords.txt");
        result.SanitizedResponseText.Should().NotContain("passwords.txt");
        result.SanitizedResponseText.Should().Contain("[مسار غير موثق محذوف]");
        result.VerifiedCitations.Should().HaveCount(1);
    }

    [Fact]
    public void Validate_OutOfRangeCitationIndex_IsFlagged()
    {
        var validator = new GroundingValidator();

        var toolHits = new List<SearchResultItem>
        {
            new SearchResultItem(
                FilePath: "C:\\Users\\User\\Documents\\file1.pdf",
                FileName: "file1.pdf",
                PageNumber: 1,
                SourceKind: "text_layer",
                Score: 0.9f,
                Snippet: "مستند تجريبي"
            )
        };

        // LLM cites [10] when only 1 hit exists
        var generatedText = "بحسب المصدر [10] فإن التاريخ هو 2026.";

        var result = validator.Validate(generatedText, toolHits);

        result.IsValid.Should().BeFalse();
        result.HallucinatedPaths.Should().Contain(p => p.Contains("10"));
    }
}

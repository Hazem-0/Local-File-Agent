using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Application.Agent;
using LocalFileAgent.Domain.Agent;
using LocalFileAgent.Domain.Search;
using Xunit;

namespace LocalFileAgent.Application.Tests;

public class AgentServiceTests
{
    private sealed class FakeSearchService : ISearchService
    {
        public List<SearchResultItem> PresetHits { get; set; } = new();

        public Task<IReadOnlyList<SearchResultItem>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SearchResultItem>>(PresetHits);
        }
    }

    [Fact]
    public async Task AskAsync_WithHits_SynthesizesGroundedAnswerWithCitations()
    {
        var searchService = new FakeSearchService
        {
            PresetHits = new List<SearchResultItem>
            {
                new SearchResultItem(
                    FilePath: "C:\\Users\\User\\Documents\\invoice_2025.pdf",
                    FileName: "invoice_2025.pdf",
                    PageNumber: 1,
                    SourceKind: "text_layer",
                    Score: 0.98f,
                    Snippet: "فاتورة ضريبية رقم 1024 لشركة الأفق"
                ),
                new SearchResultItem(
                    FilePath: "C:\\Users\\User\\Documents\\receipt.png",
                    FileName: "receipt.png",
                    PageNumber: 1,
                    SourceKind: "ocr_win",
                    Score: 0.85f,
                    Snippet: "إيصال سداد نقدي"
                )
            }
        };

        var planner = new AgentPlanner(ollamaClient: null);
        var validator = new GroundingValidator();
        var agent = new AgentService(planner, searchService, validator, ollamaClient: null);

        var answer = await agent.AskAsync("فين فواتير شركة الأفق؟");

        answer.Should().NotBeNull();
        answer.IsGrounded.Should().BeTrue();
        answer.Hits.Should().HaveCount(2);
        answer.VerifiedCitations.Should().NotBeEmpty();
        answer.ResponseText.Should().Contain("invoice_2025.pdf");
        answer.ResponseText.Should().Contain("الأفق");
        answer.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task AskAsync_WithNoHits_ReturnsPoliteArabicMessage()
    {
        var searchService = new FakeSearchService
        {
            PresetHits = new List<SearchResultItem>()
        };

        var planner = new AgentPlanner(ollamaClient: null);
        var agent = new AgentService(planner, searchService, ollamaClient: null);

        var answer = await agent.AskAsync("مستندات غير موجودة إطلاقاً");

        answer.Should().NotBeNull();
        answer.IsGrounded.Should().BeTrue();
        answer.Hits.Should().BeEmpty();
        answer.ResponseText.Should().Contain("لم يتم العثور على أي ملفات مطابقة");
    }
}

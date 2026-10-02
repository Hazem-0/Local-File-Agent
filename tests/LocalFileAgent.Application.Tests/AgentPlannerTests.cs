using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Application.Agent;
using LocalFileAgent.Domain.Models;
using Xunit;

namespace LocalFileAgent.Application.Tests;

public class AgentPlannerTests
{
    private sealed class FakeOllamaClient : IOllamaClient
    {
        public Func<ChatRequest, ChatResponse>? OnChat { get; set; }

        public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            if (OnChat != null)
            {
                return Task.FromResult(OnChat(request));
            }

            return Task.FromResult(new ChatResponse("test", "{}", 10, 10, 100));
        }

        public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult("0.5.0");
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModelInfo>>(Array.Empty<ModelInfo>());
        public Task<GenerateResponse> GenerateAsync(GenerateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new GenerateResponse("test", "test", 0, 0, 0.0));
        public Task<EmbeddingResponse> EmbedAsync(EmbeddingRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new EmbeddingResponse("test", Array.Empty<float[]>(), 0, 0.0));
    }

    [Fact]
    public async Task PlanAsync_DeterministicFallback_ExtractsKeywordsAndExtensions()
    {
        var planner = new AgentPlanner(ollamaClient: null);

        // Egyptian query: "عايز فواتير شركة الكهرباء لعام 2025 بصيغة بي دي اف"
        var query = "عايز فواتير شركة الكهرباء لعام 2025 بصيغة بي دي اف";
        var plan = await planner.PlanAsync(query);

        plan.RawQuery.Should().Be(query);
        plan.NormalizedQuery.Should().NotBeNullOrWhiteSpace();

        // Egyptian carrier word "عايز" should be stripped from key terms
        plan.Keywords.Should().NotContain("عايز");
        plan.Keywords.Should().Contain("فواتير");
        plan.Keywords.Should().Contain("الكهرباء");

        // Stems should be added to variants
        plan.DialectVariants.Should().Contain("فاتورة");

        // Requested extension "بي دي اف" maps to .pdf
        plan.FileExtensions.Should().NotBeNull();
        plan.FileExtensions.Should().Contain(".pdf");
    }

    [Fact]
    public async Task PlanAsync_DeterministicFallback_MapsWordAndExcelExtensions()
    {
        var planner = new AgentPlanner(ollamaClient: null);

        var planDocx = await planner.PlanAsync("ابحث عن عقود العمل ملفات وورد");
        planDocx.FileExtensions.Should().Contain(".docx");

        var planXlsx = await planner.PlanAsync("تقرير المبيعات الشهرية اكسل");
        planXlsx.FileExtensions.Should().Contain(".xlsx");

        var planImg = await planner.PlanAsync("صورة البطاقة الشخصية");
        planImg.FileExtensions.Should().Contain(".png");
    }

    [Fact]
    public async Task PlanAsync_WithLlm_ParsesStructuredJsonOutput()
    {
        var fakeClient = new FakeOllamaClient
        {
            OnChat = req =>
            {
                var json = @"{
                  ""keywords"": [""عقد"", ""إيجار""],
                  ""variants"": [""اتفاقية إيجار"", ""عقود""],
                  ""extensions"": ["".docx""],
                  ""intent"": ""search_files""
                }";
                return new ChatResponse("gemma4:e2b", json, 20, 20, 150);
            }
        };

        var planner = new AgentPlanner(ollamaClient: fakeClient);
        var plan = await planner.PlanAsync("ابحث عن عقود الإيجار");

        plan.Keywords.Should().Contain("عقد");
        plan.DialectVariants.Should().Contain("اتفاقية إيجار");
        plan.FileExtensions.Should().Contain(".docx");
        plan.Intent.Should().Be("search_files");
    }

    [Fact]
    public async Task PlanAsync_WithMalformedLlmJson_GracefullyFallsBackToDeterministicPlan()
    {
        var fakeClient = new FakeOllamaClient
        {
            OnChat = req => new ChatResponse("gemma4:e2b", "عفواً، لا أستطيع الإجابة بصيغة JSON.", 10, 10, 50)
        };

        var planner = new AgentPlanner(ollamaClient: fakeClient);
        var plan = await planner.PlanAsync("فواتير المبيعات");

        plan.Keywords.Should().Contain("فواتير");
        plan.DialectVariants.Should().Contain("فاتورة");
    }
}

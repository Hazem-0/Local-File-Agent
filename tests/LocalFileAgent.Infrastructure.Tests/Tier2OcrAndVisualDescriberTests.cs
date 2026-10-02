using System;
using System.IO;
using System.Numerics.Tensors;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Infrastructure.Ollama;
using LocalFileAgent.Infrastructure.Vision;
using LocalFileAgent.Text;
using Xunit;

namespace LocalFileAgent.Infrastructure.Tests;

public class Tier2OcrAndVisualDescriberTests
{
    private sealed class FakeOllamaClient : IOllamaClient
    {
        public Func<GenerateRequest, GenerateResponse>? OnGenerate { get; set; }

        public Task<GenerateResponse> GenerateAsync(GenerateRequest request, CancellationToken cancellationToken = default)
        {
            if (OnGenerate != null)
            {
                return Task.FromResult(OnGenerate(request));
            }

            return Task.FromResult(new GenerateResponse("نموذج استجابة تجريبية.", "done", 10, 10, 100));
        }

        public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult("0.5.0");
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModelInfo>>(Array.Empty<ModelInfo>());
        public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new ChatResponse("test", "test", 0, 0, 0.0));
        public Task<EmbeddingResponse> EmbedAsync(EmbeddingRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new EmbeddingResponse("test", Array.Empty<float[]>(), 0, 0.0));
    }

    [Fact]
    public async Task Tier2OcrService_CleansRepetitionLoops_AndReturnsStructuredResult()
    {
        var fakeOllama = new FakeOllamaClient
        {
            OnGenerate = req =>
            {
                // Simulate an infinite looping suffix
                var textWithLoop = "فاتورة ضريبية رقم 1024 المبلغ 5000 ريال " +
                                   "فاتورة فاتورة فاتورة فاتورة فاتورة فاتورة فاتورة فاتورة فاتورة فاتورة";
                return new GenerateResponse("gemma4:e2b", textWithLoop, 20, 20, 200);
            }
        };

        var service = new Tier2OcrService(fakeOllama, new TextQualityGate(), new ArabicOrderFixer());
        var dummyBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 });

        var result = await service.RecognizeBase64Async(dummyBase64);

        result.Should().NotBeNull();
        result!.Text.Should().Contain("فاتورة ضريبية رقم 1024");
        result.Text.Should().NotContain("فاتورة فاتورة فاتورة فاتورة");
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.70f);
        result.Engine.Should().Be("ocr_vlm");
    }

    [Fact]
    public async Task VisualDescriber_ExtractsConciseCaption_AndFullSummary()
    {
        var fakeOllama = new FakeOllamaClient
        {
            OnGenerate = req =>
            {
                var responseText = "صورة ملتقطة لعقد إيجار شقة سكنية في مدينة الرياض. العقد يتضمن بيانات المؤجر والمستأجر وتفاصيل الدفعات السنوية.";
                return new GenerateResponse("gemma4:e2b", responseText, 30, 30, 300);
            }
        };

        var describer = new VisualDescriber(fakeOllama);
        var dummyBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 });

        var result = await describer.DescribeImageBase64Async(dummyBase64);

        result.Should().NotBeNull();
        result!.Caption.Should().Be("صورة ملتقطة لعقد إيجار شقة سكنية في مدينة الرياض");
        result.Summary.Should().Contain("العقد يتضمن بيانات المؤجر");
        result.Confidence.Should().Be(0.85f);
    }

    [Fact]
    public async Task VisualEmbeddingService_Produces1152DimNormalizedVectors()
    {
        var visualEmbedder = new VisualEmbeddingService();

        var textVector = await visualEmbedder.GenerateTextEmbeddingAsync("فاتورة شراء مستلزمات مكتبية");
        textVector.Length.Should().Be(1152);

        // Assert unit L2 normalization: norm should equal 1.0
        var norm = TensorPrimitives.Norm(textVector.AsSpan());
        norm.Should().BeApproximately(1.0f, 0.001f);

        // Identical text produces identical deterministic projection
        var textVector2 = await visualEmbedder.GenerateTextEmbeddingAsync("فاتورة شراء مستلزمات مكتبية");
        TensorPrimitives.CosineSimilarity(textVector.AsSpan(), textVector2.AsSpan()).Should().BeApproximately(1.0f, 0.0001f);
    }
}

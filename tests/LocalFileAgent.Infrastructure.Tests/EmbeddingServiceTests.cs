using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Infrastructure.Ollama;
using Xunit;

namespace LocalFileAgent.Infrastructure.Tests;

public class EmbeddingServiceTests
{
    private sealed class MockOllamaClient : IOllamaClient
    {
        public List<EmbeddingRequest> ReceivedRequests { get; } = new();

        public Task<EmbeddingResponse> EmbedAsync(EmbeddingRequest request, CancellationToken cancellationToken = default)
        {
            ReceivedRequests.Add(request);

            var vectors = request.Input.Select(txt => new float[] { txt.Length, 1.0f }).ToList();
            return Task.FromResult(new EmbeddingResponse(request.Model, vectors, 10, 5.0));
        }

        public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult("0.35.0");
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModelInfo>>(Array.Empty<ModelInfo>());
        public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<GenerateResponse> GenerateAsync(GenerateRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_EmptyText_ReturnsEmptyWithoutCallingOllama()
    {
        var mock = new MockOllamaClient();
        var service = new EmbeddingService(mock);

        var result = await service.GenerateEmbeddingAsync("   ");

        result.Should().BeEmpty();
        mock.ReceivedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_CallsOllamaAndCachesResult()
    {
        var mock = new MockOllamaClient();
        var service = new EmbeddingService(mock);

        var v1 = await service.GenerateEmbeddingAsync("فاتورة ضريبية");
        var v2 = await service.GenerateEmbeddingAsync("فاتورة ضريبية");

        v1.Should().NotBeEmpty();
        v2.Should().Equal(v1);

        // Ollama called only once due to caching
        mock.ReceivedRequests.Should().HaveCount(1);
        mock.ReceivedRequests[0].Input.Should().ContainSingle().Which.Should().Be("فاتورة ضريبية");
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_BatchesRequestsCorrectly()
    {
        var mock = new MockOllamaClient();
        var options = new EmbeddingOptions
        {
            BatchSize = 10
        };
        var service = new EmbeddingService(mock, options);

        // 25 distinct texts
        var texts = Enumerable.Range(1, 25).Select(i => $"نص تجريبي {i}").ToList();

        var embeddings = await service.GenerateEmbeddingsAsync(texts);

        embeddings.Should().HaveCount(25);
        mock.ReceivedRequests.Should().HaveCount(3);
        mock.ReceivedRequests[0].Input.Should().HaveCount(10);
        mock.ReceivedRequests[1].Input.Should().HaveCount(10);
        mock.ReceivedRequests[2].Input.Should().HaveCount(5);

        // Calling again with same texts uses cache (zero new calls)
        var cachedEmbeddings = await service.GenerateEmbeddingsAsync(texts);
        cachedEmbeddings.Should().HaveCount(25);
        mock.ReceivedRequests.Should().HaveCount(3);
    }
}

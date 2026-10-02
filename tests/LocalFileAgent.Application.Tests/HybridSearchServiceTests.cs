using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Application.Search;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Domain.Storage;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Text;
using Xunit;

namespace LocalFileAgent.Application.Tests;

public class HybridSearchServiceTests
{
    private sealed class FakeIndexStore : IIndexStore
    {
        public List<SearchResultItem> FtsResults { get; set; } = new();
        public Dictionary<long, SearchResultItem> ChunksById { get; set; } = new();

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<long> UpsertFileAsync(IndexedFile file, CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public Task<IndexedFile?> GetFileByPathAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<IndexedFile?>(null);
        public Task<IReadOnlyList<long>> InsertChunksAsync(long fileId, IReadOnlyList<IndexedChunk> chunks, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<long>>(Array.Empty<long>());
        public Task DeleteFileAsync(long fileId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<SearchResultItem>> SearchFtsAsync(
            string normalizedQuery,
            IReadOnlyList<string>? scopePaths = null,
            int limit = 20,
            bool useTrigram = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SearchResultItem>>(FtsResults);
        }

        public Task<IReadOnlyList<SearchResultItem>> GetChunksByIdsAsync(
            IReadOnlyList<long> chunkIds,
            IReadOnlyList<string>? scopePaths = null,
            CancellationToken cancellationToken = default)
        {
            var list = new List<SearchResultItem>();
            foreach (var id in chunkIds)
            {
                if (ChunksById.TryGetValue(id, out var item))
                {
                    list.Add(item);
                }
            }
            return Task.FromResult<IReadOnlyList<SearchResultItem>>(list);
        }

        public Task<long> GetIndexedFileCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public Task<long> GetChunkCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeVectorIndex : IVectorIndex
    {
        public List<(long Id, float Score)> VectorHits { get; set; } = new();

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddAsync(IReadOnlyList<(long Id, float[] Vector)> items, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<(long Id, float Score)>> SearchAsync(
            float[] query,
            int k,
            Func<long, bool>? filter = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<(long Id, float Score)>>(VectorHits);
        }

        public Task<IReadOnlyList<(long Id, float Score)>> SearchExactAsync(
            float[] query,
            int k,
            IReadOnlySet<long> candidateIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<(long Id, float Score)>>(VectorHits);
        }

        public Task DeleteAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> GetCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(VectorHits.Count);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public float[] VectorToReturn { get; set; } = new float[] { 1.0f, 0.0f };

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(VectorToReturn);
        }

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            var list = new List<float[]>();
            for (var i = 0; i < texts.Count; i++) list.Add(VectorToReturn);
            return Task.FromResult<IReadOnlyList<float[]>>(list);
        }
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyResults()
    {
        var store = new FakeIndexStore();
        var normalizer = new ArabicTextNormalizer();
        var service = new HybridSearchService(store, normalizer);

        var results = await service.SearchAsync(new SearchRequest("   "));
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_LexicalOnlyMatch_ReturnsFtsMatchKind()
    {
        var store = new FakeIndexStore();
        store.FtsResults.Add(new SearchResultItem(
            FilePath: "D:\\docs\\invoice.pdf",
            FileName: "invoice.pdf",
            PageNumber: 1,
            SourceKind: "text_layer",
            Score: 0.9f,
            Snippet: "فاتورة ضريبية",
            ChunkId: 101L,
            MatchKind: "fts"
        ));

        var normalizer = new ArabicTextNormalizer();
        var service = new HybridSearchService(store, normalizer);

        var results = await service.SearchAsync(new SearchRequest("فاتورة"));

        results.Should().HaveCount(1);
        results[0].MatchKind.Should().Be("fts");
        results[0].ChunkId.Should().Be(101L);
        // Lexical rank 1 with w=0.5, k=60: 0.5 / (60 + 1) = 0.5 / 61 ≈ 0.008196
        results[0].Score.Should().BeApproximately(0.5f / 61f, 0.0001f);
    }

    [Fact]
    public async Task SearchAsync_SemanticOnlyMatch_ReturnsVectorMatchKind()
    {
        var store = new FakeIndexStore();
        // Fts has 0 results (e.g. cross-dialect or synonym query)
        store.ChunksById[202L] = new SearchResultItem(
            FilePath: "D:\\docs\\receipt.docx",
            FileName: "receipt.docx",
            PageNumber: 1,
            SourceKind: "text_layer",
            Score: 0.85f,
            Snippet: "وصل استلام بضائع",
            ChunkId: 202L,
            MatchKind: "vector"
        );

        var vectorIndex = new FakeVectorIndex();
        vectorIndex.VectorHits.Add((202L, 0.85f));

        var embeddingService = new FakeEmbeddingService();
        var normalizer = new ArabicTextNormalizer();

        var service = new HybridSearchService(store, normalizer, vectorIndex, embeddingService);

        var results = await service.SearchAsync(new SearchRequest("إيصال"));

        results.Should().HaveCount(1);
        results[0].MatchKind.Should().Be("vector");
        results[0].ChunkId.Should().Be(202L);
        results[0].Score.Should().BeApproximately(0.5f / 61f, 0.0001f);
    }

    [Fact]
    public async Task SearchAsync_HybridMatch_CombinesRrfScores()
    {
        var store = new FakeIndexStore();
        // Item 303 is returned by FTS
        store.FtsResults.Add(new SearchResultItem(
            FilePath: "D:\\docs\\contract.pdf",
            FileName: "contract.pdf",
            PageNumber: 1,
            SourceKind: "text_layer",
            Score: 0.9f,
            Snippet: "عقد توريد أجهزة حاسوب",
            ChunkId: 303L,
            MatchKind: "fts"
        ));

        // Item 303 is also returned by Vector index
        store.ChunksById[303L] = new SearchResultItem(
            FilePath: "D:\\docs\\contract.pdf",
            FileName: "contract.pdf",
            PageNumber: 1,
            SourceKind: "text_layer",
            Score: 0.88f,
            Snippet: "عقد توريد أجهزة حاسوب",
            ChunkId: 303L,
            MatchKind: "vector"
        );

        var vectorIndex = new FakeVectorIndex();
        vectorIndex.VectorHits.Add((303L, 0.88f));

        var embeddingService = new FakeEmbeddingService();
        var normalizer = new ArabicTextNormalizer();

        var service = new HybridSearchService(store, normalizer, vectorIndex, embeddingService);

        var results = await service.SearchAsync(new SearchRequest("عقد حاسوب"));

        results.Should().HaveCount(1);
        results[0].MatchKind.Should().Be("hybrid");
        results[0].ChunkId.Should().Be(303L);

        // Fused score is sum of lexical and semantic reciprocal ranks:
        // (0.5 / 61) + (0.5 / 61) = 1.0 / 61 ≈ 0.016393
        var expectedScore = (0.5f / 61f) + (0.5f / 61f);
        results[0].Score.Should().BeApproximately(expectedScore, 0.0001f);
    }
}

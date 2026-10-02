using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LocalFileAgent.Domain.Search;

public sealed record SearchRequest(
    string Query,
    IReadOnlyList<string>? ScopePaths = null,
    int Limit = 20
);

public sealed record SearchResultItem(
    string FilePath,
    string FileName,
    int PageNumber,
    string SourceKind,
    float Score,
    string Snippet,
    long ChunkId = 0,
    string MatchKind = "fts"
);

public sealed record HybridSearchOptions(
    float LexicalWeight = 0.5f,
    float SemanticWeight = 0.5f,
    float VisualWeight = 0.3f,
    int RrfK = 60,
    int LexicalCandidateLimit = 50,
    int SemanticCandidateLimit = 50,
    int VisualCandidateLimit = 20,
    float MinSemanticSimilarity = 0.55f,
    float MinVisualSimilarity = 0.28f,
    int MinSemanticQueryLength = 3
);

public interface ISearchService
{
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}

public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}

public interface IVectorIndex : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task AddAsync(IReadOnlyList<(long Id, float[] Vector)> items, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(long Id, float Score)>> SearchAsync(float[] query, int k, Func<long, bool>? filter = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(long Id, float Score)>> SearchExactAsync(float[] query, int k, IReadOnlySet<long> candidateIds, CancellationToken cancellationToken = default);
    Task DeleteAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default);
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);
    Task ClearAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public interface IVisualEmbeddingService
{
    Task<float[]> GenerateImageEmbeddingAsync(string imagePath, CancellationToken cancellationToken = default);
    Task<float[]> GenerateTextEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}

public interface IVisualVectorIndex : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task AddAsync(IReadOnlyList<(long FileId, float[] Vector)> items, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(long FileId, float Score)>> SearchAsync(float[] query, int k, Func<long, bool>? filter = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(IReadOnlyList<long> fileIds, CancellationToken cancellationToken = default);
    Task<int> GetCountAsync(CancellationToken cancellationToken = default);
    Task ClearAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}


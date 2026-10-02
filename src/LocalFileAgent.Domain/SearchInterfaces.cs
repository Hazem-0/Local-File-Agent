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
    int RrfK = 60,
    int LexicalCandidateLimit = 50,
    int SemanticCandidateLimit = 50
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
}

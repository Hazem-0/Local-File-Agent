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
    string Snippet
);

public interface ISearchService
{
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}

public interface IVectorIndex
{
    Task AddAsync(IReadOnlyList<(long Id, float[] Vector)> items, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(long Id, float Score)>> SearchAsync(float[] query, int k, Func<long, bool>? filter = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(long Id, float Score)>> SearchExactAsync(float[] query, int k, IReadOnlySet<long> candidateIds, CancellationToken cancellationToken = default);
}

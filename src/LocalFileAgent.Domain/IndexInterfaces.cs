using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.Search;

namespace LocalFileAgent.Domain.Storage;

public sealed record IndexedFile(
    long Id,
    string Path,
    string Name,
    string Extension,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    DateTimeOffset IndexedAt,
    string ETag,
    string Status,
    string? ErrorMessage = null
);

public sealed record IndexedChunk(
    long Id,
    long FileId,
    int PageNumber,
    int Ordinal,
    string TextRaw,
    string TextNormalized,
    string SourceKind,
    float Confidence = 1.0f
);

public interface IIndexStore : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<long> UpsertFileAsync(IndexedFile file, CancellationToken cancellationToken = default);
    Task<IndexedFile?> GetFileByPathAsync(string path, CancellationToken cancellationToken = default);
    Task<IndexedFile?> GetFileByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<long>> InsertChunksAsync(long fileId, IReadOnlyList<IndexedChunk> chunks, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(long fileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResultItem>> GetChunksByIdsAsync(
        IReadOnlyList<long> chunkIds,
        IReadOnlyList<string>? scopePaths = null,
        CancellationToken cancellationToken = default
    );
    Task<IReadOnlyList<SearchResultItem>> SearchFtsAsync(
        string normalizedQuery,
        IReadOnlyList<string>? scopePaths = null,
        int limit = 20,
        bool useTrigram = false,
        CancellationToken cancellationToken = default
    );
    Task<long> GetIndexedFileCountAsync(CancellationToken cancellationToken = default);
    Task<long> GetChunkCountAsync(CancellationToken cancellationToken = default);
    Task ClearAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

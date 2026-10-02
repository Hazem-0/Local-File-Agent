using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.FileSystem;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Domain.Storage;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Domain.Throttling;
using LocalFileAgent.Domain.Worker;

namespace LocalFileAgent.Application.Indexing;

public sealed record IndexingProgressReport(
    int TotalDiscovered,
    int IndexedCount,
    int SkippedCount,
    int FailedCount,
    string CurrentFilePath
);

public sealed record IndexProgressResult(
    int IndexedCount,
    int SkippedCount,
    int FailedCount,
    TimeSpan Elapsed
);

public interface IIndexOrchestrator
{
    Task<IndexProgressResult> IndexDirectoryAsync(
        string directoryPath,
        ScanOptions? options = null,
        IProgress<IndexingProgressReport>? progress = null,
        CancellationToken cancellationToken = default
    );
}

public sealed class IndexOrchestrator : IIndexOrchestrator
{
    private readonly IFileScanner _scanner;
    private readonly IWorkerClient _worker;
    private readonly IIndexStore _indexStore;
    private readonly ITextNormalizer _normalizer;
    private readonly IChunker _chunker;
    private readonly IVectorIndex? _vectorIndex;
    private readonly IEmbeddingService? _embeddingService;
    private readonly IVisualVectorIndex? _visualVectorIndex;
    private readonly IVisualEmbeddingService? _visualEmbedding;
    private readonly ITier2OcrService? _tier2Ocr;
    private readonly IVisualDescriber? _visualDescriber;
    private readonly IResourceGovernor? _governor;

    public IndexOrchestrator(
        IFileScanner scanner,
        IWorkerClient worker,
        IIndexStore indexStore,
        ITextNormalizer normalizer,
        IChunker chunker,
        IVectorIndex? vectorIndex = null,
        IEmbeddingService? embeddingService = null,
        IVisualVectorIndex? visualVectorIndex = null,
        IVisualEmbeddingService? visualEmbedding = null,
        ITier2OcrService? tier2Ocr = null,
        IVisualDescriber? visualDescriber = null,
        IResourceGovernor? governor = null)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _indexStore = indexStore ?? throw new ArgumentNullException(nameof(indexStore));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _vectorIndex = vectorIndex;
        _embeddingService = embeddingService;
        _visualVectorIndex = visualVectorIndex;
        _visualEmbedding = visualEmbedding;
        _tier2Ocr = tier2Ocr;
        _visualDescriber = visualDescriber;
        _governor = governor;
    }

    public async Task<IndexProgressResult> IndexDirectoryAsync(
        string directoryPath,
        ScanOptions? options = null,
        IProgress<IndexingProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        var start = DateTime.UtcNow;
        options ??= new ScanOptions();

        await _indexStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (_vectorIndex != null)
        {
            await _vectorIndex.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        if (_visualVectorIndex != null)
        {
            await _visualVectorIndex.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        await _worker.StartAsync(cancellationToken).ConfigureAwait(false);

        var totalDiscovered = 0;
        var indexedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;

        await foreach (var file in _scanner.ScanAsync(directoryPath, options, null, cancellationToken).ConfigureAwait(false))
        {
            totalDiscovered++;
            var etag = $"{file.SizeBytes}_{file.ModifiedAt.ToUnixTimeSeconds()}";

            // Incremental check: has file already been indexed with same etag?
            var existing = await _indexStore.GetFileByPathAsync(file.Path, cancellationToken).ConfigureAwait(false);
            if (existing != null && existing.ETag == etag && existing.Status == "indexed")
            {
                skippedCount++;
                progress?.Report(new IndexingProgressReport(totalDiscovered, indexedCount, skippedCount, failedCount, file.Path));
                continue;
            }

            try
            {
                var parseReq = new WorkerParseRequest(
                    RequestId: Guid.NewGuid().ToString("N"),
                    FilePath: file.Path,
                    Extension: file.Extension
                );

                var parseResp = await _worker.ParseFileAsync(parseReq, cancellationToken).ConfigureAwait(false);
                if (!parseResp.Success)
                {
                    failedCount++;
                    var failedFile = new IndexedFile(
                        Id: 0,
                        Path: file.Path,
                        Name: file.Name,
                        Extension: file.Extension,
                        SizeBytes: file.SizeBytes,
                        CreatedAt: file.CreatedAt,
                        ModifiedAt: file.ModifiedAt,
                        IndexedAt: DateTimeOffset.UtcNow,
                        ETag: etag,
                        Status: "failed",
                        ErrorMessage: parseResp.ErrorMessage
                    );
                    await _indexStore.UpsertFileAsync(failedFile, cancellationToken).ConfigureAwait(false);
                    progress?.Report(new IndexingProgressReport(totalDiscovered, indexedCount, skippedCount, failedCount, file.Path));
                    continue;
                }

                var indexedFile = new IndexedFile(
                    Id: 0,
                    Path: file.Path,
                    Name: file.Name,
                    Extension: file.Extension,
                    SizeBytes: file.SizeBytes,
                    CreatedAt: file.CreatedAt,
                    ModifiedAt: file.ModifiedAt,
                    IndexedAt: DateTimeOffset.UtcNow,
                    ETag: etag,
                    Status: "indexed"
                );

                var fileId = await _indexStore.UpsertFileAsync(indexedFile, cancellationToken).ConfigureAwait(false);

                var chunksList = new List<IndexedChunk>();
                var chunkingOptions = new ChunkingOptions();

                foreach (var page in parseResp.Pages)
                {
                    if (string.IsNullOrWhiteSpace(page.Text)) continue;

                    var rawChunks = _chunker.Chunk(page.Text, chunkingOptions, page.PageNumber, page.SourceKind, page.Confidence);
                    foreach (var rc in rawChunks)
                    {
                        var norm = _normalizer.Normalize(rc.Text, NormalizationProfile.Search);
                        chunksList.Add(new IndexedChunk(
                            Id: 0,
                            FileId: fileId,
                            PageNumber: rc.PageNumber,
                            Ordinal: rc.Ordinal,
                            TextRaw: rc.Text,
                            TextNormalized: norm.ProcessedText,
                            SourceKind: rc.SourceKind,
                            Confidence: rc.Confidence
                        ));
                    }
                }

                if (IsImageFile(file.Extension))
                {
                    // Fast Lane: Dual-space Visual Embedding (1152-dim)
                    if (_visualVectorIndex != null && _visualEmbedding != null)
                    {
                        try
                        {
                            var visualVec = await _visualEmbedding.GenerateImageEmbeddingAsync(file.Path, cancellationToken).ConfigureAwait(false);
                            if (visualVec.Length > 0)
                            {
                                await _visualVectorIndex.AddAsync(new[] { (fileId, visualVec) }, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                            // Non-fatal
                        }
                    }

                    // Resource Governor check: throttle heavy model operations on battery/low charge
                    var isHeavyThrottled = _governor?.ShouldThrottleHeavyWork() == true;

                    // Tier 2 OCR Escalation: Escalate if Tier 1 returned empty or low-confidence (<0.60) text
                    var hasAdequateOcr = parseResp.Pages.Any(p => !string.IsNullOrWhiteSpace(p.Text) && p.Confidence >= 0.60f);
                    if (!isHeavyThrottled && !hasAdequateOcr && _tier2Ocr != null)
                    {
                        try
                        {
                            var ocr2 = await _tier2Ocr.RecognizeAsync(file.Path, cancellationToken).ConfigureAwait(false);
                            if (ocr2 != null && !string.IsNullOrWhiteSpace(ocr2.Text))
                            {
                                var ocrChunks = _chunker.Chunk(ocr2.Text, chunkingOptions, 1, ocr2.Engine, ocr2.Confidence);
                                foreach (var rc in ocrChunks)
                                {
                                    var norm = _normalizer.Normalize(rc.Text, NormalizationProfile.Search);
                                    chunksList.Add(new IndexedChunk(
                                        Id: 0,
                                        FileId: fileId,
                                        PageNumber: rc.PageNumber,
                                        Ordinal: chunksList.Count + rc.Ordinal,
                                        TextRaw: rc.Text,
                                        TextNormalized: norm.ProcessedText,
                                        SourceKind: rc.SourceKind,
                                        Confidence: rc.Confidence
                                    ));
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                            // Non-fatal
                        }
                    }

                    // Slow Lane: Visual Describer (Arabic VLM document/scene captioning)
                    if (!isHeavyThrottled && _visualDescriber != null)
                    {
                        try
                        {
                            var vlmDesc = await _visualDescriber.DescribeImageAsync(file.Path, cancellationToken).ConfigureAwait(false);
                            if (vlmDesc != null && !string.IsNullOrWhiteSpace(vlmDesc.Summary))
                            {
                                var descChunks = _chunker.Chunk(vlmDesc.Summary, chunkingOptions, 1, "vlm", vlmDesc.Confidence);
                                foreach (var rc in descChunks)
                                {
                                    var norm = _normalizer.Normalize(rc.Text, NormalizationProfile.Search);
                                    chunksList.Add(new IndexedChunk(
                                        Id: 0,
                                        FileId: fileId,
                                        PageNumber: rc.PageNumber,
                                        Ordinal: chunksList.Count + rc.Ordinal,
                                        TextRaw: rc.Text,
                                        TextNormalized: norm.ProcessedText,
                                        SourceKind: "vlm",
                                        Confidence: rc.Confidence
                                    ));
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                            // Non-fatal
                        }
                    }
                }

                var chunkIds = await _indexStore.InsertChunksAsync(fileId, chunksList, cancellationToken).ConfigureAwait(false);

                var throttleEmbedding = _governor?.ShouldThrottleHeavyWork() == true;
                if (!throttleEmbedding && _vectorIndex != null && _embeddingService != null && chunkIds.Count > 0)
                {
                    try
                    {
                        var textsToEmbed = new List<string>(chunksList.Count);
                        for (var i = 0; i < chunksList.Count; i++)
                        {
                            textsToEmbed.Add(chunksList[i].TextNormalized);
                        }

                        var vectors = await _embeddingService.GenerateEmbeddingsAsync(textsToEmbed, cancellationToken).ConfigureAwait(false);
                        var vectorItems = new List<(long Id, float[] Vector)>(chunksList.Count);
                        for (var i = 0; i < chunksList.Count && i < vectors.Count && i < chunkIds.Count; i++)
                        {
                            vectorItems.Add((chunkIds[i], vectors[i]));
                        }
                        await _vectorIndex.AddAsync(vectorItems, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Non-fatal: FTS5 indexing succeeded, vector generation failure does not block file indexing
                    }
                }

                indexedCount++;

                if (_governor != null)
                {
                    await _governor.WaitIfThrottledAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                failedCount++;
                var errFile = new IndexedFile(
                    Id: 0,
                    Path: file.Path,
                    Name: file.Name,
                    Extension: file.Extension,
                    SizeBytes: file.SizeBytes,
                    CreatedAt: file.CreatedAt,
                    ModifiedAt: file.ModifiedAt,
                    IndexedAt: DateTimeOffset.UtcNow,
                    ETag: etag,
                    Status: "failed",
                    ErrorMessage: ex.Message
                );
                await _indexStore.UpsertFileAsync(errFile, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new IndexingProgressReport(totalDiscovered, indexedCount, skippedCount, failedCount, file.Path));
        }

        return new IndexProgressResult(indexedCount, skippedCount, failedCount, DateTime.UtcNow - start);
    }

    private static bool IsImageFile(string extension) =>
        extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
}

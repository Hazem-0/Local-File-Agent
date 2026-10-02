using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Domain.Storage;
using LocalFileAgent.Domain.Text;

namespace LocalFileAgent.Application.Search;

public sealed class HybridSearchService : ISearchService
{
    private readonly IIndexStore _indexStore;
    private readonly IVectorIndex? _vectorIndex;
    private readonly IEmbeddingService? _embeddingService;
    private readonly IVisualVectorIndex? _visualVectorIndex;
    private readonly IVisualEmbeddingService? _visualEmbedding;
    private readonly ITextNormalizer _normalizer;
    private readonly HybridSearchOptions _options;

    public HybridSearchService(
        IIndexStore indexStore,
        ITextNormalizer normalizer,
        IVectorIndex? vectorIndex = null,
        IEmbeddingService? embeddingService = null,
        IVisualVectorIndex? visualVectorIndex = null,
        IVisualEmbeddingService? visualEmbedding = null,
        HybridSearchOptions? options = null)
    {
        _indexStore = indexStore ?? throw new ArgumentNullException(nameof(indexStore));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _vectorIndex = vectorIndex;
        _embeddingService = embeddingService;
        _visualVectorIndex = visualVectorIndex;
        _visualEmbedding = visualEmbedding;
        _options = options ?? new HybridSearchOptions();
    }

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Array.Empty<SearchResultItem>();
        }

        var normResult = _normalizer.Normalize(request.Query, NormalizationProfile.Search);
        var normalizedQuery = string.IsNullOrWhiteSpace(normResult.ProcessedText)
            ? request.Query.Trim()
            : normResult.ProcessedText;

        // Step 1: Lexical Search (FTS5)
        var lexicalCandidates = await _indexStore.SearchFtsAsync(
            normalizedQuery,
            request.ScopePaths,
            _options.LexicalCandidateLimit,
            useTrigram: false,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);

        // Fallback to trigram if 0 exact token matches
        if (lexicalCandidates.Count == 0)
        {
            lexicalCandidates = await _indexStore.SearchFtsAsync(
                normalizedQuery,
                request.ScopePaths,
                _options.LexicalCandidateLimit,
                useTrigram: true,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
        }

        // Step 2: Semantic Search (Dense Vector)
        IReadOnlyList<SearchResultItem> vectorCandidates = Array.Empty<SearchResultItem>();
        if (_vectorIndex != null && _embeddingService != null)
        {
            try
            {
                var queryVector = await _embeddingService.GenerateEmbeddingAsync(request.Query, cancellationToken).ConfigureAwait(false);
                if (queryVector.Length > 0)
                {
                    var vectorHits = await _vectorIndex.SearchAsync(
                        queryVector,
                        k: _options.SemanticCandidateLimit,
                        filter: null,
                        cancellationToken: cancellationToken
                    ).ConfigureAwait(false);

                    if (vectorHits.Count > 0)
                    {
                        var chunkIds = vectorHits.Select(h => h.Id).ToList();
                        var chunkDetails = await _indexStore.GetChunksByIdsAsync(
                            chunkIds,
                            request.ScopePaths,
                            cancellationToken
                        ).ConfigureAwait(false);

                        var hitScores = vectorHits.ToDictionary(h => h.Id, h => h.Score);

                        // Order chunk details by vector similarity score
                        var orderedDetails = chunkDetails
                            .Where(c => hitScores.ContainsKey(c.ChunkId))
                            .Select(c => c with { Score = hitScores[c.ChunkId], MatchKind = "vector" })
                            .OrderByDescending(c => c.Score)
                            .ToList();

                        vectorCandidates = orderedDetails;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Graceful degradation: vector failure leaves lexical results intact
            }
        }

        // Step 3: Visual Search (Dual-space SigLIP 2 query)
        IReadOnlyList<SearchResultItem> visualCandidates = Array.Empty<SearchResultItem>();
        if (_visualVectorIndex != null && _visualEmbedding != null)
        {
            try
            {
                var visualQuery = await _visualEmbedding.GenerateTextEmbeddingAsync(request.Query, cancellationToken).ConfigureAwait(false);
                if (visualQuery.Length > 0)
                {
                    var visualHits = await _visualVectorIndex.SearchAsync(
                        visualQuery,
                        k: _options.VisualCandidateLimit,
                        filter: null,
                        cancellationToken: cancellationToken
                    ).ConfigureAwait(false);

                    if (visualHits.Count > 0)
                    {
                        var visualList = new List<SearchResultItem>(visualHits.Count);
                        foreach (var hit in visualHits)
                        {
                            var file = await _indexStore.GetFileByIdAsync(hit.FileId, cancellationToken).ConfigureAwait(false);
                            if (file != null)
                            {
                                if (request.ScopePaths != null && request.ScopePaths.Count > 0 &&
                                    !request.ScopePaths.Any(sp => file.Path.StartsWith(sp, StringComparison.OrdinalIgnoreCase)))
                                {
                                    continue;
                                }

                                visualList.Add(new SearchResultItem(
                                    FilePath: file.Path,
                                    FileName: file.Name,
                                    PageNumber: 1,
                                    SourceKind: "visual_siglip",
                                    Score: hit.Score,
                                    Snippet: file.Name,
                                    ChunkId: hit.FileId,
                                    MatchKind: "visual"
                                ));
                            }
                        }
                        visualCandidates = visualList;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Graceful degradation: visual search failure does not disrupt lexical/semantic results
            }
        }

        // Step 4: Reciprocal Rank Fusion (RRF: Lexical + Semantic + Visual)
        var fusedResults = PerformReciprocalRankFusion(lexicalCandidates, vectorCandidates, visualCandidates, _options);

        if (fusedResults.Count > request.Limit)
        {
            return fusedResults.GetRange(0, request.Limit);
        }

        return fusedResults;
    }

    private static List<SearchResultItem> PerformReciprocalRankFusion(
        IReadOnlyList<SearchResultItem> lexical,
        IReadOnlyList<SearchResultItem> semantic,
        IReadOnlyList<SearchResultItem> visual,
        HybridSearchOptions options)
    {
        var fusedMap = new Dictionary<string, (SearchResultItem Item, bool InLex, bool InSem, bool InVis, double RrfScore)>(StringComparer.OrdinalIgnoreCase);

        // Lexical ranking pass
        for (var i = 0; i < lexical.Count; i++)
        {
            var item = lexical[i];
            var rank = i + 1;
            var key = GetCandidateKey(item);
            var term = options.LexicalWeight / (options.RrfK + rank);

            fusedMap[key] = (item, InLex: true, InSem: false, InVis: false, RrfScore: term);
        }

        // Semantic ranking pass
        for (var i = 0; i < semantic.Count; i++)
        {
            var item = semantic[i];
            var rank = i + 1;
            var key = GetCandidateKey(item);
            var term = options.SemanticWeight / (options.RrfK + rank);

            if (fusedMap.TryGetValue(key, out var existing))
            {
                fusedMap[key] = (existing.Item, InLex: true, InSem: true, InVis: existing.InVis, RrfScore: existing.RrfScore + term);
            }
            else
            {
                fusedMap[key] = (item, InLex: false, InSem: true, InVis: false, RrfScore: term);
            }
        }

        // Visual ranking pass
        for (var i = 0; i < visual.Count; i++)
        {
            var item = visual[i];
            var rank = i + 1;
            var key = GetCandidateKey(item);
            var term = options.VisualWeight / (options.RrfK + rank);

            if (fusedMap.TryGetValue(key, out var existing))
            {
                fusedMap[key] = (existing.Item, InLex: existing.InLex, InSem: existing.InSem, InVis: true, RrfScore: existing.RrfScore + term);
            }
            else
            {
                fusedMap[key] = (item, InLex: false, InSem: false, InVis: true, RrfScore: term);
            }
        }

        var results = new List<SearchResultItem>(fusedMap.Count);
        foreach (var entry in fusedMap.Values)
        {
            var matchKind = (entry.InLex, entry.InSem, entry.InVis) switch
            {
                (true, true, _) => "hybrid",
                (true, false, true) => "hybrid",
                (false, true, true) => "hybrid",
                (true, false, false) => "fts",
                (false, true, false) => "vector",
                (false, false, true) => "visual",
                _ => "fts"
            };

            results.Add(entry.Item with
            {
                Score = (float)entry.RrfScore,
                MatchKind = matchKind
            });
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        return results;
    }

    private static string GetCandidateKey(SearchResultItem item)
    {
        if (item.ChunkId > 0)
        {
            return $"chunk_{item.ChunkId}";
        }

        return $"{item.FilePath}:{item.PageNumber}:{item.Snippet.GetHashCode()}";
    }
}

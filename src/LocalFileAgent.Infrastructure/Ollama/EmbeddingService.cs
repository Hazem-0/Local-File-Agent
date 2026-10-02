using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Domain.Search;

namespace LocalFileAgent.Infrastructure.Ollama;

public sealed record EmbeddingOptions
{
    public string ModelName { get; init; } = "bge-m3";
    public int BatchSize { get; init; } = 16;
    public int MaxCacheEntries { get; init; } = 5000;
}

public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IOllamaClient _ollamaClient;
    private readonly EmbeddingOptions _options;
    private readonly ConcurrentDictionary<string, float[]> _cache = new(StringComparer.Ordinal);

    public EmbeddingService(IOllamaClient ollamaClient, EmbeddingOptions? options = null)
    {
        _ollamaClient = ollamaClient ?? throw new ArgumentNullException(nameof(ollamaClient));
        _options = options ?? new EmbeddingOptions();
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<float>();
        }

        var key = ComputeHash(text);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var request = new EmbeddingRequest(_options.ModelName, new[] { text });
        var response = await _ollamaClient.EmbedAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Embeddings.Count == 0)
        {
            return Array.Empty<float>();
        }

        var embedding = response.Embeddings[0];
        TryCache(key, embedding);
        return embedding;
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var results = new float[texts.Count][];
        var missingIndices = new List<int>();
        var missingTexts = new List<string>();

        // Step 1: Check in-memory cache
        for (var i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            if (string.IsNullOrWhiteSpace(text))
            {
                results[i] = Array.Empty<float>();
                continue;
            }

            var key = ComputeHash(text);
            if (_cache.TryGetValue(key, out var cached))
            {
                results[i] = cached;
            }
            else
            {
                missingIndices.Add(i);
                missingTexts.Add(text);
            }
        }

        // If all items were cached, return immediately
        if (missingTexts.Count == 0)
        {
            return results;
        }

        // Step 2: Batch uncached requests to Ollama
        var batchSize = Math.Max(1, _options.BatchSize);
        for (var b = 0; b < missingTexts.Count; b += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentBatchCount = Math.Min(batchSize, missingTexts.Count - b);
            var batchTexts = new List<string>(currentBatchCount);
            for (var j = 0; j < currentBatchCount; j++)
            {
                batchTexts.Add(missingTexts[b + j]);
            }

            var req = new EmbeddingRequest(_options.ModelName, batchTexts);
            var resp = await _ollamaClient.EmbedAsync(req, cancellationToken).ConfigureAwait(false);

            for (var j = 0; j < resp.Embeddings.Count && j < currentBatchCount; j++)
            {
                var originalIdx = missingIndices[b + j];
                var embedding = resp.Embeddings[j];
                results[originalIdx] = embedding;

                var key = ComputeHash(batchTexts[j]);
                TryCache(key, embedding);
            }
        }

        return results;
    }

    private void TryCache(string key, float[] vector)
    {
        if (_cache.Count >= _options.MaxCacheEntries)
        {
            // Evict some entries when cache limit is exceeded
            _cache.Clear();
        }

        _cache[key] = vector;
    }

    private static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

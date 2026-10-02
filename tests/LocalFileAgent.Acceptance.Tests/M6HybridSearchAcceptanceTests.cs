using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Application.Search;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Domain.Storage;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Infrastructure.Ollama;
using LocalFileAgent.Infrastructure.Storage;
using LocalFileAgent.Text;
using Xunit;

namespace LocalFileAgent.Acceptance.Tests;

public class M6HybridSearchAcceptanceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public M6HybridSearchAcceptanceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LocalFileAgent_M6Acceptance_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "m6_acceptance.db");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task M6_EndToEnd_HybridSearch_CombinesLexicalAndSemanticSearch()
    {
        // 1. Initialize SQLite store, vector index, and normalizer
        await using var indexStore = new SqliteIndexStore(_dbPath);
        await using var vectorIndex = new SqliteVectorIndex(_dbPath);

        await indexStore.InitializeAsync();
        await vectorIndex.InitializeAsync();

        var normalizer = new ArabicTextNormalizer();

        // 2. Index 3 distinct Arabic documents
        var file1 = new IndexedFile(
            Id: 0,
            Path: "C:\\Users\\User\\Documents\\invoice_2025.pdf",
            Name: "invoice_2025.pdf",
            Extension: ".pdf",
            SizeBytes: 1024,
            CreatedAt: DateTimeOffset.UtcNow,
            ModifiedAt: DateTimeOffset.UtcNow,
            IndexedAt: DateTimeOffset.UtcNow,
            ETag: "1",
            Status: "indexed"
        );
        var f1Id = await indexStore.UpsertFileAsync(file1);

        var file2 = new IndexedFile(
            Id: 0,
            Path: "C:\\Users\\User\\Documents\\lease_contract.docx",
            Name: "lease_contract.docx",
            Extension: ".docx",
            SizeBytes: 2048,
            CreatedAt: DateTimeOffset.UtcNow,
            ModifiedAt: DateTimeOffset.UtcNow,
            IndexedAt: DateTimeOffset.UtcNow,
            ETag: "2",
            Status: "indexed"
        );
        var f2Id = await indexStore.UpsertFileAsync(file2);

        var file3 = new IndexedFile(
            Id: 0,
            Path: "C:\\Users\\User\\Documents\\financial_report.xlsx",
            Name: "financial_report.xlsx",
            Extension: ".xlsx",
            SizeBytes: 4096,
            CreatedAt: DateTimeOffset.UtcNow,
            ModifiedAt: DateTimeOffset.UtcNow,
            IndexedAt: DateTimeOffset.UtcNow,
            ETag: "3",
            Status: "indexed"
        );
        var f3Id = await indexStore.UpsertFileAsync(file3);

        // Chunks
        var chunk1 = new IndexedChunk(0, f1Id, 1, 0, "فاتورة ضريبية رسمية لتوريد أجهزة حاسوب محمولة", normalizer.Normalize("فاتورة ضريبية رسمية لتوريد أجهزة حاسوب محمولة", NormalizationProfile.Search).ProcessedText, "text_layer");
        var chunk2 = new IndexedChunk(0, f2Id, 1, 0, "عقد إيجار شقة سكنية بالقاهرة الجديدة لمدة سنة", normalizer.Normalize("عقد إيجار شقة سكنية بالقاهرة الجديدة لمدة سنة", NormalizationProfile.Search).ProcessedText, "text_layer");
        var chunk3 = new IndexedChunk(0, f3Id, 1, 0, "تقرير مالي ربع سنوي يتضمن الأرباح والمصروفات التشغيلية", normalizer.Normalize("تقرير مالي ربع سنوي يتضمن الأرباح والمصروفات التشغيلية", NormalizationProfile.Search).ProcessedText, "text_layer");

        var c1Ids = await indexStore.InsertChunksAsync(f1Id, new[] { chunk1 });
        var c2Ids = await indexStore.InsertChunksAsync(f2Id, new[] { chunk2 });
        var c3Ids = await indexStore.InsertChunksAsync(f3Id, new[] { chunk3 });

        c1Ids.Should().ContainSingle();
        c2Ids.Should().ContainSingle();
        c3Ids.Should().ContainSingle();

        var c1Id = c1Ids[0];
        var c2Id = c2Ids[0];
        var c3Id = c3Ids[0];

        // 3. Setup embeddings (Live Ollama if available, otherwise deterministic semantic vectors)
        IEmbeddingService embeddingService;
        var useLiveOllama = false;

        try
        {
            using var pingClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            var ping = await pingClient.GetAsync("http://127.0.0.1:11434/api/version");
            if (ping.IsSuccessStatusCode)
            {
                useLiveOllama = true;
            }
        }
        catch
        {
            useLiveOllama = false;
        }

        if (useLiveOllama)
        {
            var ollamaClient = new OllamaClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:11434") });
            embeddingService = new EmbeddingService(ollamaClient, new EmbeddingOptions { ModelName = "bge-m3" });
        }
        else
        {
            // Deterministic mock embedding service for isolated offline testing
            embeddingService = new DeterministicSemanticEmbeddingService();
        }

        var vec1 = await embeddingService.GenerateEmbeddingAsync(chunk1.TextRaw);
        var vec2 = await embeddingService.GenerateEmbeddingAsync(chunk2.TextRaw);
        var vec3 = await embeddingService.GenerateEmbeddingAsync(chunk3.TextRaw);

        await vectorIndex.AddAsync(new[]
        {
            (c1Id, vec1),
            (c2Id, vec2),
            (c3Id, vec3)
        });

        // 4. Create HybridSearchService
        var hybridSearch = new HybridSearchService(
            indexStore,
            normalizer,
            vectorIndex,
            embeddingService
        );

        // 5. Test Query A: Exact keyword query ("فاتورة ضريبية")
        // Should find invoice_2025.pdf via both FTS and vector (hybrid match)
        var resultsA = await hybridSearch.SearchAsync(new SearchRequest("فاتورة ضريبية"));
        resultsA.Should().NotBeEmpty();
        resultsA[0].FileName.Should().Be("invoice_2025.pdf");
        resultsA[0].MatchKind.Should().BeOneOf("hybrid", "fts");
        resultsA[0].PageNumber.Should().Be(1);
        resultsA[0].SourceKind.Should().Be("text_layer");

        // 6. Test Query B: Semantic query with zero exact token overlap
        // Query: "اتفاقية سكن" vs chunk "عقد إيجار شقة سكنية"
        var resultsB = await hybridSearch.SearchAsync(new SearchRequest("اتفاقية سكن"));
        resultsB.Should().NotBeEmpty();
        resultsB[0].FileName.Should().Be("lease_contract.docx");

        // 7. Test Query C: Financial report query
        var resultsC = await hybridSearch.SearchAsync(new SearchRequest("الأرباح والمصروفات"));
        resultsC.Should().NotBeEmpty();
        resultsC[0].FileName.Should().Be("financial_report.xlsx");
    }

    private sealed class DeterministicSemanticEmbeddingService : IEmbeddingService
    {
        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            // 3-dimensional deterministic semantic cluster
            float[] vec;
            if (text.Contains("فاتورة") || text.Contains("حاسوب") || text.Contains("توريد"))
            {
                vec = new float[] { 0.95f, 0.05f, 0.05f };
            }
            else if (text.Contains("عقد") || text.Contains("إيجار") || text.Contains("شقة") || text.Contains("اتفاقية") || text.Contains("سكن"))
            {
                vec = new float[] { 0.05f, 0.95f, 0.05f };
            }
            else
            {
                vec = new float[] { 0.05f, 0.05f, 0.95f };
            }

            return Task.FromResult(vec);
        }

        public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            var results = new List<float[]>(texts.Count);
            foreach (var t in texts)
            {
                results.Add(await GenerateEmbeddingAsync(t, cancellationToken));
            }
            return results;
        }
    }
}

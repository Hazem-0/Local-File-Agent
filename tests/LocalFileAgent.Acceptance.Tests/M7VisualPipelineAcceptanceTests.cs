using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Application.FileSystem;
using LocalFileAgent.Application.Indexing;
using LocalFileAgent.Application.Search;
using LocalFileAgent.Domain.FileSystem;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Domain.Storage;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Domain.Worker;
using LocalFileAgent.Infrastructure.Storage;
using LocalFileAgent.Infrastructure.Vision;
using LocalFileAgent.Text;
using Xunit;

namespace LocalFileAgent.Acceptance.Tests;

public class M7VisualPipelineAcceptanceTests : IAsyncLifetime, IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private SqliteIndexStore _indexStore = null!;
    private SqliteVectorIndex _vectorIndex = null!;
    private SqliteVisualVectorIndex _visualVectorIndex = null!;
    private ArabicTextNormalizer _normalizer = null!;
    private ArabicChunker _chunker = null!;

    public M7VisualPipelineAcceptanceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lfa_m7_acc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "m7_acceptance.db");
    }

    public async Task InitializeAsync()
    {
        _indexStore = new SqliteIndexStore(_dbPath);
        _vectorIndex = new SqliteVectorIndex(_dbPath);
        _visualVectorIndex = new SqliteVisualVectorIndex(_dbPath);

        await _indexStore.InitializeAsync();
        await _vectorIndex.InitializeAsync();
        await _visualVectorIndex.InitializeAsync();

        _normalizer = new ArabicTextNormalizer();
        _chunker = new ArabicChunker();
    }

    public async Task DisposeAsync()
    {
        if (_visualVectorIndex != null)
        {
            await _visualVectorIndex.DisposeAsync();
        }
        if (_vectorIndex != null)
        {
            await _vectorIndex.DisposeAsync();
        }
        if (_indexStore != null)
        {
            await _indexStore.DisposeAsync();
        }
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    private static string GetSyntheticCorpusPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "corpus", "synthetic");
    }

    private sealed class FakeWorkerClient : IWorkerClient
    {
        public Func<WorkerParseRequest, WorkerParseResponse>? OnParse { get; set; }

        Task IWorkerClient.StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        Task<bool> IWorkerClient.PingAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<WorkerParseResponse> ParseFileAsync(WorkerParseRequest request, CancellationToken cancellationToken = default)
        {
            if (OnParse != null)
            {
                return Task.FromResult(OnParse(request));
            }

            return Task.FromResult(new WorkerParseResponse(request.RequestId, true, Array.Empty<ExtractedPage>(), null, 10.0));
        }

        ValueTask IAsyncDisposable.DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeTier2OcrService : ITier2OcrService
    {
        public string ExtractedText { get; set; } = "فاتورة مبيعات رقم ٢٠٢٦ القيمة الإجمالية ٥٠٠٠ ريال";

        public Task<OcrExtractionResult?> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OcrExtractionResult?>(new OcrExtractionResult(
                Text: ExtractedText,
                Confidence: 0.95f,
                Engine: "ocr_glm"
            ));
        }

        public Task<OcrExtractionResult?> RecognizeBase64Async(string base64Image, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OcrExtractionResult?>(new OcrExtractionResult(
                Text: ExtractedText,
                Confidence: 0.95f,
                Engine: "ocr_glm"
            ));
        }
    }

    private sealed class FakeVisualDescriber : IVisualDescriber
    {
        public string Caption { get; set; } = "صورة ملتقطة لعقد إيجار تجاري";
        public string Summary { get; set; } = "صورة ملتقطة لعقد إيجار تجاري موثق يتضمن تفاصيل المستأجر والعقار ومدة الإيجار السنوية.";

        public Task<VisualDescriptionResult?> DescribeImageAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<VisualDescriptionResult?>(new VisualDescriptionResult(Caption, Summary, 0.90f));
        }

        public Task<VisualDescriptionResult?> DescribeImageBase64Async(string base64Image, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<VisualDescriptionResult?>(new VisualDescriptionResult(Caption, Summary, 0.90f));
        }
    }

    [Fact]
    public async Task M7_PerceptualImageHashing_DeduplicatesSyntheticImages()
    {
        var corpusPath = GetSyntheticCorpusPath();
        var img1 = Path.Combine(corpusPath, "صورة_فاتورة_ممسوحة_206.png");
        var img2 = Path.Combine(corpusPath, "صورة_فاتورة_ممسوحة_207.png");

        if (!File.Exists(img1) || !File.Exists(img2))
        {
            return;
        }

        var hasher = new ImageHasher();
        var hash1 = await hasher.ComputeDHashAsync(img1);
        var hash1Copy = await hasher.ComputeDHashAsync(img1);
        var hash2 = await hasher.ComputeDHashAsync(img2);

        // Identical file produces exact 0 distance
        hash1.Should().NotBe(0UL);
        hasher.ComputeHammingDistance(hash1, hash1Copy).Should().Be(0);
        hasher.AreNearDuplicates(hash1, hash1Copy).Should().BeTrue();

        // Invoice template copies are recognized as near duplicates
        hasher.AreNearDuplicates(hash1, hash2, threshold: 5).Should().BeTrue();

        // Completely distinct hash has high distance and is not a near duplicate
        var distinctHash = ~hash1;
        hasher.ComputeHammingDistance(hash1, distinctHash).Should().Be(64);
        hasher.AreNearDuplicates(hash1, distinctHash).Should().BeFalse();
    }

    [Fact]
    public async Task M7_TwoTierOcrEscalation_AndVisualDescriber_EndToEndIndexingAndSearch()
    {
        var corpusPath = GetSyntheticCorpusPath();
        var targetImage = Path.Combine(corpusPath, "صورة_فاتورة_ممسوحة_206.png");

        if (!File.Exists(targetImage))
        {
            return;
        }

        // 1. Simulate Tier 1 returning degraded/empty OCR so pipeline escalates to Tier 2
        var fakeWorker = new FakeWorkerClient
        {
            OnParse = req =>
            {
                // Degraded OCR response with confidence below quality threshold
                var degradedPage = new ExtractedPage(1, "??? ???", "ocr_win", 0.20f);
                return new WorkerParseResponse(req.RequestId, true, new[] { degradedPage }, null, 50.0);
            }
        };

        var tier2Ocr = new FakeTier2OcrService();
        var visualDescriber = new FakeVisualDescriber();
        var visualEmbedder = new VisualEmbeddingService();
        var scanner = new FileScanner();

        var orchestrator = new IndexOrchestrator(
            scanner: scanner,
            worker: fakeWorker,
            indexStore: _indexStore,
            normalizer: _normalizer,
            chunker: _chunker,
            vectorIndex: _vectorIndex,
            embeddingService: null,
            visualVectorIndex: _visualVectorIndex,
            visualEmbedding: visualEmbedder,
            tier2Ocr: tier2Ocr,
            visualDescriber: visualDescriber
        );

        // Scan only the synthetic corpus directory with .png filter
        var scanOptions = new ScanOptions(
            MaxDepth: 1,
            IncludedExtensions: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png" },
            MaxFileSizeBytes: 10 * 1024 * 1024
        );

        var result = await orchestrator.IndexDirectoryAsync(corpusPath, scanOptions);
        result.IndexedCount.Should().BeGreaterThan(0);

        // 2. Verify that Tier 2 OCR and VLM chunks were indexed into SQLite
        var indexedFile = await _indexStore.GetFileByPathAsync(targetImage);
        indexedFile.Should().NotBeNull();
        indexedFile!.Status.Should().Be("indexed");

        // 3. Search via FTS5 for Tier 2 OCR extracted text
        var searchService = new HybridSearchService(
            indexStore: _indexStore,
            normalizer: _normalizer,
            vectorIndex: _vectorIndex,
            embeddingService: null,
            visualVectorIndex: _visualVectorIndex,
            visualEmbedding: visualEmbedder
        );

        var ocrHits = await searchService.SearchAsync(new SearchRequest("فاتورة مبيعات ٢٠٢٦"));
        ocrHits.Should().NotBeEmpty();
        ocrHits.Any(h => h.FilePath.Equals(targetImage, StringComparison.OrdinalIgnoreCase)).Should().BeTrue();

        // 4. Search for VLM Visual Description text (scene/doc summary)
        var vlmHits = await searchService.SearchAsync(new SearchRequest("عقد إيجار تجاري"));
        vlmHits.Should().NotBeEmpty();
        vlmHits.Any(h => h.FilePath.Equals(targetImage, StringComparison.OrdinalIgnoreCase)).Should().BeTrue();

        // 5. Verify Visual Vector Index contains entries and supports visual similarity search
        var visualCount = await _visualVectorIndex.GetCountAsync();
        visualCount.Should().BeGreaterThan(0);

        var visualQuery = await visualEmbedder.GenerateTextEmbeddingAsync("عقد إيجار");
        var visualSearchResults = await _visualVectorIndex.SearchAsync(visualQuery, k: 5);
        visualSearchResults.Should().NotBeEmpty();
    }
}

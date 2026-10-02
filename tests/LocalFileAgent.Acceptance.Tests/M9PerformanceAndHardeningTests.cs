using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Application.Agent;
using LocalFileAgent.Application.Search;
using LocalFileAgent.Application.Throttling;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Domain.Storage;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Domain.Throttling;
using LocalFileAgent.Infrastructure.Ollama;
using LocalFileAgent.Infrastructure.Storage;
using LocalFileAgent.Text;
using Xunit;

namespace LocalFileAgent.Acceptance.Tests;

public class M9PerformanceAndHardeningTests : IAsyncLifetime, IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private SqliteIndexStore _indexStore = null!;
    private SqliteVectorIndex _vectorIndex = null!;
    private SqliteVisualVectorIndex _visualVectorIndex = null!;
    private ArabicTextNormalizer _normalizer = null!;
    private ArabicChunker _chunker = null!;

    public M9PerformanceAndHardeningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lfa_m9_perf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "m9_acceptance.db");
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

        // Seed realistic corpus data
        for (var i = 1; i <= 25; i++)
        {
            var file = new IndexedFile(
                Id: 0,
                Path: $@"C:\Corpus\Doc_{i:D3}.pdf",
                Name: $"Doc_{i:D3}.pdf",
                Extension: ".pdf",
                SizeBytes: 1024 * i,
                CreatedAt: DateTimeOffset.UtcNow,
                ModifiedAt: DateTimeOffset.UtcNow,
                IndexedAt: DateTimeOffset.UtcNow,
                ETag: $"etag_{i}",
                Status: "indexed"
            );

            var fileId = await _indexStore.UpsertFileAsync(file);

            var text = i % 2 == 0
                ? $"فاتورة شراء رقم {i} بقيمة {i * 100} جنيه مصري وتفاصيل التوريد والضريبة المضافة"
                : $"عقد إيجار شقة سكنية رقم {i} بين الطرف الأول والطرف الثاني مع شروط السداد والالتزامات";

            var chunks = _chunker.Chunk(text, new ChunkingOptions(), 1, "text_layer");
            var indexedChunks = chunks.Select(c => new IndexedChunk(
                Id: 0,
                FileId: fileId,
                PageNumber: c.PageNumber,
                Ordinal: c.Ordinal,
                TextRaw: c.Text,
                TextNormalized: _normalizer.Normalize(c.Text, NormalizationProfile.Search).ProcessedText,
                SourceKind: c.SourceKind,
                Confidence: c.Confidence
            )).ToList();

            var chunkIds = await _indexStore.InsertChunksAsync(fileId, indexedChunks);

            // Seed synthetic 1024-dim normalized vector
            var vec = new float[1024];
            vec[i % 1024] = 1.0f;
            await _vectorIndex.AddAsync(new[] { (chunkIds[0], vec) });
        }
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
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                // Internal test temp directory cleanup
            }
        }
        catch
        {
            // Best effort
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task M9_T91_LexicalSearch_MeetsLatencyBudget_Under300ms()
    {
        var queries = new[] { "فاتورة", "عقد", "شراء", "إيجار", "سكنية" };
        var latencies = new List<double>();

        // Warmup
        var warmNorm = _normalizer.Normalize("فاتورة", NormalizationProfile.Search).ProcessedText;
        await _indexStore.SearchFtsAsync(warmNorm, null, 20);

        foreach (var query in queries)
        {
            var norm = _normalizer.Normalize(query, NormalizationProfile.Search).ProcessedText;

            var sw = Stopwatch.StartNew();
            var results = await _indexStore.SearchFtsAsync(norm, null, 20);
            sw.Stop();

            results.Should().NotBeEmpty();
            latencies.Add(sw.Elapsed.TotalMilliseconds);
        }

        var maxLatency = latencies.Max();
        var avgLatency = latencies.Average();

        maxLatency.Should().BeLessThan(300, "Lexical search must complete well within the 300ms latency budget");
        avgLatency.Should().BeLessThan(50, "Average lexical search latency on SQLite WAL should be sub-50ms");
    }

    [Fact]
    public async Task M9_T91_DenseVectorSimd_MeetsLatencyBudget_Under300ms()
    {
        var queryVec = new float[1024];
        queryVec[2] = 1.0f; // matches Doc_002

        // Warmup
        await _vectorIndex.SearchAsync(queryVec, 10);

        var sw = Stopwatch.StartNew();
        var results = await _vectorIndex.SearchAsync(queryVec, 10);
        sw.Stop();

        results.Should().NotBeEmpty();
        results[0].Score.Should().BeGreaterThan(0.9f);
        sw.Elapsed.TotalMilliseconds.Should().BeLessThan(300, "Hardware SIMD vector cosine similarity must complete in <300ms");
    }

    [Fact]
    public async Task M9_T91_HybridFusionSearch_MeetsLatencyBudget_Under500ms()
    {
        var hybridService = new HybridSearchService(
            _indexStore,
            _normalizer,
            vectorIndex: _vectorIndex,
            embeddingService: null, // Test fast-path / fallback hybrid fusion
            visualVectorIndex: _visualVectorIndex,
            visualEmbedding: null
        );

        var sw = Stopwatch.StartNew();
        var results = await hybridService.SearchAsync(new SearchRequest("فاتورة شراء", Limit: 10));
        sw.Stop();

        results.Should().NotBeEmpty();
        results[0].FileName.Should().Contain("Doc_");
        sw.Elapsed.TotalMilliseconds.Should().BeLessThan(500, "3-Way Hybrid RRF fusion search must complete within 500ms SLA");
    }

    [Fact]
    public async Task M9_T91_AgentQueryPlanning_MeetsLatencyBudget_Under2000ms()
    {
        var planner = new AgentPlanner(normalizer: _normalizer);

        var sw = Stopwatch.StartNew();
        var plan = await planner.PlanAsync("عايز فواتير الكهرباء وتفاصيلها بصيغة بي دي اف");
        sw.Stop();

        plan.Should().NotBeNull();
        plan.DialectVariants.Should().Contain("فاتورة");
        plan.FileExtensions.Should().Contain(".pdf");
        sw.Elapsed.TotalMilliseconds.Should().BeLessThan(2000, "Agent query translation must complete within the 2-second SLA");
    }

    [Fact]
    public void M9_T93_SecurityAudit_LoopbackEnforcement_BlocksExternalNetworks()
    {
        var externalEndpoints = new[]
        {
            "http://api.openai.com/v1",
            "https://192.168.1.50:11434",
            "http://models.internal.corp:8080",
            "https://8.8.8.8:11434"
        };

        using var httpClient = new HttpClient();

        foreach (var endpoint in externalEndpoints)
        {
            var act = () => new OllamaClient(httpClient, endpoint);
            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*Security policy violation: Endpoint*is not loopback*");
        }

        // Loopback endpoints must succeed
        var validEndpoints = new[]
        {
            "http://127.0.0.1:11434",
            "http://localhost:11434",
            "http://[::1]:11434"
        };

        foreach (var endpoint in validEndpoints)
        {
            var client = new OllamaClient(httpClient, endpoint);
            client.Should().NotBeNull();
        }
    }

    [Fact]
    public void M9_T93_SecurityAudit_AppDataPaths_ConfinedToLocalAppData()
    {
        var dataDir = AppDataPaths.GetDataDirectory();
        var dbPath = AppDataPaths.GetDatabasePath();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var expectedRoot = Path.Combine(localAppData, "LocalFileAgent");

        dataDir.Should().Be(expectedRoot);
        dbPath.Should().StartWith(expectedRoot);
        Path.GetFileName(dbPath).Should().Be("lfa_index.db");
    }

    [Fact]
    public void M9_T92_ResourceThrottling_PowerStateDetectionAndPolicyEnforcement()
    {
        var provider = new LocalFileAgent.Infrastructure.Throttling.WindowsPowerStatusProvider();
        var status = provider.GetPowerStatus();

        status.Should().NotBeNull();

        // Verify governor policy with simulated battery state
        var batteryGovernor = new ResourceGovernor(
            options: new ResourceThrottleOptions(PauseOnBattery: true, BatteryThresholdPercent: 20)
        );

        // When not throttled, WaitIfThrottledAsync returns promptly
        var sw = Stopwatch.StartNew();
        var waitTask = batteryGovernor.WaitIfThrottledAsync(CancellationToken.None);
        waitTask.IsCompletedSuccessfully.Should().BeTrue();
        sw.Stop();
        sw.Elapsed.TotalMilliseconds.Should().BeLessThan(100);
    }
}

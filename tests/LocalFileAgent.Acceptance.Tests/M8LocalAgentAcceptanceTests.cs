using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Application.Agent;
using LocalFileAgent.Application.FileSystem;
using LocalFileAgent.Application.Indexing;
using LocalFileAgent.Application.Search;
using LocalFileAgent.Domain.FileSystem;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Domain.Worker;
using LocalFileAgent.Infrastructure.Storage;
using LocalFileAgent.Infrastructure.Vision;
using LocalFileAgent.Text;
using Xunit;

namespace LocalFileAgent.Acceptance.Tests;

public class M8LocalAgentAcceptanceTests : IAsyncLifetime, IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private SqliteIndexStore _indexStore = null!;
    private SqliteVectorIndex _vectorIndex = null!;
    private SqliteVisualVectorIndex _visualVectorIndex = null!;
    private ArabicTextNormalizer _normalizer = null!;
    private ArabicChunker _chunker = null!;

    public M8LocalAgentAcceptanceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lfa_m8_acc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "m8_acceptance.db");
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
        Task IWorkerClient.StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        Task<bool> IWorkerClient.PingAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<WorkerParseResponse> ParseFileAsync(WorkerParseRequest request, CancellationToken cancellationToken = default)
        {
            var p1 = new ExtractedPage(1, "فاتورة مبيعات رقم ٢٠٢٦ القيمة الإجمالية ٥٠٠٠ ريال", "ocr_win", 0.95f);
            return Task.FromResult(new WorkerParseResponse(request.RequestId, true, new[] { p1 }, null, 10.0));
        }

        ValueTask IAsyncDisposable.DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAgentOllamaClient : IOllamaClient
    {
        public Func<ChatRequest, ChatResponse>? OnChat { get; set; }

        public Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            if (OnChat != null)
            {
                return Task.FromResult(OnChat(request));
            }

            return Task.FromResult(new ChatResponse("gemma4:e2b", "تم العثور على الفاتورة المطلوبة بنجاح.", 20, 20, 100));
        }

        public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult("0.5.0");
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModelInfo>>(Array.Empty<ModelInfo>());
        public Task<GenerateResponse> GenerateAsync(GenerateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new GenerateResponse("test", "test", 0, 0, 0.0));
        public Task<EmbeddingResponse> EmbedAsync(EmbeddingRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new EmbeddingResponse("test", Array.Empty<float[]>(), 0, 0.0));
    }

    [Fact]
    public async Task M8_EndToEnd_AgentQuestionAndGroundedSynthesis()
    {
        var corpusPath = GetSyntheticCorpusPath();
        var targetImage = Path.Combine(corpusPath, "صورة_فاتورة_ممسوحة_206.png");

        if (!File.Exists(targetImage))
        {
            return;
        }

        // 1. Index document into store
        var fakeWorker = new FakeWorkerClient();
        var scanner = new FileScanner();
        var visualEmbedder = new VisualEmbeddingService();

        var orchestrator = new IndexOrchestrator(
            scanner: scanner,
            worker: fakeWorker,
            indexStore: _indexStore,
            normalizer: _normalizer,
            chunker: _chunker,
            vectorIndex: _vectorIndex,
            embeddingService: null,
            visualVectorIndex: _visualVectorIndex,
            visualEmbedding: visualEmbedder
        );

        var scanOptions = new ScanOptions(
            MaxDepth: 1,
            IncludedExtensions: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png" },
            MaxFileSizeBytes: 10 * 1024 * 1024
        );

        await orchestrator.IndexDirectoryAsync(corpusPath, scanOptions);

        // 2. Setup Search and Agent
        var searchService = new HybridSearchService(
            indexStore: _indexStore,
            normalizer: _normalizer,
            vectorIndex: _vectorIndex,
            embeddingService: null,
            visualVectorIndex: _visualVectorIndex,
            visualEmbedding: visualEmbedder
        );

        var fakeOllama = new FakeAgentOllamaClient
        {
            OnChat = req =>
            {
                // Call B synthesis response mentioning the actual filename
                var content = "بناءً على نتائج البحث، تم العثور على الفاتورة في الملف \"صورة_فاتورة_ممسوحة_206.png\" برقم 2026 وقيمة 5000 ريال.";
                return new ChatResponse("gemma4:e2b", content, 30, 30, 200);
            }
        };

        var planner = new AgentPlanner(ollamaClient: fakeOllama, normalizer: _normalizer);
        var validator = new GroundingValidator();
        var agent = new AgentService(planner, searchService, validator, fakeOllama);

        // 3. User asks an Egyptian dialect question: "عاوز فواتير مبيعات 2026"
        var answer = await agent.AskAsync("عاوز فواتير مبيعات 2026");

        answer.Should().NotBeNull();
        answer.IsGrounded.Should().BeTrue();
        answer.Hits.Should().NotBeEmpty();
        answer.VerifiedCitations.Should().NotBeEmpty();
        answer.ResponseText.Should().Contain("صورة_فاتورة_ممسوحة_206.png");
    }

    [Fact]
    public async Task M8_HallucinationDetection_NeutralizesUncitedFilePaths()
    {
        var corpusPath = GetSyntheticCorpusPath();
        var targetImage = Path.Combine(corpusPath, "صورة_فاتورة_ممسوحة_206.png");

        if (!File.Exists(targetImage))
        {
            return;
        }

        var fakeWorker = new FakeWorkerClient();
        var scanner = new FileScanner();
        var orchestrator = new IndexOrchestrator(scanner, fakeWorker, _indexStore, _normalizer, _chunker);

        var scanOptions = new ScanOptions(
            MaxDepth: 1,
            IncludedExtensions: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png" }
        );
        await orchestrator.IndexDirectoryAsync(corpusPath, scanOptions);

        var searchService = new HybridSearchService(_indexStore, _normalizer);

        // Model hallucinates an unauthorized/invented file path
        var fakeOllama = new FakeAgentOllamaClient
        {
            OnChat = req =>
            {
                var content = "وجدت الملف في \"صورة_فاتورة_ممسوحة_206.png\" وأيضاً معلومات في C:\\Private\\passwords.txt.";
                return new ChatResponse("gemma4:e2b", content, 30, 30, 200);
            }
        };

        var planner = new AgentPlanner(ollamaClient: fakeOllama, normalizer: _normalizer);
        var validator = new GroundingValidator();
        var agent = new AgentService(planner, searchService, validator, fakeOllama);

        var answer = await agent.AskAsync("فواتير المبيعات");

        answer.Should().NotBeNull();
        answer.IsGrounded.Should().BeFalse(); // Hallucination flagged
        answer.ResponseText.Should().NotContain("passwords.txt"); // Hallucinated path sanitized
        answer.ResponseText.Should().Contain("[مسار غير موثق محذوف]");
    }
}

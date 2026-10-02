using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Application.Indexing;
using LocalFileAgent.Application.Throttling;
using LocalFileAgent.Domain.FileSystem;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Domain.Storage;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Domain.Throttling;
using LocalFileAgent.Domain.Worker;
using LocalFileAgent.Text;
using Xunit;

namespace LocalFileAgent.Application.Tests;

public class ResourceGovernorTests
{
    private sealed class MockPowerStatusProvider : IPowerStatusProvider
    {
        public bool IsOnBattery { get; set; }
        public int? BatteryLifePercent { get; set; }

        public PowerStatusInfo GetPowerStatus() =>
            new(IsOnBattery, BatteryLifePercent, IsOnBattery && (BatteryLifePercent == null || BatteryLifePercent <= 20));
    }

    private sealed class MockIndexStore : IIndexStore
    {
        public readonly List<IndexedFile> UpsertedFiles = new();
        public readonly List<IndexedChunk> InsertedChunks = new();

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<long> UpsertFileAsync(IndexedFile file, CancellationToken cancellationToken = default)
        {
            UpsertedFiles.Add(file);
            return Task.FromResult((long)UpsertedFiles.Count);
        }
        public Task<IndexedFile?> GetFileByPathAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<IndexedFile?>(null);
        public Task<IndexedFile?> GetFileByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult<IndexedFile?>(null);
        public Task<IReadOnlyList<long>> InsertChunksAsync(long fileId, IReadOnlyList<IndexedChunk> chunks, CancellationToken cancellationToken = default)
        {
            InsertedChunks.AddRange(chunks);
            var ids = new List<long>();
            for (var i = 0; i < chunks.Count; i++) ids.Add(i + 1);
            return Task.FromResult<IReadOnlyList<long>>(ids);
        }
        public Task DeleteFileAsync(long fileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<LocalFileAgent.Domain.Search.SearchResultItem>> GetChunksByIdsAsync(IReadOnlyList<long> chunkIds, IReadOnlyList<string>? scopePaths = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalFileAgent.Domain.Search.SearchResultItem>>(Array.Empty<LocalFileAgent.Domain.Search.SearchResultItem>());
        public Task<IReadOnlyList<LocalFileAgent.Domain.Search.SearchResultItem>> SearchFtsAsync(string normalizedQuery, IReadOnlyList<string>? scopePaths = null, int limit = 20, bool useTrigram = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalFileAgent.Domain.Search.SearchResultItem>>(Array.Empty<LocalFileAgent.Domain.Search.SearchResultItem>());
        public Task<long> GetIndexedFileCountAsync(CancellationToken cancellationToken = default) => Task.FromResult((long)UpsertedFiles.Count);
        public Task<long> GetChunkCountAsync(CancellationToken cancellationToken = default) => Task.FromResult((long)InsertedChunks.Count);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MockWorkerClient : IWorkerClient
    {
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<WorkerParseResponse> ParseFileAsync(WorkerParseRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WorkerParseResponse(
                RequestId: request.RequestId,
                Success: true,
                Pages: new[] { new ExtractedPage(1, "نص صفحة", "ocr_win", 0.40f) }
            ));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MockTier2Ocr : ITier2OcrService
    {
        public int CallCount { get; private set; }
        public Task<OcrExtractionResult?> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<OcrExtractionResult?>(new OcrExtractionResult("نص عالي الجودة من النموذج الثاني", 0.95f, "ocr_glm"));
        }
        public Task<OcrExtractionResult?> RecognizeBase64Async(string base64Image, CancellationToken cancellationToken = default) =>
            RecognizeAsync(string.Empty, cancellationToken);
    }

    private sealed class MockVisualDescriber : IVisualDescriber
    {
        public int CallCount { get; private set; }
        public Task<VisualDescriptionResult?> DescribeImageAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<VisualDescriptionResult?>(new VisualDescriptionResult("فاتورة كهرباء", "فاتورة رسمية من شركة الكهرباء", 0.90f));
        }
        public Task<VisualDescriptionResult?> DescribeImageBase64Async(string base64Image, CancellationToken cancellationToken = default) =>
            DescribeImageAsync(string.Empty, cancellationToken);
    }

    private sealed class MockFileScanner : IFileScanner
    {
        private readonly List<DiscoveredFile> _files;
        public MockFileScanner(List<DiscoveredFile> files) => _files = files;

        public async IAsyncEnumerable<DiscoveredFile> ScanAsync(
            string rootPath,
            ScanOptions options,
            IProgress<ScanProgressReport>? progress = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var file in _files)
            {
                yield return file;
            }
            await Task.CompletedTask;
        }
    }

    [Fact]
    public void ResourceGovernor_OnAcPower_DoesNotThrottle()
    {
        var provider = new MockPowerStatusProvider { IsOnBattery = false, BatteryLifePercent = 100 };
        var governor = new ResourceGovernor(provider, new ResourceThrottleOptions(PauseOnBattery: true));

        governor.ShouldThrottleHeavyWork().Should().BeFalse();
        governor.GetPowerStatus().IsThrottled.Should().BeFalse();
    }

    [Fact]
    public void ResourceGovernor_OnBattery_WithPauseOnBatteryTrue_Throttles()
    {
        var provider = new MockPowerStatusProvider { IsOnBattery = true, BatteryLifePercent = 85 };
        var governor = new ResourceGovernor(provider, new ResourceThrottleOptions(PauseOnBattery: true));

        governor.ShouldThrottleHeavyWork().Should().BeTrue();
        governor.GetPowerStatus().IsThrottled.Should().BeTrue();
    }

    [Fact]
    public void ResourceGovernor_OnBattery_WithPauseOnBatteryFalse_RespectsThreshold()
    {
        var provider = new MockPowerStatusProvider { IsOnBattery = true, BatteryLifePercent = 15 };
        var governor = new ResourceGovernor(provider, new ResourceThrottleOptions(PauseOnBattery: false, BatteryThresholdPercent: 20));

        // 15% <= 20% -> throttled
        governor.ShouldThrottleHeavyWork().Should().BeTrue();

        // 50% > 20% -> not throttled
        provider.BatteryLifePercent = 50;
        governor.ShouldThrottleHeavyWork().Should().BeFalse();
    }

    [Fact]
    public async Task IndexOrchestrator_WhenThrottled_DefersSlowLaneOcrAndVlm()
    {
        var fakePath = "C:\\mock_corpus\\sample_invoice.png";

        var files = new List<DiscoveredFile>
        {
            new(
                Path: fakePath,
                Name: "sample_invoice.png",
                Extension: ".png",
                SizeBytes: 1024,
                CreatedAt: DateTimeOffset.UtcNow,
                ModifiedAt: DateTimeOffset.UtcNow,
                Attributes: FileAttributes.Normal
            )
        };

        var scanner = new MockFileScanner(files);
        var worker = new MockWorkerClient();
        var indexStore = new MockIndexStore();
        var normalizer = new ArabicTextNormalizer();
        var chunker = new ArabicChunker();
        var tier2 = new MockTier2Ocr();
        var vlm = new MockVisualDescriber();

        // Set provider on battery so governor throttles heavy operations
        var powerProvider = new MockPowerStatusProvider { IsOnBattery = true, BatteryLifePercent = 10 };
        var governor = new ResourceGovernor(powerProvider, new ResourceThrottleOptions(PauseOnBattery: true));

        var orchestrator = new IndexOrchestrator(
            scanner,
            worker,
            indexStore,
            normalizer,
            chunker,
            vectorIndex: null,
            embeddingService: null,
            visualVectorIndex: null,
            visualEmbedding: null,
            tier2Ocr: tier2,
            visualDescriber: vlm,
            governor: governor
        );

        var result = await orchestrator.IndexDirectoryAsync("C:\\mock_corpus", cancellationToken: CancellationToken.None);

        result.IndexedCount.Should().Be(1);
        // Tier 2 OCR and VLM must NOT have been called due to battery conservation throttle
        tier2.CallCount.Should().Be(0, "Tier 2 OCR should be skipped when throttled to preserve battery");
        vlm.CallCount.Should().Be(0, "Slow-lane VLM should be skipped when throttled to preserve battery");

        // But the file must still be indexed via fast lane text
        indexStore.UpsertedFiles.Should().HaveCount(1);
        indexStore.UpsertedFiles[0].Status.Should().Be("indexed");
    }
}

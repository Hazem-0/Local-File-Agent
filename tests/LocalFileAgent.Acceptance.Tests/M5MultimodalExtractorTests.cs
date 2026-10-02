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
using LocalFileAgent.Infrastructure.Storage;
using LocalFileAgent.Infrastructure.Worker;
using LocalFileAgent.Text;
using Xunit;

namespace LocalFileAgent.Acceptance.Tests;

public class M5MultimodalExtractorTests : IAsyncLifetime, IDisposable
{
    private readonly string _tempDbPath;
    private SqliteIndexStore _indexStore = null!;
    private WorkerClient _worker = null!;
    private IndexOrchestrator _orchestrator = null!;
    private ArabicTextNormalizer _normalizer = null!;
    private ArabicChunker _chunker = null!;
    private FileScanner _scanner = null!;

    public M5MultimodalExtractorTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"lfa_m5_acc_{Guid.NewGuid():N}.db");
    }

    public async Task InitializeAsync()
    {
        _indexStore = new SqliteIndexStore(_tempDbPath);
        await _indexStore.InitializeAsync(CancellationToken.None);

        _normalizer = new ArabicTextNormalizer();
        _chunker = new ArabicChunker();
        _scanner = new FileScanner();
        _worker = new WorkerClient();
        await _worker.StartAsync(CancellationToken.None);

        _orchestrator = new IndexOrchestrator(_scanner, _worker, _indexStore, _normalizer, _chunker);
    }

    public async Task DisposeAsync()
    {
        if (_worker != null)
        {
            await _worker.DisposeAsync();
        }
        if (_indexStore != null)
        {
            await _indexStore.DisposeAsync();
        }
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _worker?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _indexStore?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static string GetSyntheticCorpusPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "corpus", "synthetic");
    }

    [Fact]
    public async Task M5_EndToEnd_IndexOfficeAndPdfDocuments_AndSearchWithProvenance()
    {
        var corpusPath = GetSyntheticCorpusPath();
        Directory.Exists(corpusPath).Should().BeTrue();

        var scanOptions = new ScanOptions(
            MaxDepth: 0,
            IncludedExtensions: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".docx", ".pdf" }
        );

        // 1. Index Office and PDF documents
        var indexResult = await _orchestrator.IndexDirectoryAsync(
            corpusPath,
            scanOptions,
            null,
            CancellationToken.None
        );

        indexResult.IndexedCount.Should().BeGreaterThan(0);
        indexResult.FailedCount.Should().Be(0);

        var fileCount = await _indexStore.GetIndexedFileCountAsync(CancellationToken.None);
        fileCount.Should().Be(indexResult.IndexedCount);

        // 2. Perform searches using SearchViewModel
        using var searchVm = new SearchViewModel(_indexStore, _normalizer, _orchestrator)
        {
            DebounceDelayMs = 0
        };

        // Query A: Search DOCX contract terms
        await searchVm.ExecuteSearchImmediateAsync("عقد بيع وتنازل");
        searchVm.Results.Should().NotBeEmpty();
        var docxMatch = searchVm.Results.FirstOrDefault(r => r.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase));
        docxMatch.Should().NotBeNull();
        docxMatch!.SourceKindDisplay.Should().Be("طبقة النص");
        docxMatch.DisplayPath.StartsWith('\u2066').Should().BeTrue();
        docxMatch.DisplayPath.EndsWith('\u2069').Should().BeTrue();

        // Query B: Search Digital PDF terms
        await searchVm.ExecuteSearchImmediateAsync("تجهيز مكاتب");
        searchVm.Results.Should().NotBeEmpty();
        var pdfDigitalMatch = searchVm.Results.FirstOrDefault(r => r.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        pdfDigitalMatch.Should().NotBeNull();
        pdfDigitalMatch!.SourceKind.Should().Be("text_layer");

        // Query C: Search Scanned PDF OCR terms
        await searchVm.ExecuteSearchImmediateAsync("MARKER_SCANNED_PDF");
        searchVm.Results.Should().NotBeEmpty();
        var pdfScannedMatch = searchVm.Results.FirstOrDefault(r => r.FileName.StartsWith("فاتورة_ممسوحة_ضوئيا", StringComparison.OrdinalIgnoreCase));
        pdfScannedMatch.Should().NotBeNull();
        pdfScannedMatch!.SourceKindDisplay.Should().Be("تعرف ضوئي OCR");
        pdfScannedMatch.Score.Should().BeGreaterThan(0);
    }
}

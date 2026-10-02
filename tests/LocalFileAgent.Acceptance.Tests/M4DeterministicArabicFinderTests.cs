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

public class M4DeterministicArabicFinderTests : IAsyncLifetime, IDisposable
{
    private readonly string _tempDbPath;
    private SqliteIndexStore _indexStore = null!;
    private WorkerClient _worker = null!;
    private IndexOrchestrator _orchestrator = null!;
    private ArabicTextNormalizer _normalizer = null!;
    private ArabicChunker _chunker = null!;
    private FileScanner _scanner = null!;

    public M4DeterministicArabicFinderTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"lfa_m4_acc_{Guid.NewGuid():N}.db");
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

    private static string GetSyntheticTextCorpusPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "corpus", "synthetic");
    }

    [Fact]
    public async Task M4_EndToEnd_IndexCorpusAndPerformArabicSearches()
    {
        var corpusPath = GetSyntheticTextCorpusPath();
        Directory.Exists(corpusPath).Should().BeTrue();

        var scanOptions = new ScanOptions(
            MaxDepth: 1,
            IncludedExtensions: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".md" }
        );

        // 1. Index corpus
        var indexResult = await _orchestrator.IndexDirectoryAsync(
            corpusPath,
            scanOptions,
            null,
            CancellationToken.None
        );

        indexResult.IndexedCount.Should().BeGreaterThan(0);
        indexResult.FailedCount.Should().Be(0);

        var fileCount = await _indexStore.GetIndexedFileCountAsync(CancellationToken.None);
        var chunkCount = await _indexStore.GetChunkCountAsync(CancellationToken.None);

        fileCount.Should().Be(indexResult.IndexedCount);
        chunkCount.Should().BeGreaterThanOrEqualTo(fileCount);

        // 2. Search via SearchViewModel
        using var searchVm = new SearchViewModel(_indexStore, _normalizer, _orchestrator)
        {
            DebounceDelayMs = 0
        };

        // Query A: Common Arabic term
        await searchVm.ExecuteSearchImmediateAsync("تقرير");
        searchVm.Results.Should().NotBeEmpty();
        searchVm.Results.All(r => r.DisplayPath.StartsWith('\u2066') && r.DisplayPath.EndsWith('\u2069')).Should().BeTrue();
        searchVm.Results.All(r => r.DisplayFileName.StartsWith('\u2066') && r.DisplayFileName.EndsWith('\u2069')).Should().BeTrue();

        // Query B: Diacritic invariance (تَقرِير vs تقرير)
        var resultsDiacritics = searchVm.Results.Count;
        await searchVm.ExecuteSearchImmediateAsync("تَقْرِير");
        searchVm.Results.Count.Should().Be(resultsDiacritics);

        // Query C: Arabic-Indic vs Western digits match
        await searchVm.ExecuteSearchImmediateAsync("٢٠٢٥");
        var countIndic = searchVm.Results.Count;
        await searchVm.ExecuteSearchImmediateAsync("2025");
        var countWestern = searchVm.Results.Count;
        countIndic.Should().Be(countWestern);

        // Query D: Highlighting verification
        var topResult = searchVm.Results.FirstOrDefault(r => r.HasSnippet);
        if (topResult != null)
        {
            topResult.SnippetRuns.Should().NotBeEmpty();
            topResult.SnippetRuns.Any(r => r.IsHighlighted).Should().BeTrue();
        }

        // Query E: Clear search
        searchVm.ClearSearch();
        searchVm.Results.Should().BeEmpty();
        searchVm.TotalCount.Should().Be(0);
    }
}

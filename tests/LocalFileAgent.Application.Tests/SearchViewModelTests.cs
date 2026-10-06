using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Application.Indexing;
using LocalFileAgent.Application.Search;
using LocalFileAgent.Domain.FileSystem;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Domain.Storage;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Text;
using Xunit;

namespace LocalFileAgent.Application.Tests;

public class SearchViewModelTests
{
    private sealed class FakeIndexStore : IIndexStore
    {
        public List<string> ReceivedQueries { get; } = new();
        public List<bool> ReceivedTrigramFlags { get; } = new();
        public IReadOnlyList<string>? LastScopePaths { get; private set; }

        public Func<string, bool, IReadOnlyList<SearchResultItem>>? SearchHandler { get; set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<long> UpsertFileAsync(IndexedFile file, CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public Task<IndexedFile?> GetFileByPathAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<IndexedFile?>(null);
        public Task<IndexedFile?> GetFileByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult<IndexedFile?>(null);
        public Task<IReadOnlyList<long>> InsertChunksAsync(long fileId, IReadOnlyList<IndexedChunk> chunks, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<long>>(Array.Empty<long>());
        public Task DeleteFileAsync(long fileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<SearchResultItem>> GetChunksByIdsAsync(IReadOnlyList<long> chunkIds, IReadOnlyList<string>? scopePaths = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SearchResultItem>>(Array.Empty<SearchResultItem>());

        public Task<IReadOnlyList<SearchResultItem>> SearchFtsAsync(
            string normalizedQuery,
            IReadOnlyList<string>? scopePaths = null,
            int limit = 20,
            bool useTrigram = false,
            CancellationToken cancellationToken = default)
        {
            ReceivedQueries.Add(normalizedQuery);
            ReceivedTrigramFlags.Add(useTrigram);
            LastScopePaths = scopePaths;

            if (SearchHandler != null)
            {
                return Task.FromResult(SearchHandler(normalizedQuery, useTrigram));
            }

            return Task.FromResult<IReadOnlyList<SearchResultItem>>(Array.Empty<SearchResultItem>());
        }

        public Task<long> GetIndexedFileCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(42L);
        public Task<long> GetChunkCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(128L);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeOrchestrator : IIndexOrchestrator
    {
        public bool WasCalled { get; private set; }

        public Task<IndexProgressResult> IndexDirectoryAsync(
            string directoryPath,
            ScanOptions? options = null,
            IProgress<IndexingProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            progress?.Report(new IndexingProgressReport(10, 8, 2, 0, "D:\\test\\doc.txt"));
            return Task.FromResult(new IndexProgressResult(8, 2, 0, TimeSpan.FromSeconds(1.5)));
        }
    }

    [Fact]
    public async Task ExecuteSearchImmediateAsync_NormalizesQueryAndWrapsLtrPaths()
    {
        var fakeStore = new FakeIndexStore();
        var normalizer = new ArabicTextNormalizer();

        fakeStore.SearchHandler = (q, tri) =>
        {
            return new[]
            {
                new SearchResultItem(
                    FilePath: "D:\\corpus\\فواتير\\فاتورة_1.pdf",
                    FileName: "فاتورة_1.pdf",
                    PageNumber: 2,
                    SourceKind: "text_layer",
                    Score: 0.95f,
                    Snippet: "فاتورة مبيعات مسجلة برقم 100"
                )
            };
        };

        using var vm = new SearchViewModel(fakeStore, normalizer)
        {
            DebounceDelayMs = 0
        };

        // Query with tashkeel and Alef-hamza
        await vm.ExecuteSearchImmediateAsync("فَاتُورَةٌ إِيجَار");

        fakeStore.ReceivedQueries.Should().ContainSingle();
        fakeStore.ReceivedQueries[0].Should().Be("فاتورة ايجار");

        vm.Results.Should().HaveCount(1);
        var result = vm.Results[0];
        result.FileName.Should().Be("فاتورة_1.pdf");
        result.DisplayFileName.Should().Be("\u2066فاتورة_1.pdf\u2069");
        result.DisplayPath.Should().Be("\u2066D:\\corpus\\فواتير\\فاتورة_1.pdf\u2069");
        result.PageNumber.Should().Be(2);
        result.SourceKindDisplay.Should().Be("طبقة النص");
        result.FormattedScore.Should().Be("0.95");
        result.SnippetRuns.Should().NotBeEmpty();
        vm.TotalCount.Should().Be(1);
        vm.StatusMessage.Should().Contain("1");
    }

    [Fact]
    public async Task ExecuteSearchImmediateAsync_FallsBackToTrigramWhenNoExactMatches()
    {
        var fakeStore = new FakeIndexStore();
        var normalizer = new ArabicTextNormalizer();

        fakeStore.SearchHandler = (q, tri) =>
        {
            if (!tri)
            {
                // Exact token search returns 0
                return Array.Empty<SearchResultItem>();
            }
            // Trigram substring search returns 1
            return new[]
            {
                new SearchResultItem("D:\\file.txt", "file.txt", 1, "ocr_win", 0.5f, "مقطع تجريبي")
            };
        };

        using var vm = new SearchViewModel(fakeStore, normalizer)
        {
            DebounceDelayMs = 0
        };

        await vm.ExecuteSearchImmediateAsync("تجريب");

        fakeStore.ReceivedTrigramFlags.Should().Contain(false);
        fakeStore.ReceivedTrigramFlags.Should().Contain(true);

        vm.Results.Should().HaveCount(1);
        vm.Results[0].SourceKindDisplay.Should().Be("تعرف ضوئي OCR");
    }

    [Fact]
    public async Task ExecuteSearchImmediateAsync_PassesScopeFilter()
    {
        var fakeStore = new FakeIndexStore();
        var normalizer = new ArabicTextNormalizer();

        using var vm = new SearchViewModel(fakeStore, normalizer)
        {
            DebounceDelayMs = 0,
            SelectedScope = "D:\\finance"
        };

        await vm.ExecuteSearchImmediateAsync("تقرير");

        fakeStore.LastScopePaths.Should().NotBeNull();
        fakeStore.LastScopePaths.Should().Equal("D:\\finance");
    }

    [Fact]
    public async Task OnQueryChanged_WithDebounce_ExecutesAfterDelay()
    {
        var fakeStore = new FakeIndexStore();
        var normalizer = new ArabicTextNormalizer();

        using var vm = new SearchViewModel(fakeStore, normalizer)
        {
            DebounceDelayMs = 50
        };

        fakeStore.SearchHandler = (q, tri) => new[]
        {
            new SearchResultItem("D:\\doc.txt", "doc.txt", 1, "text_layer", 0.8f, "محتوى")
        };

        vm.Query = "محتوى";
        vm.Results.Should().BeEmpty(); // Still debouncing

        await Task.Delay(120);

        vm.Results.Should().HaveCount(1);
    }

    [Fact]
    public async Task ClearSearch_ResetsAllProperties()
    {
        var fakeStore = new FakeIndexStore();
        var normalizer = new ArabicTextNormalizer();

        fakeStore.SearchHandler = (q, tri) => new[]
        {
            new SearchResultItem("D:\\doc.txt", "doc.txt", 1, "text_layer", 0.8f, "محتوى")
        };

        using var vm = new SearchViewModel(fakeStore, normalizer)
        {
            DebounceDelayMs = 0
        };

        await vm.ExecuteSearchImmediateAsync("محتوى");
        vm.Results.Should().HaveCount(1);

        vm.ClearSearch();

        vm.Query.Should().BeEmpty();
        vm.Results.Should().BeEmpty();
        vm.TotalCount.Should().Be(0);
        vm.StatusMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task StartIndexingAsync_ExecutesAndRefreshesStats()
    {
        var fakeStore = new FakeIndexStore();
        var normalizer = new ArabicTextNormalizer();
        var orchestrator = new FakeOrchestrator();

        using var vm = new SearchViewModel(fakeStore, normalizer, orchestrator)
        {
            DebounceDelayMs = 0
        };

        await vm.StartIndexingAsync("D:\\test\\folder");

        orchestrator.WasCalled.Should().BeTrue();
        vm.IsIndexing.Should().BeFalse();
        vm.IndexingMessage.Should().Contain("اكتملت الفهرسة");
        vm.IndexedFilesCount.Should().Be(42);
        vm.IndexedChunksCount.Should().Be(128);
    }

    [Fact]
    public async Task DeleteFromIndexCommand_RemovesItemFromResultsAndDatabase()
    {
        var fakeStore = new FakeIndexStore();
        var normalizer = new ArabicTextNormalizer();

        fakeStore.SearchHandler = (q, tri) => new[]
        {
            new SearchResultItem("D:\\docs\\file1.txt", "file1.txt", 1, "text_layer", 0.9f, "نص أول")
        };

        using var vm = new SearchViewModel(fakeStore, normalizer) { DebounceDelayMs = 0 };
        await vm.ExecuteSearchImmediateAsync("نص");
        vm.Results.Should().HaveCount(1);

        var item = vm.Results[0];
        await vm.DeleteFromIndexCommand.ExecuteAsync(item);

        vm.Results.Should().BeEmpty();
        vm.StatusMessage.Should().Contain("تمت إزالة الملف");
    }

    [Fact]
    public async Task ClearIndexCommand_ClearsAllResultsAndResetsCounts()
    {
        var fakeStore = new FakeIndexStore();
        var normalizer = new ArabicTextNormalizer();

        using var vm = new SearchViewModel(fakeStore, normalizer) { DebounceDelayMs = 0 };
        vm.Results.Add(new SearchResultViewModel(
            new SearchResultItem("D:\\doc.pdf", "doc.pdf", 1, "text_layer", 0.9f, "تجربة"),
            "تجربة",
            normalizer
        ));

        await vm.ClearIndexCommand.ExecuteAsync(null);

        vm.Results.Should().BeEmpty();
        vm.TotalCount.Should().Be(0);
        vm.IndexedFilesCount.Should().Be(0);
        vm.IndexedChunksCount.Should().Be(0);
        vm.StatusMessage.Should().Contain("تم مسح كافة الملفات والمقاطع");
    }

    [Fact]
    public void Keywords_Cards_CanAddRemoveClearAndBuildEffectiveQuery()
    {
        var fakeStore = new FakeIndexStore();
        var normalizer = new ArabicTextNormalizer();

        using var vm = new SearchViewModel(fakeStore, normalizer) { DebounceDelayMs = 0 };

        // Initially empty
        vm.Keywords.Should().BeEmpty();
        vm.HasKeywords.Should().BeFalse();
        vm.HasNoKeywords.Should().BeTrue();
        vm.KeywordsCount.Should().Be(0);
        vm.GetEffectiveQuery().Should().BeEmpty();

        // Add first keyword card
        vm.AddKeyword("+ فاتورة");
        vm.Keywords.Should().ContainSingle().Which.Should().Be("فاتورة");
        vm.HasKeywords.Should().BeTrue();
        vm.HasNoKeywords.Should().BeFalse();
        vm.KeywordsCount.Should().Be(1);
        vm.KeywordsSummary.Should().Contain("1");
        vm.GetEffectiveQuery().Should().Be("فاتورة");

        // Add duplicate keyword (should be ignored)
        vm.AddKeyword("فاتورة");
        vm.Keywords.Should().HaveCount(1);

        // Add multi-delimiter keywords (comma, Arabic comma, semicolon)
        vm.AddKeyword("٢٠٢٥، توريد ; عقد");
        vm.Keywords.Should().Equal("فاتورة", "٢٠٢٥", "توريد", "عقد");
        vm.KeywordsCount.Should().Be(4);
        vm.GetEffectiveQuery().Should().Be("فاتورة ٢٠٢٥ توريد عقد");

        // Set main query
        vm.Query = "شركة النيل";
        vm.GetEffectiveQuery().Should().Be("شركة النيل فاتورة ٢٠٢٥ توريد عقد");

        // Remove a keyword card
        vm.RemoveKeyword("فاتورة");
        vm.Keywords.Should().Equal("٢٠٢٥", "توريد", "عقد");
        vm.GetEffectiveQuery().Should().Be("شركة النيل ٢٠٢٥ توريد عقد");

        // Clear all keywords
        vm.ClearKeywords();
        vm.Keywords.Should().BeEmpty();
        vm.HasKeywords.Should().BeFalse();
        vm.HasNoKeywords.Should().BeTrue();
        vm.KeywordsCount.Should().Be(0);
        vm.GetEffectiveQuery().Should().Be("شركة النيل");
    }
}

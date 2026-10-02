using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalFileAgent.Application.Indexing;
using LocalFileAgent.Domain.FileSystem;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Domain.Storage;
using LocalFileAgent.Domain.Text;

namespace LocalFileAgent.Application.Search;

public sealed partial class SearchViewModel : ObservableObject, IDisposable
{
    private readonly IIndexStore _indexStore;
    private readonly ITextNormalizer _normalizer;
    private readonly IIndexOrchestrator? _orchestrator;
    private readonly IDispatcherService _dispatcher;
    private readonly ISearchService? _searchService;

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _indexingCts;
    private bool _disposed;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _isIndexing;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _indexingMessage = string.Empty;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private long _searchLatencyMs;

    [ObservableProperty]
    private string? _selectedScope;

    [ObservableProperty]
    private bool _useTrigram;

    [ObservableProperty]
    private long _indexedFilesCount;

    [ObservableProperty]
    private long _indexedChunksCount;

    [ObservableProperty]
    private SearchResultViewModel? _selectedResult;

    public int DebounceDelayMs { get; set; } = 250;

    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand ClearSearchCommand { get; }
    public IAsyncRelayCommand<string?> StartIndexingCommand { get; }
    public IRelayCommand CancelIndexingCommand { get; }
    public IAsyncRelayCommand RefreshStatsCommand { get; }

    public SearchViewModel(
        IIndexStore indexStore,
        ITextNormalizer normalizer,
        IIndexOrchestrator? orchestrator = null,
        IDispatcherService? dispatcher = null,
        ISearchService? searchService = null)
    {
        _indexStore = indexStore ?? throw new ArgumentNullException(nameof(indexStore));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _orchestrator = orchestrator;
        _dispatcher = dispatcher ?? new ImmediateDispatcherService();
        _searchService = searchService;

        SearchCommand = new AsyncRelayCommand(() => ExecuteSearchImmediateAsync(Query));
        ClearSearchCommand = new RelayCommand(ClearSearch);
        StartIndexingCommand = new AsyncRelayCommand<string?>(StartIndexingAsync);
        CancelIndexingCommand = new RelayCommand(CancelIndexing);
        RefreshStatsCommand = new AsyncRelayCommand(RefreshStatsAsync);
    }

    partial void OnQueryChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();

        var token = _searchCts.Token;
        _ = TriggerDebouncedSearchAsync(value, token);
    }

    private async Task TriggerDebouncedSearchAsync(string text, CancellationToken cancellationToken)
    {
        if (DebounceDelayMs > 0)
        {
            try
            {
                await Task.Delay(DebounceDelayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        await ExecuteSearchImmediateAsync(text, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteSearchImmediateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _dispatcher.Invoke(() =>
            {
                Results.Clear();
                TotalCount = 0;
                SearchLatencyMs = 0;
                StatusMessage = string.Empty;
                IsSearching = false;
            });
            return;
        }

        _dispatcher.Invoke(() => IsSearching = true);
        var sw = Stopwatch.StartNew();

        try
        {
            var scopes = string.IsNullOrWhiteSpace(SelectedScope) ? null : new[] { SelectedScope };
            IReadOnlyList<SearchResultItem> items;

            if (_searchService != null)
            {
                var searchReq = new SearchRequest(
                    Query: text,
                    ScopePaths: scopes,
                    Limit: 50
                );
                items = await _searchService.SearchAsync(searchReq, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var normResult = _normalizer.Normalize(text, NormalizationProfile.Search);
                var normalizedQuery = string.IsNullOrWhiteSpace(normResult.ProcessedText)
                    ? text.Trim()
                    : normResult.ProcessedText;

                items = await _indexStore.SearchFtsAsync(
                    normalizedQuery,
                    scopePaths: scopes,
                    limit: 50,
                    useTrigram: UseTrigram,
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);

                // Automatic fallback to trigram substring search if 0 exact token matches found
                if (items.Count == 0 && !UseTrigram)
                {
                    var trigramItems = await _indexStore.SearchFtsAsync(
                        normalizedQuery,
                        scopePaths: scopes,
                        limit: 50,
                        useTrigram: true,
                        cancellationToken: cancellationToken
                    ).ConfigureAwait(false);

                    if (trigramItems.Count > 0)
                    {
                        items = trigramItems;
                    }
                }
            }

            sw.Stop();

            _dispatcher.Invoke(() =>
            {
                Results.Clear();
                foreach (var item in items)
                {
                    Results.Add(new SearchResultViewModel(item, text, _normalizer));
                }

                TotalCount = items.Count;
                SearchLatencyMs = sw.ElapsedMilliseconds;
                StatusMessage = items.Count > 0
                    ? string.Format(CultureInfo.InvariantCulture, "تم العثور على {0} نتيجة ({1} ميلي ثانية)", items.Count, sw.ElapsedMilliseconds)
                    : "لم يتم العثور على أي نتائج";
                IsSearching = false;
            });
        }
        catch (OperationCanceledException)
        {
            _dispatcher.Invoke(() => IsSearching = false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _dispatcher.Invoke(() =>
            {
                StatusMessage = string.Format(CultureInfo.InvariantCulture, "خطأ في البحث: {0}", ex.Message);
                IsSearching = false;
            });
        }
    }

    public void ClearSearch()
    {
        _searchCts?.Cancel();
        Query = string.Empty;
        Results.Clear();
        TotalCount = 0;
        SearchLatencyMs = 0;
        StatusMessage = string.Empty;
        IsSearching = false;
    }

    public async Task StartIndexingAsync(string? directoryPath, CancellationToken cancellationToken = default)
    {
        if (_orchestrator == null || string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        _indexingCts?.Cancel();
        _indexingCts?.Dispose();
        _indexingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var token = _indexingCts.Token;

        _dispatcher.Invoke(() =>
        {
            IsIndexing = true;
            IndexingMessage = "بدء فحص وفهرسة الملفات...";
        });

        var progress = new Progress<IndexingProgressReport>(report =>
        {
            _dispatcher.Invoke(() =>
            {
                IndexingMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "تم فحص {0} ملف (مفهرس: {1}، تم تجاوزه: {2}، فشل: {3})",
                    report.TotalDiscovered,
                    report.IndexedCount,
                    report.SkippedCount,
                    report.FailedCount
                );
            });
        });

        try
        {
            var result = await _orchestrator.IndexDirectoryAsync(
                directoryPath,
                new ScanOptions(),
                progress,
                token
            ).ConfigureAwait(false);

            await RefreshStatsAsync(token).ConfigureAwait(false);

            _dispatcher.Invoke(() =>
            {
                IndexingMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "اكتملت الفهرسة: {0} ملف جديد، {1} تم تجاوزه، في {2:N1} ثانية",
                    result.IndexedCount,
                    result.SkippedCount,
                    result.Elapsed.TotalSeconds
                );
                IsIndexing = false;
            });
        }
        catch (OperationCanceledException)
        {
            _dispatcher.Invoke(() =>
            {
                IndexingMessage = "تم إلغاء الفهرسة بواسطة المستخدم";
                IsIndexing = false;
            });
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() =>
            {
                IndexingMessage = string.Format(CultureInfo.InvariantCulture, "فشل أثناء الفهرسة: {0}", ex.Message);
                IsIndexing = false;
            });
        }
    }

    public void CancelIndexing()
    {
        _indexingCts?.Cancel();
    }

    public async Task RefreshStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var files = await _indexStore.GetIndexedFileCountAsync(cancellationToken).ConfigureAwait(false);
            var chunks = await _indexStore.GetChunkCountAsync(cancellationToken).ConfigureAwait(false);

            _dispatcher.Invoke(() =>
            {
                IndexedFilesCount = files;
                IndexedChunksCount = chunks;
            });
        }
        catch
        {
            // Ignore stats lookup errors during refresh
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _indexingCts?.Cancel();
        _indexingCts?.Dispose();
    }
}

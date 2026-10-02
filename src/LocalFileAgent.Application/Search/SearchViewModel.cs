using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalFileAgent.Application.FileSystem;
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
    private readonly LocalFileAgent.Domain.Agent.IAgentService? _agentService;

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

    [ObservableProperty]
    private string _agentAnswerText = string.Empty;

    [ObservableProperty]
    private bool _isAgentMode;

    [ObservableProperty]
    private bool _isAnswerGrounded = true;

    public int DebounceDelayMs { get; set; } = 250;

    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand AskAgentCommand { get; }
    public IRelayCommand ClearSearchCommand { get; }
    public IAsyncRelayCommand<string?> StartIndexingCommand { get; }
    public IRelayCommand CancelIndexingCommand { get; }
    public IAsyncRelayCommand RefreshStatsCommand { get; }
    public IRelayCommand<object?> OpenFileCommand { get; }
    public IRelayCommand<object?> OpenFolderCommand { get; }
    public IRelayCommand CloseAgentAnswerCommand { get; }

    public SearchViewModel(
        IIndexStore indexStore,
        ITextNormalizer normalizer,
        IIndexOrchestrator? orchestrator = null,
        IDispatcherService? dispatcher = null,
        ISearchService? searchService = null,
        LocalFileAgent.Domain.Agent.IAgentService? agentService = null)
    {
        _indexStore = indexStore ?? throw new ArgumentNullException(nameof(indexStore));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _orchestrator = orchestrator;
        _dispatcher = dispatcher ?? new ImmediateDispatcherService();
        _searchService = searchService;
        _agentService = agentService;

        SearchCommand = new AsyncRelayCommand(() => ExecuteSearchImmediateAsync(Query));
        AskAgentCommand = new AsyncRelayCommand(AskAgentAsync);
        ClearSearchCommand = new RelayCommand(ClearSearch);
        StartIndexingCommand = new AsyncRelayCommand<string?>(StartIndexingAsync);
        CancelIndexingCommand = new RelayCommand(CancelIndexing);
        RefreshStatsCommand = new AsyncRelayCommand(RefreshStatsAsync);

        OpenFileCommand = new RelayCommand<object?>(param =>
        {
            var path = param switch
            {
                SearchResultViewModel vm => vm.FilePath,
                string p => p,
                _ => SelectedResult?.FilePath
            };
            if (!string.IsNullOrWhiteSpace(path))
            {
                SafeFileLauncher.Instance.OpenFile(path);
            }
        });

        OpenFolderCommand = new RelayCommand<object?>(param =>
        {
            var path = param switch
            {
                SearchResultViewModel vm => vm.FilePath,
                string p => p,
                _ => SelectedResult?.FilePath
            };
            if (!string.IsNullOrWhiteSpace(path))
            {
                SafeFileLauncher.Instance.OpenContainingFolder(path);
            }
        });

        CloseAgentAnswerCommand = new RelayCommand(() =>
        {
            IsAgentMode = false;
            AgentAnswerText = string.Empty;
        });
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
        IsAgentMode = false;
        AgentAnswerText = string.Empty;
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

    private async Task AskAgentAsync()
    {
        if (string.IsNullOrWhiteSpace(Query) || _agentService == null)
        {
            return;
        }

        IsSearching = true;
        IsAgentMode = true;
        StatusMessage = "جارٍ تحليل السؤال والبحث وصياغة الإجابة الموثقة...";
        AgentAnswerText = string.Empty;

        try
        {
            var scopePaths = string.IsNullOrWhiteSpace(SelectedScope) ? null : new[] { SelectedScope };
            var answer = await _agentService.AskAsync(Query, scopePaths).ConfigureAwait(false);

            _dispatcher.Invoke(() =>
            {
                Results.Clear();
                foreach (var h in answer.Hits)
                {
                    Results.Add(new SearchResultViewModel(h, Query, _normalizer));
                }
                TotalCount = Results.Count;
                AgentAnswerText = answer.ResponseText;
                IsAnswerGrounded = answer.IsGrounded;
                SearchLatencyMs = (long)answer.Elapsed.TotalMilliseconds;
                StatusMessage = answer.IsGrounded
                    ? string.Format(CultureInfo.InvariantCulture, "تمت صياغة إجابة موثقة بالاعتماد على {0} نتائج ({1} مللي ثانية)", TotalCount, SearchLatencyMs)
                    : string.Format(CultureInfo.InvariantCulture, "تمت صياغة إجابة مع تنبيه توثيق ({0} مللي ثانية)", SearchLatencyMs);
            });
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() =>
            {
                StatusMessage = string.Format(CultureInfo.InvariantCulture, "خطأ أثناء المعالجة: {0}", ex.Message);
            });
        }
        finally
        {
            IsSearching = false;
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

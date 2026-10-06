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
    private readonly IVectorIndex? _vectorIndex;
    private readonly IVisualVectorIndex? _visualVectorIndex;

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

    [ObservableProperty]
    private string _currentDirectoryPath = @"d:\wordo\corpus\private\";

    public int DebounceDelayMs { get; set; } = 250;

    public ObservableCollection<SearchResultViewModel> Results { get; } = [];
    public ObservableCollection<string> Keywords { get; } = [];

    private static readonly char[] KeywordDelimiters = [ '،', ',', ';', '\r', '\n' ];

    public bool HasKeywords => Keywords.Count > 0;
    public bool HasNoKeywords => Keywords.Count == 0;
    public int KeywordsCount => Keywords.Count;
    public string KeywordsSummary => string.Format(CultureInfo.InvariantCulture, "[ {0} بطاقات نشطة ]", Keywords.Count);

    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand AskAgentCommand { get; }
    public IRelayCommand ClearSearchCommand { get; }
    public IRelayCommand<string?> AddKeywordCommand { get; }
    public IRelayCommand<string?> RemoveKeywordCommand { get; }
    public IRelayCommand ClearKeywordsCommand { get; }
    public IAsyncRelayCommand<string?> StartIndexingCommand { get; }
    public IRelayCommand CancelIndexingCommand { get; }
    public IAsyncRelayCommand RefreshStatsCommand { get; }
    public IAsyncRelayCommand UpdateArchiveCommand { get; }
    public IRelayCommand<object?> OpenFileCommand { get; }
    public IRelayCommand<object?> OpenFolderCommand { get; }
    public IRelayCommand CloseAgentAnswerCommand { get; }
    public IAsyncRelayCommand<SearchResultViewModel?> DeleteFromIndexCommand { get; }
    public IAsyncRelayCommand ClearIndexCommand { get; }

    public SearchViewModel(
        IIndexStore indexStore,
        ITextNormalizer normalizer,
        IIndexOrchestrator? orchestrator = null,
        IDispatcherService? dispatcher = null,
        ISearchService? searchService = null,
        LocalFileAgent.Domain.Agent.IAgentService? agentService = null,
        IVectorIndex? vectorIndex = null,
        IVisualVectorIndex? visualVectorIndex = null)
    {
        _indexStore = indexStore ?? throw new ArgumentNullException(nameof(indexStore));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _orchestrator = orchestrator;
        _dispatcher = dispatcher ?? new ImmediateDispatcherService();
        _searchService = searchService;
        _agentService = agentService;
        _vectorIndex = vectorIndex;
        _visualVectorIndex = visualVectorIndex;

        SearchCommand = new AsyncRelayCommand(() => ExecuteSearchImmediateAsync(GetEffectiveQuery()));
        AskAgentCommand = new AsyncRelayCommand(AskAgentAsync);
        ClearSearchCommand = new RelayCommand(ClearSearch);
        AddKeywordCommand = new RelayCommand<string?>(AddKeyword);
        RemoveKeywordCommand = new RelayCommand<string?>(RemoveKeyword);
        ClearKeywordsCommand = new RelayCommand(ClearKeywords);

        Keywords.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasKeywords));
            OnPropertyChanged(nameof(HasNoKeywords));
            OnPropertyChanged(nameof(KeywordsCount));
            OnPropertyChanged(nameof(KeywordsSummary));
        };
        StartIndexingCommand = new AsyncRelayCommand<string?>(StartIndexingAsync);
        CancelIndexingCommand = new RelayCommand(CancelIndexing);
        RefreshStatsCommand = new AsyncRelayCommand(RefreshStatsAsync);
        UpdateArchiveCommand = new AsyncRelayCommand(UpdateArchiveAsync);

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

        DeleteFromIndexCommand = new AsyncRelayCommand<SearchResultViewModel?>(DeleteFromIndexAsync);
        ClearIndexCommand = new AsyncRelayCommand(ClearIndexAsync);
    }

    private async Task DeleteFromIndexAsync(SearchResultViewModel? item)
    {
        if (item == null) return;

        try
        {
            var file = await _indexStore.GetFileByPathAsync(item.FilePath).ConfigureAwait(false);
            if (file != null)
            {
                await _indexStore.DeleteFileAsync(file.Id).ConfigureAwait(false);

                if (_visualVectorIndex != null)
                {
                    await _visualVectorIndex.DeleteAsync(new[] { file.Id }).ConfigureAwait(false);
                }
            }

            _dispatcher.Invoke(() =>
            {
                Results.Remove(item);
                TotalCount = Results.Count;
                StatusMessage = string.Format(CultureInfo.InvariantCulture, "تمت إزالة الملف '{0}' من فهرس التطبيق بنجاح", item.FileName);
            });

            await RefreshStatsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() =>
            {
                StatusMessage = string.Format(CultureInfo.InvariantCulture, "خطأ أثناء إزالة الملف من الفهرس: {0}", ex.Message);
            });
        }
    }

    private async Task ClearIndexAsync()
    {
        try
        {
            await _indexStore.ClearAllAsync().ConfigureAwait(false);

            if (_vectorIndex != null)
            {
                await _vectorIndex.ClearAllAsync().ConfigureAwait(false);
            }

            if (_visualVectorIndex != null)
            {
                await _visualVectorIndex.ClearAllAsync().ConfigureAwait(false);
            }

            _dispatcher.Invoke(() =>
            {
                Results.Clear();
                TotalCount = 0;
                IndexedFilesCount = 0;
                IndexedChunksCount = 0;
                IsAgentMode = false;
                AgentAnswerText = string.Empty;
                StatusMessage = "تم مسح كافة الملفات والمقاطع من فهرس التطبيق بنجاح";
            });
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() =>
            {
                StatusMessage = string.Format(CultureInfo.InvariantCulture, "خطأ أثناء مسح الفهرس: {0}", ex.Message);
            });
        }
    }

    partial void OnQueryChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();

        var token = _searchCts.Token;
        _ = TriggerDebouncedSearchAsync(GetEffectiveQuery(), token);
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

                // Automatic fallback to trigram substring search if 0 exact token matches found and query is >= 3 chars
                if (items.Count == 0 && !UseTrigram && normalizedQuery.Trim().Length >= 3)
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
        Keywords.Clear();
        Results.Clear();
        TotalCount = 0;
        SearchLatencyMs = 0;
        StatusMessage = string.Empty;
        IsSearching = false;
        IsAgentMode = false;
        AgentAnswerText = string.Empty;
    }

    public void AddKeyword(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return;

        var tokens = keyword.Split(KeywordDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool anyAdded = false;

        foreach (var rawToken in tokens)
        {
            var clean = rawToken.TrimStart('+', ' ').Trim();
            if (string.IsNullOrWhiteSpace(clean)) continue;

            if (!Keywords.Any(k => string.Equals(k, clean, StringComparison.OrdinalIgnoreCase)))
            {
                Keywords.Add(clean);
                anyAdded = true;
            }
        }

        if (anyAdded)
        {
            _ = ExecuteSearchImmediateAsync(GetEffectiveQuery());
        }
    }

    public void RemoveKeyword(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return;
        var clean = keyword.TrimStart('+', ' ').Trim();
        var existing = Keywords.FirstOrDefault(k => string.Equals(k, clean, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            Keywords.Remove(existing);
            _ = ExecuteSearchImmediateAsync(GetEffectiveQuery());
        }
    }

    public void ClearKeywords()
    {
        if (Keywords.Count > 0)
        {
            Keywords.Clear();
            _ = ExecuteSearchImmediateAsync(GetEffectiveQuery());
        }
    }

    public string GetEffectiveQuery()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Query))
        {
            parts.Add(Query.Trim());
        }
        foreach (var kw in Keywords)
        {
            if (!string.IsNullOrWhiteSpace(kw))
            {
                parts.Add(kw.Trim());
            }
        }
        return string.Join(" ", parts);
    }

    public async Task StartIndexingAsync(string? directoryPath, CancellationToken cancellationToken = default)
    {
        if (_orchestrator == null || string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        CurrentDirectoryPath = directoryPath;

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

    public async Task UpdateArchiveAsync()
    {
        var target = !string.IsNullOrWhiteSpace(CurrentDirectoryPath) && Directory.Exists(CurrentDirectoryPath)
            ? CurrentDirectoryPath
            : (Directory.Exists(@"d:\wordo\corpus\private\") ? @"d:\wordo\corpus\private\" : null);

        if (!string.IsNullOrWhiteSpace(target))
        {
            await StartIndexingAsync(target).ConfigureAwait(false);
        }
        else
        {
            _dispatcher.Invoke(() =>
            {
                StatusMessage = "يرجى تحديد مجلد للأرشفة أولاً باستخدام زر [أرشفة مجلد جديد...]";
            });
        }
    }

    private async Task AskAgentAsync()
    {
        var effective = GetEffectiveQuery();
        if (string.IsNullOrWhiteSpace(effective) || _agentService == null)
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
            var answer = await _agentService.AskAsync(effective, scopePaths).ConfigureAwait(false);

            _dispatcher.Invoke(() =>
            {
                Results.Clear();
                foreach (var h in answer.Hits)
                {
                    Results.Add(new SearchResultViewModel(h, effective, _normalizer));
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

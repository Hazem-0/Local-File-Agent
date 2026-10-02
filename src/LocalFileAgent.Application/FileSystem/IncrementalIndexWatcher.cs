using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LocalFileAgent.Domain.Agent;

namespace LocalFileAgent.Application.FileSystem;

public sealed class IncrementalIndexWatcher : IFileWatcherService
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly int _debounceMs;
    private bool _isDisposed;

    public event Func<FileChangeEvent, Task>? FileChanged;

    public bool IsWatching
    {
        get
        {
            lock (_lock)
            {
                return _watchers.Count > 0;
            }
        }
    }

    public IReadOnlyList<string> WatchedPaths
    {
        get
        {
            lock (_lock)
            {
                var paths = new List<string>(_watchers.Count);
                foreach (var w in _watchers)
                {
                    paths.Add(w.Path);
                }
                return paths;
            }
        }
    }

    public IncrementalIndexWatcher(int debounceMs = 500)
    {
        _debounceMs = debounceMs;
    }

    public void StartWatching(IEnumerable<string> directoryPaths)
    {
        ArgumentNullException.ThrowIfNull(directoryPaths);

        lock (_lock)
        {
            StopWatching();

            foreach (var dir in directoryPaths)
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                try
                {
                    var watcher = new FileSystemWatcher(dir)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                    };

                    watcher.Created += OnCreated;
                    watcher.Changed += OnChanged;
                    watcher.Deleted += OnDeleted;
                    watcher.Renamed += OnRenamed;
                    watcher.EnableRaisingEvents = true;

                    _watchers.Add(watcher);
                }
                catch
                {
                    // Non-fatal if specific directory fails (e.g. unauthorized)
                }
            }
        }
    }

    public void StopWatching()
    {
        lock (_lock)
        {
            foreach (var w in _watchers)
            {
                try
                {
                    w.EnableRaisingEvents = false;
                    w.Dispose();
                }
                catch { }
            }
            _watchers.Clear();

            foreach (var kvp in _debounceTokens)
            {
                try
                {
                    kvp.Value.Cancel();
                    kvp.Value.Dispose();
                }
                catch { }
            }
            _debounceTokens.Clear();
        }
    }

    private void OnCreated(object sender, FileSystemEventArgs e) => ScheduleDebouncedEvent(FileChangeKind.Created, e.FullPath);
    private void OnChanged(object sender, FileSystemEventArgs e) => ScheduleDebouncedEvent(FileChangeKind.Changed, e.FullPath);
    private void OnDeleted(object sender, FileSystemEventArgs e) => ScheduleDebouncedEvent(FileChangeKind.Deleted, e.FullPath);
    private void OnRenamed(object sender, RenamedEventArgs e) => ScheduleDebouncedEvent(FileChangeKind.Renamed, e.FullPath, e.OldFullPath);

    private void ScheduleDebouncedEvent(FileChangeKind kind, string fullPath, string? oldFullPath = null)
    {
        if (_isDisposed || string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        // Cancel previous pending event for this file path
        if (_debounceTokens.TryGetValue(fullPath, out var oldCts))
        {
            try
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }
            catch { }
        }

        var newCts = new CancellationTokenSource();
        _debounceTokens[fullPath] = newCts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounceMs, newCts.Token).ConfigureAwait(false);

                if (!newCts.Token.IsCancellationRequested)
                {
                    _debounceTokens.TryRemove(fullPath, out _);
                    var handler = FileChanged;
                    if (handler != null)
                    {
                        await handler(new FileChangeEvent(kind, fullPath, oldFullPath)).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Debounced by newer event
            }
            finally
            {
                newCts.Dispose();
            }
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopWatching();
        GC.SuppressFinalize(this);
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Application.FileSystem;
using Xunit;

namespace LocalFileAgent.Application.Tests;

public class IncrementalIndexWatcherTests : IDisposable
{
    private readonly string _tempDir;

    public IncrementalIndexWatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lfa_watcher_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void StartAndStopWatching_UpdatesStateCorrectly()
    {
        using var watcher = new IncrementalIndexWatcher(debounceMs: 100);

        watcher.IsWatching.Should().BeFalse();
        watcher.WatchedPaths.Should().BeEmpty();

        watcher.StartWatching(new[] { _tempDir });

        watcher.IsWatching.Should().BeTrue();
        watcher.WatchedPaths.Should().Contain(_tempDir);

        watcher.StopWatching();

        watcher.IsWatching.Should().BeFalse();
        watcher.WatchedPaths.Should().BeEmpty();
    }

    [Fact]
    public void NonExistentDirectory_IsIgnoredSafely()
    {
        using var watcher = new IncrementalIndexWatcher(debounceMs: 100);
        var fakeDir = Path.Combine(_tempDir, "does_not_exist_9876");

        watcher.StartWatching(new[] { fakeDir });

        watcher.IsWatching.Should().BeFalse();
        watcher.WatchedPaths.Should().BeEmpty();
    }
}

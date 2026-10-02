using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Application.FileSystem;
using LocalFileAgent.Domain.FileSystem;
using Xunit;

namespace LocalFileAgent.Application.Tests;

public class FileScannerTests
{
    private static string GetSyntheticCorpusPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "corpus", "synthetic");
    }

    [Fact]
    public async Task ScanAsync_DiscoversSyntheticCorpusFiles()
    {
        var corpusPath = GetSyntheticCorpusPath();
        Directory.Exists(corpusPath).Should().BeTrue();

        var scanner = new FileScanner();
        var options = new ScanOptions(MaxDepth: 5);

        var discovered = new List<DiscoveredFile>();
        await foreach (var file in scanner.ScanAsync(corpusPath, options, null, CancellationToken.None))
        {
            discovered.Add(file);
        }

        discovered.Should().HaveCountGreaterThanOrEqualTo(200);
        discovered.Select(f => f.Extension).Distinct()
            .Should().Contain(new[] { ".txt", ".md", ".docx", ".csv", ".png" });

        foreach (var file in discovered)
        {
            file.SizeBytes.Should().BeGreaterThan(0);
            file.Path.Should().StartWith(corpusPath);
            File.Exists(file.Path).Should().BeTrue();
        }
    }

    [Fact]
    public async Task ScanAsync_FiltersByExtension()
    {
        var corpusPath = GetSyntheticCorpusPath();
        var scanner = new FileScanner();
        var options = new ScanOptions(
            MaxDepth: 5,
            IncludedExtensions: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".docx" }
        );

        var discovered = new List<DiscoveredFile>();
        await foreach (var file in scanner.ScanAsync(corpusPath, options, null, CancellationToken.None))
        {
            discovered.Add(file);
        }

        discovered.Should().NotBeEmpty();
        discovered.Should().OnlyContain(f => f.Extension == ".docx");
    }

    [Fact]
    public async Task ScanAsync_ReportsProgress()
    {
        var corpusPath = GetSyntheticCorpusPath();
        var scanner = new FileScanner();
        var options = new ScanOptions(MaxDepth: 2);

        var reports = new List<ScanProgressReport>();
        var progress = new Progress<ScanProgressReport>(r => reports.Add(r));

        await foreach (var _ in scanner.ScanAsync(corpusPath, options, progress, CancellationToken.None))
        {
            // iterate
        }

        reports.Should().NotBeEmpty();
        reports.Last().FilesDiscovered.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ScanAsync_RespectsMaxDepth()
    {
        var corpusPath = GetSyntheticCorpusPath();
        var scanner = new FileScanner();
        var options = new ScanOptions(MaxDepth: 0);

        var discovered = new List<DiscoveredFile>();
        await foreach (var file in scanner.ScanAsync(corpusPath, options, null, CancellationToken.None))
        {
            discovered.Add(file);
        }

        // At depth 0, only files directly in root are discovered
        discovered.Should().NotBeEmpty();
        foreach (var file in discovered)
        {
            Path.GetDirectoryName(file.Path)!.TrimEnd(Path.DirectorySeparatorChar)
                .Should().Be(corpusPath.TrimEnd(Path.DirectorySeparatorChar));
        }
    }

    [Fact]
    public async Task ScanAsync_HandlesCancellationGracefully()
    {
        var corpusPath = GetSyntheticCorpusPath();
        var scanner = new FileScanner();
        var options = new ScanOptions(MaxDepth: 5);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        var act = async () =>
        {
            await foreach (var _ in scanner.ScanAsync(corpusPath, options, null, cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

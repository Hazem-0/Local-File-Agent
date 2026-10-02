using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Domain.Worker;
using LocalFileAgent.Infrastructure.Worker;
using Xunit;

namespace LocalFileAgent.Infrastructure.Tests;

public class WorkerClientTests
{
    private static string GetSyntheticCorpusPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "corpus", "synthetic");
    }

    [Fact]
    public async Task WorkerClient_StartAndPing_CommunicatesSuccessfully()
    {
        await using var client = new WorkerClient();
        await client.StartAsync(CancellationToken.None);

        var isAlive = await client.PingAsync(CancellationToken.None);
        isAlive.Should().BeTrue();
    }

    [Fact]
    public async Task WorkerClient_ParseFileAsync_ExtractsArabicTextFile()
    {
        var corpus = GetSyntheticCorpusPath();
        var txtFiles = Directory.GetFiles(corpus, "*.txt");
        txtFiles.Should().NotBeEmpty();

        var testFile = txtFiles[0];

        await using var client = new WorkerClient();
        await client.StartAsync(CancellationToken.None);

        var response = await client.ParseFileAsync(new WorkerParseRequest(
            RequestId: "req-1",
            FilePath: testFile,
            Extension: ".txt"
        ), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Pages.Should().HaveCount(1);
        response.Pages[0].Text.Should().NotBeNullOrWhiteSpace();
        response.Pages[0].SourceKind.Should().Be("text_layer");
        response.DurationMs.Should().BeGreaterThan(0);
    }
}

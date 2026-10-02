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

    [Fact]
    public async Task WorkerClient_ParseFileAsync_ExtractsArabicDocxFile()
    {
        var corpus = GetSyntheticCorpusPath();
        var docxFiles = Directory.GetFiles(corpus, "*.docx");
        docxFiles.Should().NotBeEmpty();

        var testFile = docxFiles[0];

        await using var client = new WorkerClient();
        await client.StartAsync(CancellationToken.None);

        var response = await client.ParseFileAsync(new WorkerParseRequest(
            RequestId: "req-docx-1",
            FilePath: testFile,
            Extension: ".docx"
        ), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Pages.Should().NotBeEmpty();
        response.Pages[0].Text.Should().Contain("عقد");
        response.Pages[0].SourceKind.Should().Be("text_layer");
        response.Pages[0].Confidence.Should().Be(1.0f);
    }

    [Fact]
    public async Task WorkerClient_ParseFileAsync_ExtractsArabicImageViaOcr()
    {
        var corpus = GetSyntheticCorpusPath();
        var pngFiles = Directory.GetFiles(corpus, "*.png");
        pngFiles.Should().NotBeEmpty();

        var testFile = pngFiles[0];

        await using var client = new WorkerClient();
        await client.StartAsync(CancellationToken.None);

        var response = await client.ParseFileAsync(new WorkerParseRequest(
            RequestId: "req-png-1",
            FilePath: testFile,
            Extension: ".png"
        ), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Pages.Should().NotBeEmpty();
        response.Pages[0].SourceKind.Should().Be("ocr_win");
        response.Pages[0].Confidence.Should().BeGreaterThan(0.8f);
    }

    [Fact]
    public async Task WorkerClient_ParseFileAsync_ExtractsDigitalPdfFile()
    {
        var corpus = GetSyntheticCorpusPath();
        var digitalPdfs = Directory.GetFiles(corpus, "عقد_رسمي_رقمي_*.pdf");
        digitalPdfs.Should().NotBeEmpty();

        var testFile = digitalPdfs[0];

        await using var client = new WorkerClient();
        await client.StartAsync(CancellationToken.None);

        var response = await client.ParseFileAsync(new WorkerParseRequest(
            RequestId: "req-pdf-digital",
            FilePath: testFile,
            Extension: ".pdf"
        ), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Pages.Should().NotBeEmpty();
        response.Pages[0].Text.Should().NotBeNullOrWhiteSpace();
        response.Pages[0].SourceKind.Should().Be("text_layer");
    }

    [Fact]
    public async Task WorkerClient_ParseFileAsync_RoutesScannedPdfToOcr()
    {
        var corpus = GetSyntheticCorpusPath();
        var scannedPdfs = Directory.GetFiles(corpus, "فاتورة_ممسوحة_ضوئيا_*.pdf");
        scannedPdfs.Should().NotBeEmpty();

        var testFile = scannedPdfs[0];

        await using var client = new WorkerClient();
        await client.StartAsync(CancellationToken.None);

        var response = await client.ParseFileAsync(new WorkerParseRequest(
            RequestId: "req-pdf-scanned",
            FilePath: testFile,
            Extension: ".pdf"
        ), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Pages.Should().NotBeEmpty();
        response.Pages[0].SourceKind.Should().Be("ocr_win");
        response.Pages[0].Confidence.Should().BeGreaterThan(0.8f);
        response.Pages[0].Text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task WorkerClient_ParseFileAsync_DetectsBrokenPdfAndEscalatesToOcr()
    {
        var corpus = GetSyntheticCorpusPath();
        var brokenPdfs = Directory.GetFiles(corpus, "مستند_معطوب_طبقة_*.pdf");
        brokenPdfs.Should().NotBeEmpty();

        var testFile = brokenPdfs[0];

        await using var client = new WorkerClient();
        await client.StartAsync(CancellationToken.None);

        var response = await client.ParseFileAsync(new WorkerParseRequest(
            RequestId: "req-pdf-broken",
            FilePath: testFile,
            Extension: ".pdf"
        ), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Pages.Should().NotBeEmpty();
        // Since digital layer failed quality gate, SourceKind is either "ocr_win" or "text_layer_degraded"
        response.Pages[0].SourceKind.Should().Match(s => s == "ocr_win" || s == "text_layer_degraded");
    }
}

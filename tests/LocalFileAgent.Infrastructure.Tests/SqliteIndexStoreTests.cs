using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Domain.Storage;
using LocalFileAgent.Infrastructure.Storage;
using Xunit;

namespace LocalFileAgent.Infrastructure.Tests;

public class SqliteIndexStoreTests : IAsyncLifetime, IDisposable
{
    private readonly string _tempDbPath;
    private SqliteIndexStore _store = null!;

    public SqliteIndexStoreTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"lfa_test_{Guid.NewGuid():N}.db");
    }

    public async Task InitializeAsync()
    {
        _store = new SqliteIndexStore(_tempDbPath);
        await _store.InitializeAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_store != null)
        {
            await _store.DisposeAsync();
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
        if (disposing && _store != null)
        {
            _store.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesTablesSuccessfully()
    {
        var fileCount = await _store.GetIndexedFileCountAsync(CancellationToken.None);
        var chunkCount = await _store.GetChunkCountAsync(CancellationToken.None);

        fileCount.Should().Be(0);
        chunkCount.Should().Be(0);
    }

    [Fact]
    public async Task UpsertFileAsync_InsertsAndRetrievesFile()
    {
        var now = DateTimeOffset.UtcNow;
        var file = new IndexedFile(
            Id: 0,
            Path: "D:\\test\\فاتورة_101.pdf",
            Name: "فاتورة_101.pdf",
            Extension: ".pdf",
            SizeBytes: 10240,
            CreatedAt: now,
            ModifiedAt: now,
            IndexedAt: now,
            ETag: "etag123",
            Status: "indexed"
        );

        var id = await _store.UpsertFileAsync(file, CancellationToken.None);
        id.Should().BeGreaterThan(0);

        var retrieved = await _store.GetFileByPathAsync(file.Path, CancellationToken.None);
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(id);
        retrieved.Name.Should().Be("فاتورة_101.pdf");
        retrieved.Status.Should().Be("indexed");
    }

    [Fact]
    public async Task InsertChunksAsync_SyncsToFts5AndReturnsSearchResults()
    {
        var now = DateTimeOffset.UtcNow;
        var file = new IndexedFile(
            Id: 0,
            Path: "D:\\test\\عقد_إيجار.docx",
            Name: "عقد_إيجار.docx",
            Extension: ".docx",
            SizeBytes: 20480,
            CreatedAt: now,
            ModifiedAt: now,
            IndexedAt: now,
            ETag: "etag456",
            Status: "indexed"
        );

        var fileId = await _store.UpsertFileAsync(file, CancellationToken.None);

        var chunks = new[]
        {
            new IndexedChunk(
                Id: 0,
                FileId: fileId,
                PageNumber: 1,
                Ordinal: 0,
                TextRaw: "عقد إيجار شقة سكنية بمدينة نصر بين الطرفين",
                TextNormalized: "عقد ايجار شقة سكنية بمدينة نصر بين الطرفين",
                SourceKind: "text_layer",
                Confidence: 1.0f
            ),
            new IndexedChunk(
                Id: 0,
                FileId: fileId,
                PageNumber: 2,
                Ordinal: 1,
                TextRaw: "قيمة الإيجار الشهري خمسة آلاف جنيه مصري",
                TextNormalized: "قيمة ايجار شهري خمسة الاف جنيه مصري",
                SourceKind: "text_layer",
                Confidence: 1.0f
            )
        };

        await _store.InsertChunksAsync(fileId, chunks, CancellationToken.None);

        var chunkCount = await _store.GetChunkCountAsync(CancellationToken.None);
        chunkCount.Should().Be(2);

        // Search via FTS5 unicode61
        var results = await _store.SearchFtsAsync("ايجار", limit: 10, useTrigram: false, cancellationToken: CancellationToken.None);
        results.Should().HaveCount(2);
        results[0].FilePath.Should().Be(file.Path);
        results[0].FileName.Should().Be("عقد_إيجار.docx");
        results[0].Score.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SearchFtsAsync_EnforcesScopeContainment()
    {
        var now = DateTimeOffset.UtcNow;
        var fileInScope = new IndexedFile(
            Id: 0,
            Path: "D:\\finance\\فاتورة_شهر_مايو.txt",
            Name: "فاتورة_شهر_مايو.txt",
            Extension: ".txt",
            SizeBytes: 1000,
            CreatedAt: now,
            ModifiedAt: now,
            IndexedAt: now,
            ETag: "etag1",
            Status: "indexed"
        );

        var fileOutOfScope = new IndexedFile(
            Id: 0,
            Path: "D:\\hr\\فاتورة_أدوات_مكتبية.txt",
            Name: "فاتورة_أدوات_مكتبية.txt",
            Extension: ".txt",
            SizeBytes: 1000,
            CreatedAt: now,
            ModifiedAt: now,
            IndexedAt: now,
            ETag: "etag2",
            Status: "indexed"
        );

        var id1 = await _store.UpsertFileAsync(fileInScope, CancellationToken.None);
        var id2 = await _store.UpsertFileAsync(fileOutOfScope, CancellationToken.None);

        await _store.InsertChunksAsync(id1, new[]
        {
            new IndexedChunk(0, id1, 1, 0, "فاتورة مالية للمورد", "فاتورة مالية للمورد", "text_layer")
        }, CancellationToken.None);

        await _store.InsertChunksAsync(id2, new[]
        {
            new IndexedChunk(0, id2, 1, 0, "فاتورة أدوات مكتبية للموظفين", "فاتورة ادوات مكتبية للموظفين", "text_layer")
        }, CancellationToken.None);

        // Scope strictly limited to D:\finance
        var results = await _store.SearchFtsAsync(
            normalizedQuery: "فاتورة",
            scopePaths: new[] { "D:\\finance" },
            limit: 10,
            useTrigram: false,
            cancellationToken: CancellationToken.None
        );

        results.Should().HaveCount(1);
        results[0].FilePath.Should().StartWith("D:\\finance");
    }

    [Fact]
    public async Task SearchFtsAsync_TrigramMatchesSubstrings()
    {
        var now = DateTimeOffset.UtcNow;
        var file = new IndexedFile(
            Id: 0,
            Path: "D:\\docs\\ocr_scan.png",
            Name: "ocr_scan.png",
            Extension: ".png",
            SizeBytes: 5000,
            CreatedAt: now,
            ModifiedAt: now,
            IndexedAt: now,
            ETag: "etag_tri",
            Status: "indexed"
        );

        var fileId = await _store.UpsertFileAsync(file, CancellationToken.None);
        await _store.InsertChunksAsync(fileId, new[]
        {
            new IndexedChunk(0, fileId, 1, 0, "كود المستند: MARKER_INVOICE_999", "كود المستند MARKER_INVOICE_999", "ocr_win")
        }, CancellationToken.None);

        // Search substring via trigram
        var results = await _store.SearchFtsAsync(
            normalizedQuery: "INVOICE",
            limit: 10,
            useTrigram: true,
            cancellationToken: CancellationToken.None
        );

        results.Should().HaveCount(1);
        results[0].Snippet.Should().Contain("MARKER_INVOICE_999");
    }

    [Fact]
    public async Task DeleteFileAsync_CascadesDeletionToChunksAndFts()
    {
        var now = DateTimeOffset.UtcNow;
        var file = new IndexedFile(
            Id: 0,
            Path: "D:\\docs\\temp.txt",
            Name: "temp.txt",
            Extension: ".txt",
            SizeBytes: 500,
            CreatedAt: now,
            ModifiedAt: now,
            IndexedAt: now,
            ETag: "etag_del",
            Status: "indexed"
        );

        var fileId = await _store.UpsertFileAsync(file, CancellationToken.None);
        await _store.InsertChunksAsync(fileId, new[]
        {
            new IndexedChunk(0, fileId, 1, 0, "نص مؤقت للحذف", "نص مؤقت للحذف", "text_layer")
        }, CancellationToken.None);

        (await _store.GetChunkCountAsync(CancellationToken.None)).Should().Be(1);

        // Delete file
        await _store.DeleteFileAsync(fileId, CancellationToken.None);

        (await _store.GetChunkCountAsync(CancellationToken.None)).Should().Be(0);

        var searchResults = await _store.SearchFtsAsync("مؤقت", limit: 10, cancellationToken: CancellationToken.None);
        searchResults.Should().BeEmpty();
    }
}

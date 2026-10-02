using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Infrastructure.Storage;
using Xunit;

namespace LocalFileAgent.Infrastructure.Tests;

public class SqliteVectorIndexTests : IDisposable
{
    private readonly string _tempDbDir;
    private readonly string _dbPath;

    public SqliteVectorIndexTests()
    {
        _tempDbDir = Path.Combine(Path.GetTempPath(), "LocalFileAgent_VectorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDbDir);
        _dbPath = Path.Combine(_tempDbDir, "vectors.db");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDbDir))
        {
            try
            {
                Directory.Delete(_tempDbDir, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task InitializeAsync_CreatesTableAndLoadsZeroVectors()
    {
        await using var index = new SqliteVectorIndex(_dbPath);
        await index.InitializeAsync();

        var count = await index.GetCountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task AddAsync_StoresVectorsAndPerformsCosineSearch()
    {
        await using var index = new SqliteVectorIndex(_dbPath);
        await index.InitializeAsync();

        // 3 orthogonal unit vectors in 3D
        var v1 = new float[] { 1.0f, 0.0f, 0.0f }; // id 1
        var v2 = new float[] { 0.0f, 1.0f, 0.0f }; // id 2
        var v3 = new float[] { 0.7071f, 0.7071f, 0.0f }; // id 3 (45 degrees between v1 and v2)

        await index.AddAsync(new (long, float[])[]
        {
            (1L, v1),
            (2L, v2),
            (3L, v3)
        });

        var count = await index.GetCountAsync();
        count.Should().Be(3);

        // Query with v1: should match id 1 with score 1.0, id 3 with ~0.7071, id 2 with 0.0
        var query = new float[] { 1.0f, 0.0f, 0.0f };
        var hits = await index.SearchAsync(query, k: 3);

        hits.Should().HaveCount(3);
        hits[0].Id.Should().Be(1L);
        hits[0].Score.Should().BeApproximately(1.0f, 0.001f);

        hits[1].Id.Should().Be(3L);
        hits[1].Score.Should().BeApproximately(0.7071f, 0.001f);

        hits[2].Id.Should().Be(2L);
        hits[2].Score.Should().BeApproximately(0.0f, 0.001f);
    }

    [Fact]
    public async Task SearchAsync_WithFilter_FiltersCandidates()
    {
        await using var index = new SqliteVectorIndex(_dbPath);
        await index.InitializeAsync();

        var v1 = new float[] { 1.0f, 0.0f, 0.0f };
        var v2 = new float[] { 1.0f, 0.0f, 0.0f };

        await index.AddAsync(new (long, float[])[]
        {
            (10L, v1),
            (20L, v2)
        });

        // Filter out id 10
        var hits = await index.SearchAsync(v1, k: 5, filter: id => id != 10L);

        hits.Should().HaveCount(1);
        hits[0].Id.Should().Be(20L);
    }

    [Fact]
    public async Task SearchExactAsync_ScoresOnlyProvidedCandidates()
    {
        await using var index = new SqliteVectorIndex(_dbPath);
        await index.InitializeAsync();

        var v1 = new float[] { 1.0f, 0.0f, 0.0f };
        var v2 = new float[] { 0.9f, 0.1f, 0.0f };
        var v3 = new float[] { 0.0f, 1.0f, 0.0f };

        await index.AddAsync(new (long, float[])[]
        {
            (1L, v1),
            (2L, v2),
            (3L, v3)
        });

        // SearchExact with candidate set {2, 3} (id 1 excluded)
        var candidates = new HashSet<long> { 2L, 3L };
        var hits = await index.SearchExactAsync(v1, k: 5, candidates);

        hits.Should().HaveCount(2);
        hits[0].Id.Should().Be(2L);
        hits[1].Id.Should().Be(3L);
    }

    [Fact]
    public async Task Persistence_ReopeningDatabaseReloadsVectors()
    {
        var v = new float[] { 0.5f, 0.5f, 0.5f, 0.5f };

        // Write with first instance
        await using (var index1 = new SqliteVectorIndex(_dbPath))
        {
            await index1.InitializeAsync();
            await index1.AddAsync(new (long, float[])[] { (100L, v) });
        }

        // Reopen with second instance pointing to same file
        await using (var index2 = new SqliteVectorIndex(_dbPath))
        {
            await index2.InitializeAsync();

            var count = await index2.GetCountAsync();
            count.Should().Be(1);

            var hits = await index2.SearchAsync(v, k: 1);
            hits.Should().HaveCount(1);
            hits[0].Id.Should().Be(100L);
            hits[0].Score.Should().BeApproximately(1.0f, 0.001f);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesVectorsFromPersistenceAndMemory()
    {
        await using var index = new SqliteVectorIndex(_dbPath);
        await index.InitializeAsync();

        var v = new float[] { 1.0f, 0.0f };
        await index.AddAsync(new (long, float[])[]
        {
            (1L, v),
            (2L, v)
        });

        await index.DeleteAsync(new[] { 1L });

        var count = await index.GetCountAsync();
        count.Should().Be(1);

        var hits = await index.SearchAsync(v, k: 5);
        hits.Should().HaveCount(1);
        hits[0].Id.Should().Be(2L);
    }

    [Fact]
    public async Task UninitializedVectorIndex_AutoInitializesLazilyOnFirstAccess()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"lfa_uninit_vec_{Guid.NewGuid():N}.db");
        await using var index = new SqliteVectorIndex(tempPath);
        try
        {
            var count = await index.GetCountAsync();
            count.Should().Be(0);

            var hits = await index.SearchAsync(new float[] { 1.0f, 0.0f }, k: 5);
            hits.Should().BeEmpty();
        }
        finally
        {
            await index.DisposeAsync();
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }
}

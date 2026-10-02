using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Infrastructure.Storage;
using Xunit;

namespace LocalFileAgent.Infrastructure.Tests;

public class SqliteVisualVectorIndexTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public SqliteVisualVectorIndexTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LfaVisualVectorIndexTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "visual_vectors.db");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AddAndSearchAsync_RanksByCosineSimilarity()
    {
        await using var index = new SqliteVisualVectorIndex(_dbPath);
        await index.InitializeAsync();

        // 4-dimensional sample vectors
        var vec1 = new float[] { 1.0f, 0.0f, 0.0f, 0.0f };
        var vec2 = new float[] { 0.7071f, 0.7071f, 0.0f, 0.0f };
        var vec3 = new float[] { 0.0f, 1.0f, 0.0f, 0.0f };

        await index.AddAsync(new[]
        {
            (1L, vec1),
            (2L, vec2),
            (3L, vec3)
        });

        var count = await index.GetCountAsync();
        count.Should().Be(3);

        // Query aligned with vec1
        var query = new float[] { 1.0f, 0.0f, 0.0f, 0.0f };
        var results = await index.SearchAsync(query, k: 3);

        results.Should().HaveCount(3);
        results[0].FileId.Should().Be(1L);
        results[0].Score.Should().BeApproximately(1.0f, 0.001f);
        results[1].FileId.Should().Be(2L);
        results[1].Score.Should().BeApproximately(0.7071f, 0.001f);
        results[2].FileId.Should().Be(3L);
        results[2].Score.Should().BeApproximately(0.0f, 0.001f);
    }

    [Fact]
    public async Task SearchAsync_AppliesFilterCorrectly()
    {
        await using var index = new SqliteVisualVectorIndex(_dbPath);
        await index.InitializeAsync();

        var vec1 = new float[] { 1.0f, 0.0f };
        var vec2 = new float[] { 0.9f, 0.1f };

        await index.AddAsync(new[]
        {
            (10L, vec1),
            (20L, vec2)
        });

        // Filter excludes 10L
        var query = new float[] { 1.0f, 0.0f };
        var results = await index.SearchAsync(query, k: 2, filter: id => id != 10L);

        results.Should().HaveCount(1);
        results[0].FileId.Should().Be(20L);
    }

    [Fact]
    public async Task DeleteAsync_RemovesVectors()
    {
        await using var index = new SqliteVisualVectorIndex(_dbPath);
        await index.InitializeAsync();

        var vec1 = new float[] { 1.0f, 0.0f };
        await index.AddAsync(new[] { (42L, vec1) });

        var countBefore = await index.GetCountAsync();
        countBefore.Should().Be(1);

        await index.DeleteAsync(new[] { 42L });

        var countAfter = await index.GetCountAsync();
        countAfter.Should().Be(0);

        var results = await index.SearchAsync(vec1, k: 5);
        results.Should().BeEmpty();
    }
}

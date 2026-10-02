using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Infrastructure.Vision;
using Xunit;

namespace LocalFileAgent.Infrastructure.Tests;

public class ImageHasherTests
{
    private static string GetSyntheticCorpusPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "corpus", "synthetic");
    }

    [Fact]
    public async Task ComputeDHashAsync_ReturnsZero_WhenFileDoesNotExist()
    {
        var hasher = new ImageHasher();
        var hash = await hasher.ComputeDHashAsync("C:\\non_existent_image_12345.png");
        hash.Should().Be(0UL);
    }

    [Fact]
    public void ComputeHammingDistance_CalculatesCorrectBitDifference()
    {
        var hasher = new ImageHasher();

        hasher.ComputeHammingDistance(0UL, 0UL).Should().Be(0);
        hasher.ComputeHammingDistance(0x1UL, 0x0UL).Should().Be(1);
        hasher.ComputeHammingDistance(0xFFUL, 0x0UL).Should().Be(8);
        hasher.ComputeHammingDistance(0x0F0F0F0F0F0F0F0FUL, 0xF0F0F0F0F0F0F0F0UL).Should().Be(64);
    }

    [Fact]
    public void AreNearDuplicates_RespectsThreshold()
    {
        var hasher = new ImageHasher();

        // 3 bits difference
        var h1 = 0b0000_0111UL;
        var h2 = 0b0000_0000UL;
        hasher.AreNearDuplicates(h1, h2, threshold: 3).Should().BeTrue();
        hasher.AreNearDuplicates(h1, h2, threshold: 2).Should().BeFalse();
    }

    [Fact]
    public async Task ComputeDHashAsync_ProducesConsistentHashForCorpusImage()
    {
        var corpusPath = GetSyntheticCorpusPath();
        var imagePath = Path.Combine(corpusPath, "صورة_فاتورة_ممسوحة_206.png");

        if (!File.Exists(imagePath))
        {
            return;
        }

        var hasher = new ImageHasher();
        var hash1 = await hasher.ComputeDHashAsync(imagePath);
        var hash2 = await hasher.ComputeDHashAsync(imagePath);

        hash1.Should().NotBe(0UL);
        hash1.Should().Be(hash2);
        hasher.ComputeHammingDistance(hash1, hash2).Should().Be(0);
        hasher.AreNearDuplicates(hash1, hash2, threshold: 0).Should().BeTrue();
    }
}

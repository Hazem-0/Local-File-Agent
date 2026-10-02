using System.IO;
using FluentAssertions;
using Xunit;

namespace LocalFileAgent.Infrastructure.Tests;

public class BannedApiDocumentationTests
{
    [Fact]
    public void BannedSymbolsConfiguration_ExistsInRepositoryRoot()
    {
        // Traverse up to repository root to locate BannedSymbols.txt
        var currentDir = Directory.GetCurrentDirectory();
        var rootDir = Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", "..", ".."));
        var bannedSymbolsPath = Path.Combine(rootDir, "BannedSymbols.txt");

        File.Exists(bannedSymbolsPath).Should().BeTrue("BannedSymbols.txt must exist at repository root to enforce read-only safety");
    }
}

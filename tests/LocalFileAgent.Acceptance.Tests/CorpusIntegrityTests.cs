using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using FluentAssertions;
using Xunit;

namespace LocalFileAgent.Acceptance.Tests;

public sealed class CorpusIntegritySnapshot
{
    private readonly Dictionary<string, string> _fileHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _rootDirectory;

    public CorpusIntegritySnapshot(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        Capture();
    }

    public void Capture()
    {
        _fileHashes.Clear();
        if (!Directory.Exists(_rootDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_rootDirectory, "*", SearchOption.AllDirectories))
        {
            using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(stream);
            var hashString = Convert.ToHexString(hashBytes);
            _fileHashes[Path.GetFullPath(file)] = hashString;
        }
    }

    public void AssertUnchanged()
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return;
        }

        var currentFiles = Directory.GetFiles(_rootDirectory, "*", SearchOption.AllDirectories);
        currentFiles.Length.Should().Be(_fileHashes.Count, "No files should be added or deleted in corpus directory");

        foreach (var file in currentFiles)
        {
            var fullPath = Path.GetFullPath(file);
            _fileHashes.ContainsKey(fullPath).Should().BeTrue($"File {fullPath} must have been in initial snapshot");

            using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(stream);
            var currentHash = Convert.ToHexString(hashBytes);

            currentHash.Should().Be(_fileHashes[fullPath], $"File {fullPath} content must remain strictly unmodified");
        }
    }
}

public class CorpusIntegrityTests
{
    [Fact]
    public void Snapshot_DetectsNoModificationsOnReadOnlyAccess()
    {
        var tempCorpus = Path.Combine(Path.GetTempPath(), "LocalFileAgent_CorpusTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempCorpus);

        try
        {
            var file1 = Path.Combine(tempCorpus, "test1.txt");
            var file2 = Path.Combine(tempCorpus, "test2.pdf");

            // Setup test files using standard stream creation
            using (var s1 = new StreamWriter(File.Open(file1, FileMode.Create, FileAccess.Write, FileShare.None)))
            {
                s1.Write("عقد بيع ابتدائي");
            }
            using (var s2 = new StreamWriter(File.Open(file2, FileMode.Create, FileAccess.Write, FileShare.None)))
            {
                s2.Write("Fake PDF Header");
            }

            var snapshot = new CorpusIntegritySnapshot(tempCorpus);

            // Read-only access simulation
            using (var sRead = File.Open(file1, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(sRead))
            {
                var content = reader.ReadToEnd();
                content.Should().Contain("عقد");
            }

            // Verify integrity
            snapshot.AssertUnchanged();
        }
        finally
        {
            if (Directory.Exists(tempCorpus))
            {
                Directory.Delete(tempCorpus, true);
            }
        }
    }
}

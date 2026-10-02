using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace LocalFileAgent.Domain.FileSystem;

public sealed record DiscoveredFile(
    string Path,
    string Name,
    string Extension,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    FileAttributes Attributes
);

public sealed record ScanOptions(
    int MaxDepth = 15,
    IReadOnlySet<string>? IncludedExtensions = null,
    IReadOnlySet<string>? ExcludedDirectoryNames = null,
    bool SkipHidden = true,
    bool SkipSystem = true,
    bool SkipCloudPlaceholders = true,
    long MaxFileSizeBytes = 104_857_600L // 100 MB
);

public sealed record ScanProgressReport(
    int DirectoriesScanned,
    int FilesDiscovered,
    int FilesSkipped,
    string CurrentDirectory
);

public interface IFileScanner
{
    IAsyncEnumerable<DiscoveredFile> ScanAsync(
        string rootPath,
        ScanOptions options,
        IProgress<ScanProgressReport>? progress = null,
        CancellationToken cancellationToken = default
    );
}

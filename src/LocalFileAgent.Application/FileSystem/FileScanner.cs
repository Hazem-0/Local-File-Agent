using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using LocalFileAgent.Domain.FileSystem;

namespace LocalFileAgent.Application.FileSystem;

public sealed class FileScanner : IFileScanner
{
    private static readonly HashSet<string> DefaultExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN",
        "System Volume Information",
        ".git",
        ".vs",
        ".svn",
        "node_modules",
        "bin",
        "obj",
        "AppData"
    };

    private static readonly HashSet<string> DefaultSupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".docx",
        ".doc",
        ".xlsx",
        ".xls",
        ".pptx",
        ".ppt",
        ".txt",
        ".md",
        ".csv",
        ".png",
        ".jpg",
        ".jpeg",
        ".tiff",
        ".bmp"
    };

    public async IAsyncEnumerable<DiscoveredFile> ScanAsync(
        string rootPath,
        ScanOptions options,
        IProgress<ScanProgressReport>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(options);

        var rootDir = new DirectoryInfo(rootPath);
        if (!rootDir.Exists)
        {
            yield break;
        }

        var excludedDirs = options.ExcludedDirectoryNames ?? DefaultExcludedDirectories;
        var allowedExtensions = options.IncludedExtensions ?? DefaultSupportedExtensions;

        var dirsScanned = 0;
        var filesDiscovered = 0;
        var filesSkipped = 0;

        var stack = new Stack<(DirectoryInfo Directory, int Depth)>();
        stack.Push((rootDir, 0));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (currentDir, depth) = stack.Pop();
            dirsScanned++;

            progress?.Report(new ScanProgressReport(dirsScanned, filesDiscovered, filesSkipped, currentDir.FullName));

            // Yield control periodically to remain cooperative
            await Task.Yield();

            // 1. Process files in current directory
            IEnumerable<FileInfo> fileList;
            try
            {
                fileList = currentDir.EnumerateFiles();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                filesSkipped++;
                continue;
            }

            foreach (var file in fileList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsFileCandidate(file, options, allowedExtensions))
                {
                    filesSkipped++;
                    continue;
                }

                filesDiscovered++;
                yield return new DiscoveredFile(
                    Path: file.FullName,
                    Name: file.Name,
                    Extension: file.Extension.ToLowerInvariant(),
                    SizeBytes: file.Length,
                    CreatedAt: file.CreationTimeUtc,
                    ModifiedAt: file.LastWriteTimeUtc,
                    Attributes: file.Attributes
                );
            }

            // 2. Queue child directories if within max depth
            if (depth >= options.MaxDepth)
            {
                continue;
            }

            IEnumerable<DirectoryInfo> subDirs;
            try
            {
                subDirs = currentDir.EnumerateDirectories();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var subDir in subDirs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsDirectoryExcluded(subDir, options, excludedDirs))
                {
                    continue;
                }

                stack.Push((subDir, depth + 1));
            }
        }

        progress?.Report(new ScanProgressReport(dirsScanned, filesDiscovered, filesSkipped, rootDir.FullName));
    }

    private static bool IsDirectoryExcluded(
        DirectoryInfo dir,
        ScanOptions options,
        IReadOnlySet<string> excludedDirs)
    {
        if (excludedDirs.Contains(dir.Name))
        {
            return true;
        }

        var attr = dir.Attributes;

        if (options.SkipHidden && (attr & FileAttributes.Hidden) != 0)
        {
            return true;
        }

        if (options.SkipSystem && (attr & FileAttributes.System) != 0)
        {
            return true;
        }

        // Avoid infinite directory loop via symlink / junction
        if ((attr & FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }

        return false;
    }

    private static bool IsFileCandidate(
        FileInfo file,
        ScanOptions options,
        IReadOnlySet<string> allowedExtensions)
    {
        var attr = file.Attributes;

        if (options.SkipHidden && (attr & FileAttributes.Hidden) != 0)
        {
            return false;
        }

        if (options.SkipSystem && (attr & FileAttributes.System) != 0)
        {
            return false;
        }

        if (options.SkipCloudPlaceholders && IsCloudPlaceholder(attr))
        {
            return false;
        }

        if (file.Length > options.MaxFileSizeBytes)
        {
            return false;
        }

        var ext = file.Extension;
        if (string.IsNullOrEmpty(ext) || !allowedExtensions.Contains(ext))
        {
            return false;
        }

        return true;
    }

    private static bool IsCloudPlaceholder(FileAttributes attr)
    {
        // Check Offline attribute (0x1000)
        if ((attr & FileAttributes.Offline) != 0)
        {
            return true;
        }

        // Check RecallOnOpen (0x00040000) and RecallOnDataAccess (0x00400000)
        const FileAttributes recallOnOpen = (FileAttributes)0x00040000;
        const FileAttributes recallOnDataAccess = (FileAttributes)0x00400000;

        return (attr & recallOnOpen) != 0 || (attr & recallOnDataAccess) != 0;
    }
}

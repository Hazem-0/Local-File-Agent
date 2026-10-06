using System;
using System.Diagnostics;
using System.IO;

namespace LocalFileAgent.Application.FileSystem;

public interface IFileLauncher
{
    bool OpenFile(string filePath);
    bool OpenContainingFolder(string filePath);
}

public sealed class SafeFileLauncher : IFileLauncher
{
    public static readonly SafeFileLauncher Instance = new();

    private readonly object _lock = new();
    private string? _lastOpenedFile;
    private DateTime _lastFileTime;
    private string? _lastOpenedFolder;
    private DateTime _lastFolderTime;

    public bool OpenFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        try
        {
            var cleanPath = filePath.Trim('\u200E', '\u200F', '\u202A', '\u202B', '\u202C', '\u202D', '\u202E', '\u2066', '\u2067', '\u2068', '\u2069', ' ', '"', '\'');
            var resolved = Path.IsPathRooted(cleanPath) ? cleanPath : Path.GetFullPath(cleanPath);
            if (!File.Exists(resolved))
            {
                return false;
            }

            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (string.Equals(_lastOpenedFile, resolved, StringComparison.OrdinalIgnoreCase) &&
                    (now - _lastFileTime).TotalMilliseconds < 800)
                {
                    return true;
                }
                _lastOpenedFile = resolved;
                _lastFileTime = now;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = resolved,
                UseShellExecute = true
            };

            using var process = Process.Start(startInfo);
            return process != null;
        }
        catch
        {
            return false;
        }
    }

    public bool OpenContainingFolder(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        try
        {
            var cleanPath = filePath.Trim('\u200E', '\u200F', '\u202A', '\u202B', '\u202C', '\u202D', '\u202E', '\u2066', '\u2067', '\u2068', '\u2069', ' ', '"', '\'');
            var resolved = Path.IsPathRooted(cleanPath) ? cleanPath : Path.GetFullPath(cleanPath);

            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (string.Equals(_lastOpenedFolder, resolved, StringComparison.OrdinalIgnoreCase) &&
                    (now - _lastFolderTime).TotalMilliseconds < 800)
                {
                    return true;
                }
                _lastOpenedFolder = resolved;
                _lastFolderTime = now;
            }

            if (File.Exists(resolved))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{resolved}\"",
                    UseShellExecute = true
                };

                using var process = Process.Start(startInfo);
                return process != null;
            }

            var dir = Directory.Exists(resolved) ? resolved : Path.GetDirectoryName(resolved);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                };

                using var process = Process.Start(startInfo);
                return process != null;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}

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

    public bool OpenFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        try
        {
            var cleanPath = filePath.Trim('\u2066', '\u2067', '\u2068', '\u2069', ' ', '"', '\'');
            if (!File.Exists(cleanPath))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = cleanPath,
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
            var cleanPath = filePath.Trim('\u2066', '\u2067', '\u2068', '\u2069', ' ', '"', '\'');

            if (File.Exists(cleanPath))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{cleanPath}\"",
                    UseShellExecute = false
                };

                using var process = Process.Start(startInfo);
                return process != null;
            }

            var dir = Directory.Exists(cleanPath) ? cleanPath : Path.GetDirectoryName(cleanPath);
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

using System;
using System.IO;

namespace LocalFileAgent.Infrastructure.Storage;

public static class AppDataPaths
{
    public static string GetDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "LocalFileAgent");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    public static string GetDatabasePath() => Path.Combine(GetDataDirectory(), "lfa_index.db");
}

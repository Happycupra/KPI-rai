using System.IO;

namespace Produktionsplanung.App.Services;

public static class AppPaths
{
    internal static string? RootDirectoryOverride { get; set; }

    public static string RootDirectory => RootDirectoryOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Produktionsplanung");

    public static string DataDirectory => Path.Combine(RootDirectory, "Data");
    public static string ConfigDirectory => Path.Combine(RootDirectory, "Config");
    public static string BackupsDirectory => Path.Combine(RootDirectory, "Backups");
    public static string ExportsDirectory => Path.Combine(RootDirectory, "Exports");

    public static string DatabasePath => Path.Combine(DataDirectory, "produktionsplanung.db");
    public static string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
    }
}

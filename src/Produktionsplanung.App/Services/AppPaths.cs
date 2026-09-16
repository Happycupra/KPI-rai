using System.IO;

namespace Produktionsplanung.App.Services;

public static class AppPaths
{
    internal static string? RootDirectoryOverride { get; set; }

    public static bool IsPortableMode { get; private set; }

    public static string ExecutionDirectory
    {
        get
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var directory = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    return directory;
            }

            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public static string PortableMarkerPath => Path.Combine(ExecutionDirectory, "portable.mode");

    public static string RootDirectory => RootDirectoryOverride ?? (IsPortableMode
        ? ExecutionDirectory
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Produktionsplanung"));

    public static string DataDirectory => IsPortableMode && RootDirectoryOverride is null
        ? RootDirectory
        : Path.Combine(RootDirectory, "Data");

    public static string ConfigDirectory => IsPortableMode && RootDirectoryOverride is null
        ? RootDirectory
        : Path.Combine(RootDirectory, "Config");

    public static string BackupsDirectory => Path.Combine(RootDirectory, "Backups");
    public static string ExportsDirectory => Path.Combine(RootDirectory, "Exports");

    public static string DatabasePath => Path.Combine(DataDirectory, "produktionsplanung.db");
    public static string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");

    public static string StorageModeText => IsPortableMode
        ? "USB / Portable – Daten bleiben beim Programm"
        : "Lokale Installation – Daten unter %LOCALAPPDATA%";

    public static void InitializeStorageMode(IEnumerable<string>? args = null)
    {
        if (RootDirectoryOverride is not null)
            return;

        var requestedByArgument = args?.Any(x =>
            string.Equals(x, "--portable", StringComparison.OrdinalIgnoreCase)) == true;

        IsPortableMode = requestedByArgument || File.Exists(PortableMarkerPath);
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
    }
}

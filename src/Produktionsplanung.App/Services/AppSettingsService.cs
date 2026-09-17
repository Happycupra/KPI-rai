using System.IO;
using System.Text.Json;

namespace Produktionsplanung.App.Services;

public sealed class AppSettings
{
    public string CompanyName { get; set; } = "SolutionCompakt";
    public string SiteName { get; set; } = string.Empty;
    public string DefaultBackupDirectory { get; set; } = AppPaths.IsPortableMode ? "Backups" : AppPaths.BackupsDirectory;
    public string DefaultExportDirectory { get; set; } = AppPaths.IsPortableMode ? "Exports" : AppPaths.ExportsDirectory;
    public bool AutoBackupOnExit { get; set; } = true;
    public int BackupRetentionCount { get; set; } = 10;
    public string CsvDelimiter { get; set; } = ";";
    public bool IncludeUtf8Bom { get; set; } = true;
    public DateTime? LastSuccessfulBackupAtLocal { get; set; }
    public string LastSuccessfulBackupPath { get; set; } = string.Empty;

    // Local recovery credential. Only the PBKDF2 hash/salt is stored; the recovery code itself
    // is shown once to the administrator and must be kept outside the application.
    public string RecoveryCodeHash { get; set; } = string.Empty;
    public string RecoveryCodeSalt { get; set; } = string.Empty;
    public DateTime? RecoveryCodeCreatedAtUtc { get; set; }

    // Navigation/UI preferences.
    public bool SidebarCollapsed { get; set; }
    public bool PlanningGroupCollapsed { get; set; }
    public bool ProductionGroupCollapsed { get; set; }
    public bool MasterDataGroupCollapsed { get; set; }
    public bool SystemGroupCollapsed { get; set; }

    // Planning calendar preferences.
    public int CalendarSelectedViewIndex { get; set; } = 1;
    public string CalendarSearchText { get; set; } = string.Empty;
    public bool CalendarShowAssignments { get; set; } = true;
    public bool CalendarShowOrders { get; set; } = true;
    public bool CalendarShowAbsences { get; set; } = true;
    public bool CalendarShowOperatingCalendar { get; set; } = true;
    public bool CalendarShowWeekends { get; set; } = true;
}

public static class AppSettingsService
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        lock (Sync)
        {
            return LoadUnlocked();
        }
    }

    public static void Save(AppSettings settings)
    {
        lock (Sync)
        {
            SaveUnlocked(settings);
        }
    }

    public static AppSettings Update(Action<AppSettings> update)
    {
        lock (Sync)
        {
            var settings = LoadUnlocked();
            update(settings);
            SaveUnlocked(settings);
            return settings;
        }
    }

    public static string ResolveStoragePath(string? configured, string fallbackRelative)
    {
        var raw = string.IsNullOrWhiteSpace(configured) ? fallbackRelative : configured.Trim();
        if (Path.IsPathRooted(raw))
        {
            if (!AppPaths.IsPortableMode)
                return raw;
            var root = Path.GetPathRoot(raw);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                return raw;
            return Path.Combine(AppPaths.RootDirectory, fallbackRelative);
        }
        return Path.GetFullPath(Path.Combine(AppPaths.RootDirectory, raw));
    }

    public static string ToStoredStoragePath(string? path, string fallbackRelative)
    {
        if (string.IsNullOrWhiteSpace(path))
            return AppPaths.IsPortableMode ? fallbackRelative : Path.Combine(AppPaths.RootDirectory, fallbackRelative);

        var full = Path.GetFullPath(path.Trim());
        if (AppPaths.IsPortableMode)
        {
            var root = Path.GetFullPath(AppPaths.RootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(AppPaths.RootDirectory, full);
        }
        return full;
    }

    private static AppSettings LoadUnlocked()
    {
        AppPaths.EnsureDirectories();
        if (!File.Exists(AppPaths.SettingsPath))
            return NewDefaults();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsPath), JsonOptions) ?? NewDefaults();
            Normalize(settings);
            return settings;
        }
        catch
        {
            return NewDefaults();
        }
    }

    private static void SaveUnlocked(AppSettings settings)
    {
        AppPaths.EnsureDirectories();
        Normalize(settings);
        File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static AppSettings NewDefaults() => new()
    {
        CompanyName = "SolutionCompakt",
        DefaultBackupDirectory = AppPaths.IsPortableMode ? "Backups" : AppPaths.BackupsDirectory,
        DefaultExportDirectory = AppPaths.IsPortableMode ? "Exports" : AppPaths.ExportsDirectory
    };

    private static void Normalize(AppSettings settings)
    {
        var company = settings.CompanyName?.Trim() ?? string.Empty;
        settings.CompanyName = string.IsNullOrWhiteSpace(company) ||
                               string.Equals(company, "OpsCompact", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(company, "KPI-rai", StringComparison.OrdinalIgnoreCase)
            ? "SolutionCompakt"
            : company;
        settings.SiteName = settings.SiteName?.Trim() ?? string.Empty;
        settings.DefaultBackupDirectory = ToStoredStoragePath(settings.DefaultBackupDirectory, "Backups");
        settings.DefaultExportDirectory = ToStoredStoragePath(settings.DefaultExportDirectory, "Exports");
        settings.BackupRetentionCount = Math.Clamp(settings.BackupRetentionCount, 1, 100);
        settings.CsvDelimiter = string.IsNullOrEmpty(settings.CsvDelimiter) ? ";" : settings.CsvDelimiter[..1];
        settings.LastSuccessfulBackupPath = settings.LastSuccessfulBackupPath?.Trim() ?? string.Empty;
        settings.RecoveryCodeHash = settings.RecoveryCodeHash?.Trim() ?? string.Empty;
        settings.RecoveryCodeSalt = settings.RecoveryCodeSalt?.Trim() ?? string.Empty;
        settings.CalendarSelectedViewIndex = Math.Clamp(settings.CalendarSelectedViewIndex, 0, 2);
        settings.CalendarSearchText ??= string.Empty;
    }
}

using System.IO;
using System.Text.Json;

namespace Produktionsplanung.App.Services;

public sealed class AppSettings
{
    public string CompanyName { get; set; } = "OpsCompact";
    public string SiteName { get; set; } = string.Empty;
    public string DefaultBackupDirectory { get; set; } = AppPaths.BackupsDirectory;
    public string DefaultExportDirectory { get; set; } = AppPaths.ExportsDirectory;
    public bool AutoBackupOnExit { get; set; } = true;
    public int BackupRetentionCount { get; set; } = 10;
    public string CsvDelimiter { get; set; } = ";";
    public bool IncludeUtf8Bom { get; set; } = true;
}

public static class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Load()
    {
        AppPaths.EnsureDirectories();
        if (!File.Exists(AppPaths.SettingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            Normalize(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.EnsureDirectories();
        Normalize(settings);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppPaths.SettingsPath, json);
    }

    private static void Normalize(AppSettings settings)
    {
        settings.CompanyName = string.IsNullOrWhiteSpace(settings.CompanyName) ? "OpsCompact" : settings.CompanyName.Trim();
        settings.SiteName = settings.SiteName?.Trim() ?? string.Empty;
        settings.DefaultBackupDirectory = string.IsNullOrWhiteSpace(settings.DefaultBackupDirectory)
            ? AppPaths.BackupsDirectory
            : settings.DefaultBackupDirectory.Trim();
        settings.DefaultExportDirectory = string.IsNullOrWhiteSpace(settings.DefaultExportDirectory)
            ? AppPaths.ExportsDirectory
            : settings.DefaultExportDirectory.Trim();
        settings.BackupRetentionCount = Math.Clamp(settings.BackupRetentionCount, 1, 100);
        settings.CsvDelimiter = string.IsNullOrEmpty(settings.CsvDelimiter) ? ";" : settings.CsvDelimiter[..1];
    }
}

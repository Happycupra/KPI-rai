using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Produktionsplanung.App.Services;

public static class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string CreateBackup(string targetPath, AppSettings settings)
    {
        AppPaths.EnsureDirectories();
        if (!File.Exists(AppPaths.DatabasePath))
            throw new InvalidOperationException("Die KPI-rai-Datenbank wurde nicht gefunden.");

        if (!targetPath.EndsWith(".kpibackup", StringComparison.OrdinalIgnoreCase))
            targetPath += ".kpibackup";

        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
            Directory.CreateDirectory(targetDirectory);

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kpi-rai-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var snapshotPath = Path.Combine(tempDirectory, "produktionsplanung.db");
            CreateDatabaseSnapshot(snapshotPath);

            File.WriteAllText(
                Path.Combine(tempDirectory, "settings.json"),
                JsonSerializer.Serialize(settings, JsonOptions));

            var manifest = new BackupManifest
            {
                Product = "KPI-rai",
                CreatedAtLocal = DateTime.Now,
                AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                DatabaseFile = "produktionsplanung.db",
                SettingsFile = "settings.json"
            };

            File.WriteAllText(
                Path.Combine(tempDirectory, "manifest.json"),
                JsonSerializer.Serialize(manifest, JsonOptions));

            if (File.Exists(targetPath))
                File.Delete(targetPath);

            ZipFile.CreateFromDirectory(tempDirectory, targetPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return targetPath;
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    public static string CreateAutomaticBackup(AppSettings settings)
    {
        var directory = string.IsNullOrWhiteSpace(settings.DefaultBackupDirectory)
            ? AppPaths.BackupsDirectory
            : settings.DefaultBackupDirectory;

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"KPI-rai-auto-{DateTime.Now:yyyyMMdd-HHmmss}.kpibackup");
        var created = CreateBackup(path, settings);
        PruneAutomaticBackups(directory, settings.BackupRetentionCount);
        return created;
    }

    public static void RestoreBackup(string backupPath)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Die ausgewählte Backup-Datei wurde nicht gefunden.", backupPath);

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kpi-rai-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            ZipFile.ExtractToDirectory(backupPath, tempDirectory, overwriteFiles: true);
            var databasePath = Path.Combine(tempDirectory, "produktionsplanung.db");
            if (!File.Exists(databasePath))
                throw new InvalidDataException("Das Backup enthält keine KPI-rai-Datenbank.");

            ValidateDatabase(databasePath);

            var settingsPath = Path.Combine(tempDirectory, "settings.json");
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                _ = JsonSerializer.Deserialize<AppSettings>(json)
                    ?? throw new InvalidDataException("Die Einstellungen im Backup sind ungültig.");
            }

            AppPaths.EnsureDirectories();
            SqliteConnection.ClearAllPools();
            File.Copy(databasePath, AppPaths.DatabasePath, overwrite: true);

            if (File.Exists(settingsPath))
                File.Copy(settingsPath, AppPaths.SettingsPath, overwrite: true);
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private static void CreateDatabaseSnapshot(string destinationPath)
    {
        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = AppPaths.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite
        };
        var destinationBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };

        using var source = new SqliteConnection(sourceBuilder.ConnectionString);
        using var destination = new SqliteConnection(destinationBuilder.ConnectionString);
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static void ValidateDatabase(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        };

        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Employees','ProductionOrders');";
        var count = Convert.ToInt32(command.ExecuteScalar());
        if (count < 2)
            throw new InvalidDataException("Die Datei ist kein gültiges KPI-rai-Backup.");
    }

    private static void PruneAutomaticBackups(string directory, int retentionCount)
    {
        retentionCount = Math.Clamp(retentionCount, 1, 100);
        var files = new DirectoryInfo(directory)
            .GetFiles("KPI-rai-auto-*.kpibackup")
            .OrderByDescending(x => x.CreationTimeUtc)
            .Skip(retentionCount)
            .ToList();

        foreach (var file in files)
        {
            try { file.Delete(); }
            catch { }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { }
    }
}

public sealed class BackupManifest
{
    public string Product { get; set; } = string.Empty;
    public DateTime CreatedAtLocal { get; set; }
    public string AppVersion { get; set; } = string.Empty;
    public string DatabaseFile { get; set; } = string.Empty;
    public string SettingsFile { get; set; } = string.Empty;
}

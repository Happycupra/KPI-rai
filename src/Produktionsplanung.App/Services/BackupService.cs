using System.IO;
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
        if (CentralModeService.IsEnabled)
            throw new InvalidOperationException("Im zentralen Mehrbenutzerbetrieb liegen die operativen Daten in PostgreSQL. Lokale .kpibackup-Dateien sind deshalb deaktiviert; sichern Sie die PostgreSQL-Datenbank serverseitig.");

        AppPaths.EnsureDirectories();
        if (!File.Exists(AppPaths.DatabasePath))
            throw new InvalidOperationException("Die SolutionCompakt-Datenbank wurde nicht gefunden.");
        if (!Path.IsPathRooted(targetPath)) targetPath = Path.Combine(AppPaths.RootDirectory, targetPath);
        if (!targetPath.EndsWith(".kpibackup", StringComparison.OrdinalIgnoreCase)) targetPath += ".kpibackup";
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var temp = Path.Combine(Path.GetTempPath(), $"solutioncompakt-backup-{Guid.NewGuid():N}");
        var stagedArchive = targetPath + $".{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(temp);
        try
        {
            CreateDatabaseSnapshot(Path.Combine(temp, "produktionsplanung.db"));
            File.WriteAllText(Path.Combine(temp, "settings.json"), JsonSerializer.Serialize(settings, JsonOptions));
            var manifest = new BackupManifest
            {
                Product = "SolutionCompakt", CreatedAtLocal = DateTime.Now,
                AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                DatabaseFile = "produktionsplanung.db", SettingsFile = "settings.json",
                CompanyId = settings.CompanyId, CompanyCode = settings.CompanyCode
            };
            File.WriteAllText(Path.Combine(temp, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
            ZipFile.CreateFromDirectory(temp, stagedArchive, CompressionLevel.Optimal, false);
            File.Move(stagedArchive, targetPath, overwrite: true);
            settings.LastSuccessfulBackupAtLocal = DateTime.Now;
            settings.LastSuccessfulBackupPath = AppPaths.IsPortableMode
                ? AppSettingsService.ToStoredStoragePath(targetPath, "Backups") : targetPath;
            AppSettingsService.Save(settings);
            return targetPath;
        }
        finally { TryDeleteFile(stagedArchive); TryDeleteDirectory(temp); }
    }

    public static string CreateAutomaticBackup(AppSettings settings)
    {
        var directory = AppSettingsService.ResolveStoragePath(settings.DefaultBackupDirectory, "Backups");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"SolutionCompakt-auto-{DateTime.Now:yyyyMMdd-HHmmss-fff}.kpibackup");
        var created = CreateBackup(path, settings);
        PruneAutomaticBackups(directory, settings.BackupRetentionCount);
        return created;
    }

    public static void RestoreBackup(string backupPath) =>
        AppSettingsService.RunExclusive(() => RestoreBackupCore(backupPath));

    private static void RestoreBackupCore(string backupPath)
    {
        if (CentralModeService.IsEnabled)
            throw new InvalidOperationException("Eine lokale SQLite-Wiederherstellung ist im zentralen Mehrbenutzerbetrieb gesperrt. Deaktivieren Sie den Zentralmodus nur für eine bewusst lokale Wiederherstellung oder stellen Sie PostgreSQL serverseitig wieder her.");
        if (!File.Exists(backupPath)) throw new FileNotFoundException("Die ausgewählte Backup-Datei wurde nicht gefunden.", backupPath);
        AppPaths.EnsureDirectories();
        var temp = Path.Combine(AppPaths.RootDirectory, $"restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        var preserveRecoveryFiles = false;
        try
        {
            ZipFile.ExtractToDirectory(backupPath, temp, true);
            var database = Path.Combine(temp, "produktionsplanung.db");
            if (!File.Exists(database)) throw new InvalidDataException("Das Backup enthält keine SolutionCompakt-Datenbank.");
            ValidateDatabase(database);
            var settingsPath = Path.Combine(temp, "settings.json");
            if (File.Exists(settingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath))
                    ?? throw new InvalidDataException("Die Einstellungen im Backup sind ungültig.");
                var current = AppSettingsService.Load();
                if (!string.IsNullOrWhiteSpace(current.CompanyId) && !string.IsNullOrWhiteSpace(settings.CompanyId) &&
                    !string.Equals(current.CompanyId, settings.CompanyId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Dieses Backup gehört zu einer anderen Firma ({settings.CompanyCode}).");
                if (!string.IsNullOrWhiteSpace(current.CompanyId) && string.IsNullOrWhiteSpace(settings.CompanyId))
                {
                    settings.CompanyId = current.CompanyId; settings.CompanyCode = current.CompanyCode;
                    settings.CompanyName = current.CompanyName; settings.CompanyRegistrationMode = current.CompanyRegistrationMode;
                    settings.CompanyRegisteredAtUtc = current.CompanyRegisteredAtUtc;
                }
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
            }

            SqliteConnection.ClearAllPools();
            if (File.Exists(AppPaths.DatabasePath))
            {
                using var connection = OpenDatabase(AppPaths.DatabasePath, SqliteOpenMode.ReadWrite);
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                using var reader = command.ExecuteReader();
                if (reader.Read() && reader.GetInt32(0) != 0)
                    throw new IOException("Die Datenbank wird noch verwendet. Bitte andere Zugriffe beenden und erneut versuchen.");
            }
            SqliteConnection.ClearAllPools();
            var oldDatabase = Path.Combine(temp, "previous.db");
            var oldSettings = Path.Combine(temp, "previous-settings.json");
            var hadDatabase = File.Exists(AppPaths.DatabasePath);
            var hadSettings = File.Exists(AppPaths.SettingsPath);
            var databaseReplaced = false;
            var settingsReplaced = false;
            try
            {
                if (hadDatabase) File.Replace(database, AppPaths.DatabasePath, oldDatabase);
                else File.Move(database, AppPaths.DatabasePath);
                databaseReplaced = true;
                if (File.Exists(settingsPath))
                {
                    if (hadSettings) File.Replace(settingsPath, AppPaths.SettingsPath, oldSettings);
                    else File.Move(settingsPath, AppPaths.SettingsPath);
                    settingsReplaced = true;
                }
            }
            catch (Exception restoreError)
            {
                try
                {
                    if (settingsReplaced)
                    {
                        if (hadSettings) File.Move(oldSettings, AppPaths.SettingsPath, true);
                        else File.Delete(AppPaths.SettingsPath);
                    }
                    if (databaseReplaced)
                    {
                        if (hadDatabase) File.Move(oldDatabase, AppPaths.DatabasePath, true);
                        else File.Delete(AppPaths.DatabasePath);
                    }
                }
                catch (Exception rollbackError)
                {
                    preserveRecoveryFiles = true;
                    SessionService.InvalidateAfterRestore();
                    throw new IOException($"Wiederherstellung abgebrochen. Sicherheitskopien liegen unter {temp}. Bitte Support kontaktieren.",
                        new AggregateException(restoreError, rollbackError));
                }
                throw;
            }
            SessionService.InvalidateAfterRestore();
        }
        finally { if (!preserveRecoveryFiles) TryDeleteDirectory(temp); }
    }

    private static SqliteConnection OpenDatabase(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = mode, Pooling = false }.ConnectionString);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static void CreateDatabaseSnapshot(string destination)
    {
        using var source = OpenDatabase(AppPaths.DatabasePath, SqliteOpenMode.ReadWrite);
        using var target = OpenDatabase(destination, SqliteOpenMode.ReadWriteCreate);
        source.BackupDatabase(target);
    }

    private static void ValidateDatabase(string path)
    {
        using var connection = OpenDatabase(path, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(Convert.ToString(command.ExecuteScalar()), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Die Datenbank im Backup ist beschädigt.");
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Employees','ProductionOrders');";
        if (Convert.ToInt32(command.ExecuteScalar()) != 2)
            throw new InvalidDataException("Die Datei ist kein gültiges SolutionCompakt-Backup.");
        command.CommandText = "PRAGMA foreign_key_check;";
        using var reader = command.ExecuteReader();
        if (reader.Read()) throw new InvalidDataException("Das Backup enthält ungültige Datenverknüpfungen.");
    }

    private static void PruneAutomaticBackups(string directory, int count)
    {
        count = Math.Clamp(count, 1, 100);
        foreach (var file in new DirectoryInfo(directory).GetFiles("*.kpibackup")
            .Where(x => x.Name.StartsWith("SolutionCompakt-auto-", StringComparison.OrdinalIgnoreCase) ||
                        x.Name.StartsWith("OpsCompact-auto-", StringComparison.OrdinalIgnoreCase) ||
                        x.Name.StartsWith("KPI-rai-auto-", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.LastWriteTimeUtc).Skip(count))
            TryDeleteFile(file.FullName);
    }

    private static void TryDeleteFile(string path) { try { File.Delete(path); } catch { } }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
}

public sealed class BackupManifest
{
    public string Product { get; set; } = "";
    public DateTime CreatedAtLocal { get; set; }
    public string AppVersion { get; set; } = "";
    public string DatabaseFile { get; set; } = "";
    public string SettingsFile { get; set; } = "";
    public string CompanyId { get; set; } = "";
    public string CompanyCode { get; set; } = "";
}

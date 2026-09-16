using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Produktionsplanung.App.Services;

public static class StartupHealthService
{
    private const long CriticalFreeBytes = 25L * 1024 * 1024;
    private const long WarningFreeBytes = 250L * 1024 * 1024;
    private static FileStream? instanceLock;

    public static bool TryPrepare(out string errorMessage, out string warningMessage)
    {
        errorMessage = string.Empty;
        warningMessage = string.Empty;

        try
        {
            AppPaths.EnsureDirectories();
            VerifyWritableStorage();
            AcquireInstanceLock();

            var available = GetAvailableFreeSpace();
            if (available >= 0 && available < CriticalFreeBytes)
            {
                errorMessage = $"Zu wenig freier Speicher am KPI-rai-Datenspeicher ({FormatBytes(available)} frei). Mindestens 25 MB werden benötigt.";
                Release();
                return false;
            }

            if (available >= 0 && available < WarningFreeBytes)
                warningMessage = $"Hinweis: Am KPI-rai-Datenspeicher sind nur noch {FormatBytes(available)} frei. Bitte Backup erstellen und Speicherplatz prüfen.";

            if (File.Exists(AppPaths.DatabasePath))
            {
                var integrity = CheckDatabaseIntegrity();
                if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = $"Die lokale KPI-rai-Datenbank hat die Integritätsprüfung nicht bestanden: {integrity}. Bitte ein Backup wiederherstellen, bevor weitergearbeitet wird.";
                    Release();
                    return false;
                }
            }

            return true;
        }
        catch (IOException ex)
        {
            errorMessage = $"Der KPI-rai-Datenspeicher ist bereits durch eine andere Instanz belegt oder nicht beschreibbar. Schliessen Sie andere KPI-rai-Fenster und prüfen Sie den Datenträger.\n\n{ex.Message}";
            Release();
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"KPI-rai konnte den Datenspeicher nicht sicher initialisieren.\n\n{ex.Message}";
            Release();
            return false;
        }
    }

    public static void Release()
    {
        try
        {
            instanceLock?.Dispose();
            instanceLock = null;
            if (File.Exists(LockPath))
                File.Delete(LockPath);
        }
        catch
        {
            // Lock cleanup must never block application shutdown.
        }
    }

    private static string LockPath => Path.Combine(AppPaths.RootDirectory, ".kpi-rai.lock");

    private static void VerifyWritableStorage()
    {
        var testPath = Path.Combine(AppPaths.RootDirectory, $".write-test-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(testPath, "KPI-rai", Encoding.UTF8);
        File.Delete(testPath);
    }

    private static void AcquireInstanceLock()
    {
        instanceLock?.Dispose();
        instanceLock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        instanceLock.SetLength(0);
        using var writer = new StreamWriter(instanceLock, Encoding.UTF8, 1024, leaveOpen: true);
        writer.WriteLine($"PID={Environment.ProcessId}");
        writer.WriteLine($"StartedUtc={DateTime.UtcNow:O}");
        writer.WriteLine($"Mode={AppPaths.StorageModeText}");
        writer.Flush();
        instanceLock.Flush(flushToDisk: true);
    }

    private static string CheckDatabaseIntegrity()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = AppPaths.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly
        };

        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        return Convert.ToString(command.ExecuteScalar()) ?? "unbekannter Datenbankfehler";
    }

    private static long GetAvailableFreeSpace()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(AppPaths.RootDirectory));
            if (string.IsNullOrWhiteSpace(root)) return -1;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return -1;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024 * 1024):0.0} GB";
        return $"{bytes / (1024d * 1024):0} MB";
    }
}

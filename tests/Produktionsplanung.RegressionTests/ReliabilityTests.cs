using System.IO;
using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;
using Produktionsplanung.App.Services;

internal static partial class Program
{
    private static void BackupOverwriteFailurePreservesArchive()
    {
        var settings = AppSettingsService.Load();
        var path = BackupService.CreateBackup(Path.Combine(AppPaths.BackupsDirectory, "keep.kpibackup"), settings);
        var before = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = false;
            try { BackupService.CreateBackup(path, settings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            Check(failed, "Replacing a locked backup should fail");
        }
        Check(before.SequenceEqual(File.ReadAllBytes(path)), "Existing backup was lost or changed");
    }

    private static void RestoreRollbackPreservesDatabaseAndSession()
    {
        var settings = AppSettingsService.Load();
        var path = BackupService.CreateBackup(Path.Combine(AppPaths.BackupsDirectory, "restore.kpibackup"), settings);
        int employeeId;
        using (var db = new AppDbContext())
        {
            var employee = db.Employees.First();
            employeeId = employee.Id;
            employee.LastName = "Must survive failed restore";
            db.SaveChanges();
        }
        SessionService.SignIn(new UserAccount { Role = UserRoles.Administrator });
        var beforeSettings = File.ReadAllBytes(AppPaths.SettingsPath);
        SqliteConnection.ClearAllPools();
        using (var locked = new FileStream(AppPaths.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = false;
            try { BackupService.RestoreBackup(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            Check(failed, "Locked settings should abort restore");
        }
        using (var db = new AppDbContext())
            Check(db.Employees.Single(x => x.Id == employeeId).LastName == "Must survive failed restore",
                "Database was not rolled back after settings replacement failed");
        Check(beforeSettings.SequenceEqual(File.ReadAllBytes(AppPaths.SettingsPath)), "Settings changed on failed restore");
        Check(SessionService.IsAuthenticated && !SessionService.RequiresRestart, "Successful rollback invalidated session");
    }

    private static void CorruptBackupPreservesCurrentData()
    {
        var temp = Path.Combine(AppPaths.RootDirectory, "invalid-backup");
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "produktionsplanung.db"), "This is not SQLite");
        var archive = Path.Combine(AppPaths.RootDirectory, "corrupt.kpibackup");
        ZipFile.CreateFromDirectory(temp, archive);
        SessionService.SignIn(new UserAccount { Role = UserRoles.Administrator });
        var failed = false;
        try { BackupService.RestoreBackup(archive); }
        catch (SqliteException) { failed = true; }
        Check(failed && SessionService.IsAuthenticated && !SessionService.RequiresRestart, "Corrupt backup was accepted");
        using var db = new AppDbContext();
        Check(db.Employees.Any(), "Current data was lost");
    }

    private static void SettingsWriteFailurePreservesOriginal()
    {
        var settings = AppSettingsService.Load();
        AppSettingsService.Save(settings);
        var before = File.ReadAllBytes(AppPaths.SettingsPath);
        using (var locked = new FileStream(AppPaths.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            settings.CompanyName = "Must not be saved";
            var failed = false;
            try { AppSettingsService.Save(settings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            Check(failed, "Locked settings should reject write");
        }
        Check(before.SequenceEqual(File.ReadAllBytes(AppPaths.SettingsPath)), "Failed save truncated settings");
    }

    private static void UpdateRequiresHttpsAndHash()
    {
        var hash = new string('a', 64);
        Check(UpdateService.IsValidDownload("https://example.com/setup.zip", hash), "Valid update rejected");
        Check(!UpdateService.IsValidDownload("http://example.com/setup.zip", hash), "HTTP accepted");
        Check(!UpdateService.IsValidDownload("https://example.com/setup.zip", ""), "Missing checksum accepted");
        Check(!UpdateService.IsValidDownload("https://example.com/setup.zip", new string('z', 64)), "Invalid checksum accepted");
        Check(!UpdateService.IsValidDownload("file:///tmp/setup.zip", hash), "Local URL accepted");
    }

    private static void RuntimeLicensePausePreservesWindow()
    {
        Planner();
        var window = new MainWindow();
        var content = window.Content;
        try
        {
            window.ApplyRuntimeLicenseGate(new(false, false, 0, "suspended", null, "Blocked"), false);
            Check(!window.IsEnabled && ReferenceEquals(content, window.Content), "License pause destroyed content or did not block");
            window.ApplyRuntimeLicenseGate(new(true, false, 0, "active", DateTime.UtcNow.AddDays(1), "OK"), false);
            Check(window.IsEnabled && ReferenceEquals(content, window.Content), "License recovery did not restore editor");
        }
        finally
        {
            window.ApplyRuntimeLicenseGate(new(true, false, 0, "active", DateTime.UtcNow.AddDays(1), "OK"), false);
            window.Close();
        }
    }
}

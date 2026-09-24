using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string companyName = "SolutionCompakt";
    [ObservableProperty] private string companyCode = string.Empty;
    [ObservableProperty] private string companyId = string.Empty;
    [ObservableProperty] private string companyRegistrationMode = string.Empty;
    [ObservableProperty] private string siteName = string.Empty;
    [ObservableProperty] private string defaultBackupDirectory = AppPaths.BackupsDirectory;
    [ObservableProperty] private string defaultExportDirectory = AppPaths.ExportsDirectory;
    [ObservableProperty] private bool autoBackupOnExit = true;
    [ObservableProperty] private int backupRetentionCount = 10;
    [ObservableProperty] private string csvDelimiter = ";";
    [ObservableProperty] private bool includeUtf8Bom = true;
    [ObservableProperty] private bool autoLockEnabled = true;
    [ObservableProperty] private int autoLockMinutes = 30;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string recoveryCodeStatus = "Nicht eingerichtet";
    [ObservableProperty] private bool showContextHints = true;

    public IReadOnlyList<int> AutoLockOptions { get; } = new[] { 15, 30, 60 };
    public string DatabasePath => AppPaths.DatabasePath;
    public string SettingsPath => AppPaths.SettingsPath;

    public SettingsViewModel() => LoadSettings();

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            var settings = BuildSettings();
            AppSettingsService.Save(settings);
            ApplySettings(settings);
            RefreshRecoveryCodeStatus();
            StatusMessage = "Einstellungen gespeichert.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Einstellungen konnten nicht gespeichert werden: {ex.Message}";
        }
    }

    partial void OnShowContextHintsChanged(bool value)
    {
        AppSettingsService.UpdateCurrentUserPreferences(preferences => preferences.ShowContextHints = value);
        if (Application.Current.MainWindow is Produktionsplanung.App.MainWindow mainWindow)
            mainWindow.RefreshContextHelpPreference();
    }

    [RelayCommand]
    private void StartGuidedTour()
    {
        if (Application.Current.MainWindow is Produktionsplanung.App.MainWindow mainWindow)
            mainWindow.StartGuidedTour(fromBeginning: true);
    }

    [RelayCommand]
    private void ResetUiPreferences()
    {
        AppSettingsService.UpdateCurrentUserPreferences(preferences =>
        {
            preferences.SidebarCollapsed = false;
            preferences.PlanningGroupCollapsed = false;
            preferences.ProductionGroupCollapsed = false;
            preferences.MasterDataGroupCollapsed = false;
            preferences.SystemGroupCollapsed = false;
            preferences.CalendarSelectedViewIndex = 1;
            preferences.CalendarSearchText = string.Empty;
            preferences.CalendarShowAssignments = true;
            preferences.CalendarShowOrders = true;
            preferences.CalendarShowAbsences = true;
            preferences.CalendarShowOperatingCalendar = true;
            preferences.CalendarShowWeekends = true;
            preferences.ShowContextHints = true;
        });
        StatusMessage = "Persönliche Benutzeroberfläche zurückgesetzt. Die Navigation wird beim nächsten Anmelden vollständig mit den Standardwerten geladen.";
    }

    [RelayCommand]
    private void ManageRecoveryCode()
    {
        if (!SessionService.IsAdministrator)
        {
            StatusMessage = "Nur ein Administrator darf den Recovery-Code verwalten.";
            return;
        }

        var dialog = new Produktionsplanung.App.RecoveryCodeManagementWindow
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            RefreshRecoveryCodeStatus();
            StatusMessage = "Recovery-Code wurde erneuert. Der bisherige Code ist nicht mehr gültig.";
        }
    }

    [RelayCommand]
    private void BrowseBackupDirectory()
    {
        var current = AppSettingsService.ResolveStoragePath(DefaultBackupDirectory, "Backups");
        var dialog = new OpenFolderDialog
        {
            Title = "Standardordner für SolutionCompakt-Backups auswählen",
            InitialDirectory = Directory.Exists(current) ? current : AppPaths.BackupsDirectory
        };
        if (dialog.ShowDialog() == true)
            DefaultBackupDirectory = AppSettingsService.ToStoredStoragePath(dialog.FolderName, "Backups");
    }

    [RelayCommand]
    private void BrowseExportDirectory()
    {
        var current = AppSettingsService.ResolveStoragePath(DefaultExportDirectory, "Exports");
        var dialog = new OpenFolderDialog
        {
            Title = "Standardordner für SolutionCompakt-Exporte auswählen",
            InitialDirectory = Directory.Exists(current) ? current : AppPaths.ExportsDirectory
        };
        if (dialog.ShowDialog() == true)
            DefaultExportDirectory = AppSettingsService.ToStoredStoragePath(dialog.FolderName, "Exports");
    }

    [RelayCommand]
    private void CreateBackup()
    {
        try
        {
            var settings = BuildSettings();
            AppSettingsService.Save(settings);
            var dir = AppSettingsService.ResolveStoragePath(settings.DefaultBackupDirectory, "Backups");
            Directory.CreateDirectory(dir);
            var dialog = new SaveFileDialog
            {
                Title = "SolutionCompakt-Backup speichern",
                Filter = "SolutionCompakt Backup (*.kpibackup)|*.kpibackup",
                DefaultExt = ".kpibackup",
                AddExtension = true,
                InitialDirectory = dir,
                FileName = $"SolutionCompakt-{DateTime.Now:yyyyMMdd-HHmmss}.kpibackup"
            };
            if (dialog.ShowDialog() != true)
                return;

            var path = BackupService.CreateBackup(dialog.FileName, settings);
            ApplySettings(settings);
            StatusMessage = $"Backup erfolgreich erstellt: {path} · zuletzt erfolgreich {settings.LastSuccessfulBackupAtLocal:g}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Backup fehlgeschlagen: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RestoreBackup()
    {
        var dir = AppSettingsService.ResolveStoragePath(DefaultBackupDirectory, "Backups");
        var dialog = new OpenFileDialog
        {
            Title = "SolutionCompakt-Backup wiederherstellen",
            Filter = "SolutionCompakt Backup (*.kpibackup)|*.kpibackup|Alle Dateien (*.*)|*.*",
            InitialDirectory = Directory.Exists(dir) ? dir : AppPaths.BackupsDirectory
        };
        if (dialog.ShowDialog() != true)
            return;

        if (MessageBox.Show(
                "Beim Wiederherstellen wird die aktuelle lokale Datenbank ersetzt. SolutionCompakt erstellt davor automatisch ein Sicherheitsbackup. Fortfahren?",
                "Backup wiederherstellen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            var settings = BuildSettings();
            AppSettingsService.Save(settings);
            dir = AppSettingsService.ResolveStoragePath(settings.DefaultBackupDirectory, "Backups");
            Directory.CreateDirectory(dir);
            var safety = Path.Combine(dir, $"SolutionCompakt-vor-Restore-{DateTime.Now:yyyyMMdd-HHmmss}.kpibackup");
            BackupService.CreateBackup(safety, settings);
            BackupService.RestoreBackup(dialog.FileName);
            StatusMessage = $"Backup wiederhergestellt. Sicherheitsbackup: {safety}. Bitte SolutionCompakt jetzt neu starten.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Wiederherstellung fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            if (SessionService.RequiresRestart)
            {
                Application.Current.MainWindow.IsEnabled = false;
                MessageBox.Show(
                    StatusMessage + "\nDie Anwendung wird jetzt geschlossen. Bitte erneut starten und anmelden.",
                    "SolutionCompakt wiederherstellen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Application.Current.Shutdown();
            }
        }
    }

    [RelayCommand]
    private void RestartApplication()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                StatusMessage = "Der Anwendungspfad konnte nicht ermittelt werden.";
                return;
            }

            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Neustart nicht möglich: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ExportAll()
    {
        try
        {
            var settings = BuildSettings();
            AppSettingsService.Save(settings);
            var dir = AppSettingsService.ResolveStoragePath(settings.DefaultExportDirectory, "Exports");
            Directory.CreateDirectory(dir);
            var dialog = new OpenFolderDialog
            {
                Title = "Zielordner für SolutionCompakt-CSV-Export auswählen",
                InitialDirectory = dir
            };
            if (dialog.ShowDialog() != true)
                return;

            DefaultExportDirectory = AppSettingsService.ToStoredStoragePath(dialog.FolderName, "Exports");
            settings.DefaultExportDirectory = DefaultExportDirectory;
            AppSettingsService.Save(settings);
            var export = CsvExportService.ExportAll(dialog.FolderName, settings);
            StatusMessage = $"CSV-Export erfolgreich erstellt: {export}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export fehlgeschlagen: {ex.Message}";
        }
    }

    [RelayCommand] private void OpenDataDirectory() => OpenDirectory(AppPaths.RootDirectory);
    [RelayCommand] private void OpenBackupDirectory() => OpenDirectory(AppSettingsService.ResolveStoragePath(DefaultBackupDirectory, "Backups"));
    [RelayCommand] private void OpenExportDirectory() => OpenDirectory(AppSettingsService.ResolveStoragePath(DefaultExportDirectory, "Exports"));

    private void LoadSettings()
    {
        var settings = AppSettingsService.Load();
        ApplySettings(settings);
        RefreshRecoveryCodeStatus(settings);
        ShowContextHints = AppSettingsService.LoadCurrentUserPreferences().ShowContextHints;
        StatusMessage = settings.LastSuccessfulBackupAtLocal.HasValue
            ? $"Letztes erfolgreiches Backup: {settings.LastSuccessfulBackupAtLocal:g} · {settings.LastSuccessfulBackupPath}"
            : "Noch kein erfolgreiches Backup protokolliert.";
    }

    private AppSettings BuildSettings()
    {
        // Vorhandene Einstellungen weiterverwenden, damit Security- und Benutzerprofile
        // beim Speichern dieser Seite nicht verloren gehen.
        var settings = AppSettingsService.Load();
        settings.CompanyName = CompanyName;
        settings.SiteName = SiteName;
        settings.DefaultBackupDirectory = DefaultBackupDirectory;
        settings.DefaultExportDirectory = DefaultExportDirectory;
        settings.AutoBackupOnExit = AutoBackupOnExit;
        settings.BackupRetentionCount = BackupRetentionCount;
        settings.CsvDelimiter = CsvDelimiter;
        settings.IncludeUtf8Bom = IncludeUtf8Bom;
        settings.AutoLockEnabled = AutoLockEnabled;
        settings.AutoLockMinutes = AutoLockMinutes;
        return settings;
    }

    private void ApplySettings(AppSettings settings)
    {
        CompanyName = settings.CompanyName;
        CompanyCode = settings.CompanyCode;
        CompanyId = settings.CompanyId;
        CompanyRegistrationMode = settings.CompanyRegistrationMode;
        SiteName = settings.SiteName;
        DefaultBackupDirectory = settings.DefaultBackupDirectory;
        DefaultExportDirectory = settings.DefaultExportDirectory;
        AutoBackupOnExit = settings.AutoBackupOnExit;
        BackupRetentionCount = settings.BackupRetentionCount;
        CsvDelimiter = settings.CsvDelimiter;
        IncludeUtf8Bom = settings.IncludeUtf8Bom;
        AutoLockEnabled = settings.AutoLockEnabled;
        AutoLockMinutes = AutoLockOptions.Contains(settings.AutoLockMinutes) ? settings.AutoLockMinutes : 30;
    }

    private void RefreshRecoveryCodeStatus() => RefreshRecoveryCodeStatus(AppSettingsService.Load());

    private void RefreshRecoveryCodeStatus(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.RecoveryCodeHash) || string.IsNullOrWhiteSpace(settings.RecoveryCodeSalt))
        {
            RecoveryCodeStatus = "Noch kein Recovery-Code eingerichtet.";
            return;
        }

        RecoveryCodeStatus = settings.RecoveryCodeCreatedAtUtc.HasValue
            ? $"Eingerichtet · zuletzt erneuert {settings.RecoveryCodeCreatedAtUtc.Value.ToLocalTime():g}."
            : "Recovery-Code ist eingerichtet.";
    }

    private void OpenDirectory(string dir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dir))
                return;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ordner konnte nicht geöffnet werden: {ex.Message}";
        }
    }
}

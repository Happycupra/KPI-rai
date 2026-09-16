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
    [ObservableProperty] private string companyName = "KPI-rai";
    [ObservableProperty] private string siteName = string.Empty;
    [ObservableProperty] private string defaultBackupDirectory = AppPaths.BackupsDirectory;
    [ObservableProperty] private string defaultExportDirectory = AppPaths.ExportsDirectory;
    [ObservableProperty] private bool autoBackupOnExit = true;
    [ObservableProperty] private int backupRetentionCount = 10;
    [ObservableProperty] private string csvDelimiter = ";";
    [ObservableProperty] private bool includeUtf8Bom = true;
    [ObservableProperty] private string statusMessage = string.Empty;

    public string DatabasePath => AppPaths.DatabasePath;
    public string SettingsPath => AppPaths.SettingsPath;

    public SettingsViewModel()
    {
        LoadSettings();
    }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            var settings = BuildSettings();
            AppSettingsService.Save(settings);
            ApplySettings(settings);
            StatusMessage = "Einstellungen gespeichert.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Einstellungen konnten nicht gespeichert werden: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseBackupDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Standardordner für KPI-rai-Backups auswählen",
            InitialDirectory = Directory.Exists(DefaultBackupDirectory) ? DefaultBackupDirectory : AppPaths.BackupsDirectory
        };

        if (dialog.ShowDialog() == true)
            DefaultBackupDirectory = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseExportDirectory()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Standardordner für KPI-rai-Exporte auswählen",
            InitialDirectory = Directory.Exists(DefaultExportDirectory) ? DefaultExportDirectory : AppPaths.ExportsDirectory
        };

        if (dialog.ShowDialog() == true)
            DefaultExportDirectory = dialog.FolderName;
    }

    [RelayCommand]
    private void CreateBackup()
    {
        try
        {
            var settings = BuildSettings();
            AppSettingsService.Save(settings);
            Directory.CreateDirectory(settings.DefaultBackupDirectory);

            var dialog = new SaveFileDialog
            {
                Title = "KPI-rai-Backup speichern",
                Filter = "KPI-rai Backup (*.kpibackup)|*.kpibackup",
                DefaultExt = ".kpibackup",
                AddExtension = true,
                InitialDirectory = settings.DefaultBackupDirectory,
                FileName = $"KPI-rai-{DateTime.Now:yyyyMMdd-HHmmss}.kpibackup"
            };

            if (dialog.ShowDialog() != true)
                return;

            var path = BackupService.CreateBackup(dialog.FileName, settings);
            StatusMessage = $"Backup erfolgreich erstellt: {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Backup fehlgeschlagen: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RestoreBackup()
    {
        var dialog = new OpenFileDialog
        {
            Title = "KPI-rai-Backup wiederherstellen",
            Filter = "KPI-rai Backup (*.kpibackup)|*.kpibackup|Alle Dateien (*.*)|*.*",
            InitialDirectory = Directory.Exists(DefaultBackupDirectory) ? DefaultBackupDirectory : AppPaths.BackupsDirectory
        };

        if (dialog.ShowDialog() != true)
            return;

        var confirmation = MessageBox.Show(
            "Beim Wiederherstellen wird die aktuelle lokale Datenbank ersetzt. KPI-rai erstellt davor automatisch ein Sicherheitsbackup. Fortfahren?",
            "Backup wiederherstellen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        try
        {
            var settings = BuildSettings();
            AppSettingsService.Save(settings);
            Directory.CreateDirectory(settings.DefaultBackupDirectory);

            var safetyPath = Path.Combine(
                settings.DefaultBackupDirectory,
                $"KPI-rai-vor-Restore-{DateTime.Now:yyyyMMdd-HHmmss}.kpibackup");
            BackupService.CreateBackup(safetyPath, settings);

            BackupService.RestoreBackup(dialog.FileName);
            StatusMessage = $"Backup wiederhergestellt. Sicherheitsbackup: {safetyPath}. Bitte KPI-rai jetzt neu starten.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Wiederherstellung fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            if (SessionService.RequiresRestart)
            {
                // No further navigation or edits with accounts from the old database.
                Application.Current.MainWindow.IsEnabled = false;
                MessageBox.Show(StatusMessage + "\nDie Anwendung wird jetzt geschlossen. Bitte erneut starten und anmelden.",
                    "KPI-rai wiederherstellen", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Shutdown();
            }
        }
    }

    [RelayCommand]
    private void RestartApplication()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                StatusMessage = "Der Anwendungspfad konnte nicht ermittelt werden. Bitte KPI-rai manuell neu starten.";
                return;
            }

            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
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
            Directory.CreateDirectory(settings.DefaultExportDirectory);

            var dialog = new OpenFolderDialog
            {
                Title = "Zielordner für KPI-rai-CSV-Export auswählen",
                InitialDirectory = settings.DefaultExportDirectory
            };

            if (dialog.ShowDialog() != true)
                return;

            DefaultExportDirectory = dialog.FolderName;
            settings.DefaultExportDirectory = dialog.FolderName;
            AppSettingsService.Save(settings);

            var exportDirectory = CsvExportService.ExportAll(dialog.FolderName, settings);
            StatusMessage = $"CSV-Export erfolgreich erstellt: {exportDirectory}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export fehlgeschlagen: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenDataDirectory() => OpenDirectory(AppPaths.RootDirectory);

    [RelayCommand]
    private void OpenBackupDirectory() => OpenDirectory(DefaultBackupDirectory);

    [RelayCommand]
    private void OpenExportDirectory() => OpenDirectory(DefaultExportDirectory);

    private void LoadSettings()
    {
        var settings = AppSettingsService.Load();
        ApplySettings(settings);
        StatusMessage = string.Empty;
    }

    private AppSettings BuildSettings() => new()
    {
        CompanyName = CompanyName,
        SiteName = SiteName,
        DefaultBackupDirectory = DefaultBackupDirectory,
        DefaultExportDirectory = DefaultExportDirectory,
        AutoBackupOnExit = AutoBackupOnExit,
        BackupRetentionCount = BackupRetentionCount,
        CsvDelimiter = CsvDelimiter,
        IncludeUtf8Bom = IncludeUtf8Bom
    };

    private void ApplySettings(AppSettings settings)
    {
        CompanyName = settings.CompanyName;
        SiteName = settings.SiteName;
        DefaultBackupDirectory = settings.DefaultBackupDirectory;
        DefaultExportDirectory = settings.DefaultExportDirectory;
        AutoBackupOnExit = settings.AutoBackupOnExit;
        BackupRetentionCount = settings.BackupRetentionCount;
        CsvDelimiter = settings.CsvDelimiter;
        IncludeUtf8Bom = settings.IncludeUtf8Bom;
    }

    private void OpenDirectory(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory))
                return;

            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ordner konnte nicht geöffnet werden: {ex.Message}";
        }
    }
}

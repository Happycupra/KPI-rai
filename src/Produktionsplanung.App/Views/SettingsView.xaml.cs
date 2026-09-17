using System.ComponentModel;
using System.Windows.Controls;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class SettingsView : UserControl, IUnsavedChangesAware
{
    private SettingsViewModel viewModel;
    private string baseline = string.Empty;

    public SettingsView()
    {
        InitializeComponent();
        viewModel = CreateViewModel();
        DataContext = viewModel;
        CaptureBaseline();
    }

    public bool HasUnsavedChanges => baseline != BuildSnapshot();
    public string UnsavedChangesDescription => "Systemeinstellungen";

    public bool TrySaveChanges()
    {
        viewModel.SaveSettingsCommand.Execute(null);
        return !HasUnsavedChanges;
    }

    public void DiscardChanges()
    {
        viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        viewModel = CreateViewModel();
        DataContext = viewModel;
        CaptureBaseline();
    }

    private SettingsViewModel CreateViewModel()
    {
        var vm = new SettingsViewModel();
        vm.PropertyChanged += ViewModel_PropertyChanged;
        return vm;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.StatusMessage) &&
            viewModel.StatusMessage == "Einstellungen gespeichert.")
            CaptureBaseline();
    }

    private void CaptureBaseline() => baseline = BuildSnapshot();

    private string BuildSnapshot() => string.Join("\u001f",
        viewModel.CompanyName,
        viewModel.SiteName,
        viewModel.DefaultBackupDirectory,
        viewModel.DefaultExportDirectory,
        viewModel.AutoBackupOnExit,
        viewModel.BackupRetentionCount,
        viewModel.CsvDelimiter,
        viewModel.IncludeUtf8Bom,
        viewModel.AutoLockEnabled,
        viewModel.AutoLockMinutes);
}

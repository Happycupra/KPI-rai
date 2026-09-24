using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class WeekPlanningView : UserControl
{
    public WeekPlanningView()
    {
        InitializeComponent();
        var viewModel = new WeekPlanningViewModel();
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = viewModel;
        PreviewMouseLeftButtonUp += EmployeeName_PreviewMouseLeftButtonUp;
        PreviewMouseMove += EmployeeName_PreviewMouseMove;
        MouseLeave += (_, _) => Cursor = Cursors.Arrow;
        viewModel.RefreshProductionOrderCoverage();
        RefreshOnlineWeekPlanStatus();
    }

    private void EmployeeName_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        Cursor = TryGetEmployeeRow(e.OriginalSource as DependencyObject, out _)
            ? Cursors.Hand
            : Cursors.Arrow;
    }

    private void EmployeeName_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!TryGetEmployeeRow(e.OriginalSource as DependencyObject, out var row))
            return;

        if (Application.Current.MainWindow is MainWindow mainWindow && DataContext is WeekPlanningViewModel viewModel)
        {
            mainWindow.OpenEmployeeQuickCard(row.EmployeeId, viewModel.WeekStart);
            e.Handled = true;
        }
    }

    private void ExportWeeklyPlanPdf_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WeekPlanningViewModel viewModel)
            return;

        try
        {
            var settings = AppSettingsService.Load();
            var exportDirectory = string.IsNullOrWhiteSpace(settings.DefaultExportDirectory)
                ? AppPaths.ExportsDirectory
                : settings.DefaultExportDirectory;
            Directory.CreateDirectory(exportDirectory);

            var dialog = new SaveFileDialog
            {
                Title = "Wochenplan als PDF exportieren",
                Filter = "PDF-Dokument (*.pdf)|*.pdf",
                AddExtension = true,
                DefaultExt = ".pdf",
                FileName = WeeklyPlanPdfService.BuildFileName(viewModel.WeekStart),
                InitialDirectory = exportDirectory
            };

            if (dialog.ShowDialog() != true)
                return;

            var includeWeekends = ExportWeekendsCheckBox.IsChecked == true;
            var result = WeeklyPlanPdfService.Export(viewModel.WeekStart, dialog.FileName, includeWeekends);
            viewModel.StatusMessage = $"PDF-Wochenplan erstellt: {result.PageCount} Seite(n), {result.ProductionShiftCount} Produktionsschichten · {(includeWeekends ? "mit Wochenende" : "Mo–Fr")}.";

            Process.Start(new ProcessStartInfo(result.FilePath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage = $"PDF-Export fehlgeschlagen: {ex.Message}";
            MessageBox.Show(ex.Message, "PDF-Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PrepareOnlineWeekPlan_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WeekPlanningViewModel viewModel)
            return;

        try
        {
            var result = OnlineWeekPlanService.PreparePackage(viewModel.WeekStart);
            viewModel.StatusMessage = $"Online-Paket {result.WeekId} vorbereitet: {result.AssignmentCount} Personaleinsätze, {result.ProductionSlotCount} Produktionsschichten.";
            RefreshOnlineWeekPlanStatus();
            Process.Start(new ProcessStartInfo(Path.GetDirectoryName(result.FilePath)!)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage = "Online-Paket konnte nicht vorbereitet werden: " + ex.Message;
            MessageBox.Show(ex.Message, "Online-Wochenplan", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ConfigureFirebase_Click(object sender, RoutedEventArgs e)
    {
        if (!SessionService.IsAdministrator)
            return;

        var dialog = new FirebaseWeekPlanConfigWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
        RefreshOnlineWeekPlanStatus();
    }

    private void OpenOnlineWeekPlan_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppSettingsService.Load();
        if (!Uri.TryCreate(settings.FirebaseHostingUrl, UriKind.Absolute, out var uri))
        {
            MessageBox.Show("Die Firebase Hosting URL ist noch nicht konfiguriert.",
                "Online-Wochenplan", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }

    private void RefreshOnlineWeekPlanStatus()
    {
        var settings = AppSettingsService.Load();
        var prepared = settings.LastOnlineWeekPreparedAtUtc.HasValue
            ? $" · letztes Paket {settings.LastOnlineWeekPreparedId} am {settings.LastOnlineWeekPreparedAtUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm}"
            : " · noch kein Paket vorbereitet";

        OnlineWeekPlanStatusText.Text = OnlineWeekPlanService.FirebaseStatusText(settings) + prepared;
        PrepareOnlineWeekPlanButton.IsEnabled = SessionService.IsAdministrator;
        ConfigureFirebaseButton.IsEnabled = SessionService.IsAdministrator;
        OpenOnlineWeekPlanButton.IsEnabled = Uri.TryCreate(settings.FirebaseHostingUrl, UriKind.Absolute, out _);
    }

    private static bool TryGetEmployeeRow(DependencyObject? source, out EmployeeWeekRow row)
    {
        row = null!;
        var textBlock = FindAncestor<TextBlock>(source);
        if (textBlock?.DataContext is not EmployeeWeekRow candidate)
            return false;
        if (!string.Equals(textBlock.Text, candidate.EmployeeName, StringComparison.Ordinal))
            return false;
        row = candidate;
        return true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not WeekPlanningViewModel viewModel) return;
        if (e.PropertyName is nameof(WeekPlanningViewModel.WeekStart) or nameof(WeekPlanningViewModel.StatusMessage))
            viewModel.RefreshProductionOrderCoverage();
    }
}

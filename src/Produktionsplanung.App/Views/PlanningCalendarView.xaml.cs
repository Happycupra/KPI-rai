using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class PlanningCalendarView : UserControl
{
    private readonly PlanningCalendarViewModel viewModel;
    private bool restoringPreferences;
    private bool leftPanelCollapsed;
    private bool rightPanelCollapsed;

    public PlanningCalendarView()
    {
        InitializeComponent();
        viewModel = new PlanningCalendarViewModel();
        restoringPreferences = true;
        ApplySavedPreferences();
        restoringPreferences = false;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            if (ActualWidth < 1150)
                rightPanelCollapsed = true;
            ApplyPanelLayout();
        };
    }

    private void ApplySavedPreferences()
    {
        var preferences = AppSettingsService.LoadCurrentUserPreferences();
        viewModel.SelectedViewIndex = preferences.CalendarSelectedViewIndex;
        viewModel.SearchText = preferences.CalendarSearchText;
        viewModel.ShowAssignments = preferences.CalendarShowAssignments;
        viewModel.ShowOrders = preferences.CalendarShowOrders;
        viewModel.ShowAbsences = preferences.CalendarShowAbsences;
        viewModel.ShowOperatingCalendar = preferences.CalendarShowOperatingCalendar;
        viewModel.ShowWeekends = preferences.CalendarShowWeekends;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (restoringPreferences || e.PropertyName is null)
            return;

        if (e.PropertyName is not (
            nameof(PlanningCalendarViewModel.SelectedViewIndex) or
            nameof(PlanningCalendarViewModel.SearchText) or
            nameof(PlanningCalendarViewModel.ShowAssignments) or
            nameof(PlanningCalendarViewModel.ShowOrders) or
            nameof(PlanningCalendarViewModel.ShowAbsences) or
            nameof(PlanningCalendarViewModel.ShowOperatingCalendar) or
            nameof(PlanningCalendarViewModel.ShowWeekends)))
            return;

        AppSettingsService.UpdateCurrentUserPreferences(preferences =>
        {
            preferences.CalendarSelectedViewIndex = viewModel.SelectedViewIndex;
            preferences.CalendarSearchText = viewModel.SearchText;
            preferences.CalendarShowAssignments = viewModel.ShowAssignments;
            preferences.CalendarShowOrders = viewModel.ShowOrders;
            preferences.CalendarShowAbsences = viewModel.ShowAbsences;
            preferences.CalendarShowOperatingCalendar = viewModel.ShowOperatingCalendar;
            preferences.CalendarShowWeekends = viewModel.ShowWeekends;
        });
    }

    private void PlanningCalendarView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width < 950)
            rightPanelCollapsed = true;
        ApplyPanelLayout();
    }

    private void ToggleLeftPanel_Click(object sender, RoutedEventArgs e)
    {
        leftPanelCollapsed = !leftPanelCollapsed;
        ApplyPanelLayout();
    }

    private void ToggleRightPanel_Click(object sender, RoutedEventArgs e)
    {
        rightPanelCollapsed = !rightPanelCollapsed;
        ApplyPanelLayout();
    }

    private void ApplyPanelLayout()
    {
        if (CalendarLeftColumn is null || CalendarLeftPanel is null ||
            CalendarDetailColumn is null || CalendarDetailPanel is null ||
            CalendarLeftToggleButton is null || CalendarRightToggleButton is null)
            return;

        CalendarLeftColumn.Width = leftPanelCollapsed ? new GridLength(0) : new GridLength(238);
        CalendarLeftPanel.Visibility = leftPanelCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CalendarLeftToggleButton.Content = leftPanelCollapsed ? "›" : "‹";
        CalendarLeftToggleButton.ToolTip = leftPanelCollapsed ? "Linken Bereich einblenden" : "Linken Bereich ausblenden";

        CalendarDetailColumn.Width = rightPanelCollapsed ? new GridLength(0) : new GridLength(292);
        CalendarDetailPanel.Visibility = rightPanelCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CalendarRightToggleButton.Content = rightPanelCollapsed ? "‹" : "›";
        CalendarRightToggleButton.ToolTip = rightPanelCollapsed ? "Rechten Bereich einblenden" : "Rechten Bereich ausblenden";
    }

    private void CalendarEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarEntryRow entry })
            return;

        SelectEntry(entry);
        e.Handled = true;
    }

    private void CalendarEntry_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarEntryRow entry })
            return;

        SelectEntry(entry);
        OpenEntry(entry);
        e.Handled = true;
    }

    private void SelectEntry(CalendarEntryRow entry)
    {
        if (viewModel.SelectedDate.Date != entry.Date.Date)
            viewModel.SelectDate(entry.Date);
        viewModel.SelectEntry(entry);
    }

    private void OpenSelectedEntry_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedEntry is not null)
            OpenEntry(viewModel.SelectedEntry);
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e) => viewModel.SelectEntry(null);

    private static void OpenEntry(CalendarEntryRow entry)
    {
        if (Application.Current.MainWindow is not MainWindow mainWindow)
            return;

        switch (entry.EntryType)
        {
            case "Einsatz":
                mainWindow.OpenDayPlanning(entry.Date);
                return;

            case "Auftrag" when entry.ProductionOrderId.HasValue:
                mainWindow.OpenProductionOrder(entry.ProductionOrderId.Value);
                return;

            case "Abwesenheit" when entry.EmployeeId.HasValue:
                mainWindow.OpenEmployeeQuickCard(entry.EmployeeId.Value, entry.Date);
                return;

            case "Betriebskalender":
                mainWindow.OpenWorkTimeCalendar();
                return;
        }

        if (entry.EmployeeId.HasValue)
            mainWindow.OpenEmployeeQuickCard(entry.EmployeeId.Value, entry.Date);
        else
            mainWindow.OpenDayPlanning(entry.Date);
    }

    private void EmployeeRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarEmployeeRow employee })
            return;

        if (Application.Current.MainWindow is MainWindow mainWindow)
            mainWindow.OpenEmployeeQuickCard(employee.EmployeeId, viewModel.SelectedDate);
    }

    private void SelectCalendarDate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateTime date })
            return;

        viewModel.SelectDate(date);
    }

    private void OpenCalendarDate_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: DateTime date })
            return;

        viewModel.SelectDate(date);
        viewModel.SelectedViewIndex = 0;
        e.Handled = true;
    }

    private void ExportCalendarPdf_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = AppSettingsService.Load();
            var exportDirectory = string.IsNullOrWhiteSpace(settings.DefaultExportDirectory)
                ? AppPaths.ExportsDirectory
                : settings.DefaultExportDirectory;
            Directory.CreateDirectory(exportDirectory);

            var dialog = new SaveFileDialog
            {
                Title = "Planungskalender als PDF exportieren",
                Filter = "PDF-Dokument (*.pdf)|*.pdf",
                AddExtension = true,
                DefaultExt = ".pdf",
                FileName = PlanningCalendarPdfService.BuildFileName(viewModel),
                InitialDirectory = exportDirectory
            };

            if (dialog.ShowDialog() != true)
                return;

            var result = PlanningCalendarPdfService.Export(viewModel, dialog.FileName);
            viewModel.StatusMessage = $"Kalender-PDF erstellt: {result.PageCount} Seite(n).";

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

    private void OpenDayPlanning_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mainWindow)
            mainWindow.OpenDayPlanning(viewModel.SelectedDate);
    }
}

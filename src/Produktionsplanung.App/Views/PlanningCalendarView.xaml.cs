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
    private Point employeeDragStartPoint;
    private CalendarEmployeeRow? draggedEmployee;
    private bool suppressEmployeeClick;

    public PlanningCalendarView(DateTime? initialDate = null, int? initialViewIndex = null)
    {
        InitializeComponent();
        viewModel = new PlanningCalendarViewModel();
        restoringPreferences = true;
        ApplySavedPreferences();
        if (initialDate.HasValue)
            viewModel.SelectedDate = initialDate.Value.Date;
        if (initialViewIndex.HasValue)
            viewModel.SelectedViewIndex = Math.Clamp(initialViewIndex.Value, 0, 2);
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

    private void PlanningCalendarView_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyPanelLayout();

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

        CalendarLeftColumn.Width = leftPanelCollapsed ? new GridLength(0) : new GridLength(198);
        CalendarLeftPanel.Visibility = leftPanelCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CalendarLeftToggleButton.Content = leftPanelCollapsed ? "›" : "‹";
        CalendarLeftToggleButton.ToolTip = leftPanelCollapsed ? "Linken Bereich einblenden" : "Linken Bereich ausblenden";

        CalendarDetailColumn.Width = rightPanelCollapsed ? new GridLength(0) : new GridLength(350);
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

    private void OpenEntry(CalendarEntryRow entry)
    {
        if (Application.Current.MainWindow is not MainWindow mainWindow)
            return;

        switch (entry.EntryType)
        {
            case "Einsatz" when entry.EmployeeId.HasValue:
                mainWindow.OpenEmployeeQuickCard(entry.EmployeeId.Value, entry.Date);
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
        {
            mainWindow.OpenEmployeeQuickCard(entry.EmployeeId.Value, entry.Date);
            return;
        }

        viewModel.SelectDate(entry.Date);
        viewModel.SelectedViewIndex = 0;
    }

    private void EmployeeRow_Click(object sender, RoutedEventArgs e)
    {
        if (suppressEmployeeClick)
        {
            suppressEmployeeClick = false;
            e.Handled = true;
            return;
        }

        if (sender is not FrameworkElement { DataContext: CalendarEmployeeRow employee })
            return;

        if (Application.Current.MainWindow is MainWindow mainWindow)
            mainWindow.OpenEmployeeQuickCard(employee.EmployeeId, viewModel.SelectedDate);
    }

    private void EmployeeRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        draggedEmployee = (sender as FrameworkElement)?.DataContext as CalendarEmployeeRow;
        employeeDragStartPoint = e.GetPosition(this);
        suppressEmployeeClick = false;
    }

    private void EmployeeRow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || draggedEmployee is null)
            return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - employeeDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - employeeDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var employee = draggedEmployee;
        draggedEmployee = null;
        suppressEmployeeClick = true;
        var data = new DataObject(typeof(CalendarEmployeeRow), employee);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        e.Handled = true;
    }

    private void CalendarEntry_PreviewDragOver(object sender, DragEventArgs e)
    {
        var isProduction = sender is FrameworkElement { DataContext: CalendarEntryRow entry } &&
                           entry.EntryType == "Auftrag";
        e.Effects = isProduction && e.Data.GetDataPresent(typeof(CalendarEmployeeRow))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void CalendarEntry_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarEntryRow entry } ||
            entry.EntryType != "Auftrag" ||
            e.Data.GetData(typeof(CalendarEmployeeRow)) is not CalendarEmployeeRow employee)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = viewModel.AssignEmployeeToProduction(employee.EmployeeId, entry)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
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

}

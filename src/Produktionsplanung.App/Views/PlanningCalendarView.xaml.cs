using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class PlanningCalendarView : UserControl
{
    private readonly PlanningCalendarViewModel viewModel;
    private bool restoringPreferences;

    public PlanningCalendarView()
    {
        InitializeComponent();
        viewModel = new PlanningCalendarViewModel();
        restoringPreferences = true;
        ApplySavedPreferences();
        restoringPreferences = false;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = viewModel;
        Loaded += (_, _) => UpdateResponsiveLayout(ActualWidth);
    }

    private void ApplySavedPreferences()
    {
        var settings = AppSettingsService.Load();
        viewModel.SelectedViewIndex = settings.CalendarSelectedViewIndex;
        viewModel.SearchText = settings.CalendarSearchText;
        viewModel.ShowAssignments = settings.CalendarShowAssignments;
        viewModel.ShowOrders = settings.CalendarShowOrders;
        viewModel.ShowAbsences = settings.CalendarShowAbsences;
        viewModel.ShowOperatingCalendar = settings.CalendarShowOperatingCalendar;
        viewModel.ShowWeekends = settings.CalendarShowWeekends;
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

        AppSettingsService.Update(settings =>
        {
            settings.CalendarSelectedViewIndex = viewModel.SelectedViewIndex;
            settings.CalendarSearchText = viewModel.SearchText;
            settings.CalendarShowAssignments = viewModel.ShowAssignments;
            settings.CalendarShowOrders = viewModel.ShowOrders;
            settings.CalendarShowAbsences = viewModel.ShowAbsences;
            settings.CalendarShowOperatingCalendar = viewModel.ShowOperatingCalendar;
            settings.CalendarShowWeekends = viewModel.ShowWeekends;
        });
    }

    private void PlanningCalendarView_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateResponsiveLayout(e.NewSize.Width);

    private void UpdateResponsiveLayout(double width)
    {
        if (CalendarDetailColumn is null || CalendarDetailGapColumn is null || CalendarDetailPanel is null)
            return;

        var compact = width < 1150;
        CalendarDetailColumn.Width = compact ? new GridLength(0) : new GridLength(292);
        CalendarDetailGapColumn.Width = compact ? new GridLength(0) : new GridLength(12);
        CalendarDetailPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
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

    private void OpenDayPlanning_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mainWindow)
            mainWindow.OpenDayPlanning(viewModel.SelectedDate);
    }
}

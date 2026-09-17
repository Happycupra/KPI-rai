using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class PlanningCalendarView : UserControl
{
    private readonly PlanningCalendarViewModel viewModel;

    public PlanningCalendarView()
    {
        InitializeComponent();
        viewModel = new PlanningCalendarViewModel();
        DataContext = viewModel;
    }

    private void CalendarEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarEntryRow entry })
            return;

        viewModel.SelectEntry(entry);
        e.Handled = true;
    }

    private void CalendarEntry_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CalendarEntryRow entry })
            return;

        viewModel.SelectEntry(entry);
        OpenEntry(entry);
        e.Handled = true;
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

        if (entry.EmployeeId.HasValue)
        {
            mainWindow.OpenEmployeeQuickCard(entry.EmployeeId.Value, entry.Date);
            return;
        }

        if (entry.EntryType == "Betriebskalender")
        {
            mainWindow.OpenWorkTimeCalendar();
            return;
        }

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

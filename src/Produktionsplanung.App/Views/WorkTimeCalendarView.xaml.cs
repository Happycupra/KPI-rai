using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class WorkTimeCalendarView : UserControl
{
    public WorkTimeCalendarView()
    {
        InitializeComponent();
        DataContext = new WorkTimeCalendarViewModel();
    }

    private void WorkTimeRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: WorkTimeEntryRow row } gridRow)
        {
            gridRow.IsSelected = true;
            ((WorkTimeCalendarViewModel)DataContext).SelectedEntry = row;
        }
    }

    private static WorkTimeEntryRow? ContextEntry(object sender)
    {
        if (sender is not MenuItem item ||
            item.Parent is not ContextMenu menu ||
            menu.PlacementTarget is not DataGridRow { DataContext: WorkTimeEntryRow row })
            return null;
        return row;
    }

    private MainWindow? Host => Window.GetWindow(this) as MainWindow;

    private void WorkTimeEmployeeCardContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextEntry(sender) is { } row) Host?.OpenEmployeeQuickCard(row.EmployeeId, row.Date);
    }

    private void WorkTimeEmployeeMasterContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextEntry(sender) is { } row) Host?.OpenEmployee(row.EmployeeId);
    }

    private void WorkTimeEmployeePlanContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextEntry(sender) is { } row) Host?.OpenDayPlanning(row.Date);
    }
}

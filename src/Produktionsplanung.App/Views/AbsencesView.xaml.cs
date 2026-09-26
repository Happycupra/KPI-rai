using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class AbsencesView : UserControl
{
    public AbsencesView()
    {
        InitializeComponent();
        DataContext = new AbsenceManagementViewModel();
    }

    private void AbsenceRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: AbsenceRow row } gridRow)
        {
            gridRow.IsSelected = true;
            ((AbsenceManagementViewModel)DataContext).SelectedAbsence = row;
        }
    }

    private static AbsenceRow? ContextAbsence(object sender)
    {
        if (sender is not MenuItem item ||
            item.Parent is not ContextMenu menu ||
            menu.PlacementTarget is not DataGridRow { DataContext: AbsenceRow row })
            return null;
        return row;
    }

    private MainWindow? Host => Window.GetWindow(this) as MainWindow;

    private void EmployeeCardContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextAbsence(sender) is { } row) Host?.OpenEmployeeQuickCard(row.EmployeeId, DateTime.Today);
    }

    private void EmployeeMasterContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextAbsence(sender) is { } row) Host?.OpenEmployee(row.EmployeeId);
    }

    private void EmployeePlanContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextAbsence(sender) is not null) Host?.OpenPlanningCalendar();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class AbsencesView : UserControl, IUnsavedChangesAware
{
    private readonly AbsenceManagementViewModel viewModel;
    private string baseline = string.Empty;

    public AbsencesView()
    {
        InitializeComponent();
        viewModel = new AbsenceManagementViewModel();
        DataContext = viewModel;
        CaptureBaseline();
    }

    public bool HasUnsavedChanges => baseline != BuildSnapshot();
    public string UnsavedChangesDescription => "Abwesenheit";

    public bool TrySaveChanges()
    {
        viewModel.SaveCommand.Execute(null);
        if (!string.Equals(viewModel.StatusMessage, "Abwesenheit gespeichert.", StringComparison.Ordinal))
            return false;

        CaptureBaseline();
        return true;
    }

    public void DiscardChanges()
    {
        viewModel.NewAbsenceCommand.Execute(null);
        CaptureBaseline();
    }

    private void AbsenceRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: AbsenceRow row } gridRow)
        {
            gridRow.IsSelected = true;
            viewModel.SelectedAbsence = row;
            CaptureBaseline();
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

    private void CaptureBaseline() => baseline = BuildSnapshot();

    private string BuildSnapshot() => string.Join("\u001f",
        viewModel.SelectedAbsence?.Id ?? 0,
        viewModel.SelectedEmployee?.Id ?? 0,
        viewModel.AbsenceType,
        viewModel.StartDate.Date,
        viewModel.EndDate.Date,
        viewModel.Comment);
}

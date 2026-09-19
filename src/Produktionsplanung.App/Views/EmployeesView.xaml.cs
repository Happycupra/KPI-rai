using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class EmployeesView : UserControl, IUnsavedChangesAware
{
    private readonly EmployeeManagementViewModel viewModel;
    private string baseline = string.Empty;

    public EmployeesView() : this(null)
    {
    }

    public EmployeesView(int? employeeId)
    {
        InitializeComponent();
        viewModel = new EmployeeManagementViewModel();
        if (employeeId.HasValue)
            viewModel.SelectEmployeeById(employeeId.Value);

        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        CaptureBaseline();
    }

    public bool HasUnsavedChanges => baseline != BuildSnapshot();
    public string UnsavedChangesDescription => "Mitarbeiterdaten";

    public bool TrySaveChanges()
    {
        if (!viewModel.IsEditorOpen)
            return true;

        viewModel.SaveCommand.Execute(null);
        return !HasUnsavedChanges;
    }

    public void DiscardChanges()
    {
        viewModel.CloseEditor();
        CaptureBaseline();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EmployeeManagementViewModel.SelectedEmployee) or
            nameof(EmployeeManagementViewModel.EditingId) or
            nameof(EmployeeManagementViewModel.IsEditorOpen))
        {
            CaptureBaseline();
        }
    }

    private void NewEmployee_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmReplaceEditor())
            return;

        viewModel.NewEmployeeCommand.Execute(null);
        CaptureBaseline();
    }

    private void EditEmployeeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmReplaceEditor())
            return;

        if (sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu contextMenu ||
            contextMenu.PlacementTarget is not FrameworkElement { DataContext: EmployeeDirectoryRow row })
            return;

        viewModel.EditEmployee(row.Id);
        CaptureBaseline();
    }

    private void CloseEditor_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsEditorOpen && HasUnsavedChanges)
        {
            var result = MessageBox.Show(
                "Ungespeicherte Änderungen verwerfen?",
                "Bearbeitung schliessen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
        }

        viewModel.CloseEditor();
        CaptureBaseline();
    }

    private bool ConfirmReplaceEditor()
    {
        if (!viewModel.IsEditorOpen || !HasUnsavedChanges)
            return true;

        var result = MessageBox.Show(
            "Es gibt ungespeicherte Änderungen. Diese verwerfen und eine andere Bearbeitung öffnen?",
            "Ungespeicherte Änderungen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return false;

        viewModel.CloseEditor();
        CaptureBaseline();
        return true;
    }

    private void CaptureBaseline() => baseline = BuildSnapshot();

    private string BuildSnapshot() => string.Join("\u001f",
        viewModel.IsEditorOpen,
        viewModel.EditingId,
        viewModel.PersonnelNumber,
        viewModel.FirstName,
        viewModel.LastName,
        viewModel.Role,
        viewModel.Department,
        viewModel.WorkloadPercent,
        viewModel.WeeklyTargetHours,
        viewModel.IsActive);
}

using System.ComponentModel;
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
        viewModel.SaveCommand.Execute(null);
        return !HasUnsavedChanges;
    }

    public void DiscardChanges()
    {
        var id = viewModel.EditingId;
        if (id > 0)
        {
            viewModel.SelectedEmployee = null;
            viewModel.SelectEmployeeById(id);
        }
        else
        {
            viewModel.NewEmployeeCommand.Execute(null);
        }
        CaptureBaseline();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EmployeeManagementViewModel.SelectedEmployee) or nameof(EmployeeManagementViewModel.EditingId))
            CaptureBaseline();
    }

    private void CaptureBaseline() => baseline = BuildSnapshot();

    private string BuildSnapshot() => string.Join("\u001f",
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

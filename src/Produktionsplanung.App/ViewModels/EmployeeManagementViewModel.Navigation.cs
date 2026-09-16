namespace Produktionsplanung.App.ViewModels;

public partial class EmployeeManagementViewModel
{
    public void SelectEmployeeById(int employeeId)
    {
        var employee = _allEmployees.FirstOrDefault(x => x.Id == employeeId);
        if (employee is null)
            return;

        SearchText = string.Empty;
        if (!employee.IsActive)
            ShowInactive = true;

        ApplyFilter();
        SelectedEmployee = Employees.FirstOrDefault(x => x.Id == employeeId);
    }
}

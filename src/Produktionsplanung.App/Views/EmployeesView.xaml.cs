using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class EmployeesView : UserControl
{
    public EmployeesView() : this(null)
    {
    }

    public EmployeesView(int? employeeId)
    {
        InitializeComponent();
        var viewModel = new EmployeeManagementViewModel();
        if (employeeId.HasValue)
            viewModel.SelectEmployeeById(employeeId.Value);
        DataContext = viewModel;
    }
}

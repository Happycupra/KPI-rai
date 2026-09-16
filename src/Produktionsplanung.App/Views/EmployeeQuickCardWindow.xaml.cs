using System.Windows;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class EmployeeQuickCardWindow : Window
{
    private readonly EmployeeQuickCardViewModel viewModel;

    public EmployeeQuickCardWindow(int employeeId, DateTime? contextDate = null)
    {
        InitializeComponent();
        viewModel = new EmployeeQuickCardViewModel(employeeId, contextDate);
        DataContext = viewModel;
        Loaded += EmployeeQuickCardWindow_Loaded;
    }

    private void EmployeeQuickCardWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (Owner is not Window owner)
            return;

        var desiredHeight = Math.Max(MinHeight, Math.Min(760, owner.ActualHeight - 36));
        Height = desiredHeight;
        Left = Math.Max(SystemParameters.WorkArea.Left, owner.Left + owner.ActualWidth - Width - 18);
        Top = Math.Max(SystemParameters.WorkArea.Top, owner.Top + 18);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Refresh_Click(object sender, RoutedEventArgs e) => viewModel.Refresh();

    private void OpenDayPlan_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mainWindow)
            mainWindow.OpenDayPlanning(viewModel.ContextDate);
        Close();
    }

    private void OpenEmployee_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mainWindow)
            mainWindow.OpenEmployee(viewModel.EmployeeId);
        Close();
    }
}

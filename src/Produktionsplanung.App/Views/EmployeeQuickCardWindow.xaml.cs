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

        var desiredHeight = Math.Max(MinHeight, Math.Min(760, owner.ActualHeight - 48));
        Height = desiredHeight;

        // Keep the quick card fully inside the owner window and place it more centrally.
        // This is especially important on portrait or secondary monitors.
        var ownerWidth = Math.Max(Width, owner.ActualWidth);
        var ownerHeight = Math.Max(Height, owner.ActualHeight);
        Left = owner.Left + Math.Max(12, (ownerWidth - Width) / 2);
        Top = owner.Top + Math.Max(12, (ownerHeight - Height) / 2);
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

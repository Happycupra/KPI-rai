using System.Windows;
using Produktionsplanung.App.Views;

namespace Produktionsplanung.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ShowDashboard();
    }

    private void ShowDashboard_Click(object sender, RoutedEventArgs e) => ShowDashboard();

    private void ShowEmployees_Click(object sender, RoutedEventArgs e) =>
        ContentHost.Content = new EmployeesView();

    private void ShowSkills_Click(object sender, RoutedEventArgs e) =>
        ContentHost.Content = new SkillMatrixView();

    private void ShowDashboard() =>
        ContentHost.Content = new DashboardView();
}

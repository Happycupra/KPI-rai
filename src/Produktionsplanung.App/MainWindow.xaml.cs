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

    private void ShowDayPlanning_Click(object sender, RoutedEventArgs e) =>
        ContentHost.Content = new DayPlanningView();

    private void ShowWeekPlanning_Click(object sender, RoutedEventArgs e) =>
        ContentHost.Content = new WeekPlanningView();

    private void ShowEmployees_Click(object sender, RoutedEventArgs e) =>
        ContentHost.Content = new EmployeesView();

    private void ShowSkills_Click(object sender, RoutedEventArgs e) =>
        ContentHost.Content = new SkillMatrixView();

    private void ShowWorkstations_Click(object sender, RoutedEventArgs e) =>
        ContentHost.Content = new WorkstationsView();

    private void ShowShifts_Click(object sender, RoutedEventArgs e) =>
        ContentHost.Content = new ShiftsView();

    private void ShowAbsences_Click(object sender, RoutedEventArgs e) =>
        ContentHost.Content = new AbsencesView();

    private void ShowDashboard() =>
        ContentHost.Content = new DashboardView();
}

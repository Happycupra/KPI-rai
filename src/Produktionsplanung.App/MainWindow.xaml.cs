using System.Reflection;
using System.Windows;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.Views;

namespace Produktionsplanung.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ApplyRolePermissions();
        ShowDashboard();
    }

    private void ApplyRolePermissions()
    {
        var user = SessionService.CurrentUser;
        SessionInfoBlock.Text = user is null ? "Nicht angemeldet" : $"{user.DisplayName} · {user.Role}";
        VersionBlock.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";

        var canOperate = SessionService.IsPlannerOrAdmin;
        DayPlanningButton.IsEnabled = canOperate;
        WeekPlanningButton.IsEnabled = canOperate;
        ProductionOrdersButton.IsEnabled = canOperate;
        ProductionActualButton.IsEnabled = canOperate;
        EmployeesButton.IsEnabled = canOperate;
        SkillsButton.IsEnabled = canOperate;
        WorkstationsButton.IsEnabled = canOperate;
        ShiftsButton.IsEnabled = canOperate;
        AbsencesButton.IsEnabled = canOperate;

        UserAdminButton.IsEnabled = SessionService.IsAdministrator;
        SettingsButton.IsEnabled = SessionService.IsAdministrator;
    }

    private void ShowDashboard_Click(object sender, RoutedEventArgs e) => ShowDashboard();
    private void ShowDayPlanning_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) ContentHost.Content = new DayPlanningView(); }
    private void ShowWeekPlanning_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) ContentHost.Content = new WeekPlanningView(); }
    private void ShowProductionOrders_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) ContentHost.Content = new ProductionOrdersView(); }
    private void ShowProductionActual_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) ContentHost.Content = new ProductionActualView(); }
    private void ShowAnalytics_Click(object sender, RoutedEventArgs e) => ContentHost.Content = new AnalyticsView();
    private void ShowEmployees_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) ContentHost.Content = new EmployeesView(); }
    private void ShowSkills_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) ContentHost.Content = new SkillMatrixView(); }
    private void ShowWorkstations_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) ContentHost.Content = new WorkstationsView(); }
    private void ShowShifts_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) ContentHost.Content = new ShiftsView(); }
    private void ShowAbsences_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) ContentHost.Content = new AbsencesView(); }
    private void ShowUserAdmin_Click(object sender, RoutedEventArgs e) { if (SessionService.IsAdministrator) ContentHost.Content = new UserAdminView(); }
    private void ShowSettings_Click(object sender, RoutedEventArgs e) { if (SessionService.IsAdministrator) ContentHost.Content = new SettingsView(); }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ChangePasswordWindow { Owner = this };
        dialog.ShowDialog();
    }

    private void ShowDashboard() => ContentHost.Content = new DashboardView();
}

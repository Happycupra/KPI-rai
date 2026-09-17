using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.Views;

namespace Produktionsplanung.App;

public partial class MainWindow : Window
{
    private readonly Stack<NavigationEntry> navigationHistory = new();
    private EmployeeQuickCardWindow? employeeQuickCardWindow;
    private Button? activeNavButton;
    private bool sidebarCollapsed;

    public MainWindow()
    {
        InitializeComponent();
        ApplyRolePermissions();
        SetSidebarCollapsed(false);
        Navigate(new DashboardView(), "Dashboard", DashboardButton, addToHistory: false);
    }

    private void ApplyRolePermissions()
    {
        var user = SessionService.CurrentUser;
        SessionInfoBlock.Text = user is null ? "Nicht angemeldet" : $"{user.DisplayName} · {user.Role}";
        VersionBlock.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";

        var canOperate = SessionService.IsPlannerOrAdmin;
        PlanningCalendarButton.IsEnabled = canOperate;
        DayPlanningButton.IsEnabled = canOperate;
        WeekPlanningButton.IsEnabled = canOperate;
        WorkTimeCalendarButton.IsEnabled = canOperate;
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

    private void ShowDashboard_Click(object sender, RoutedEventArgs e) =>
        Navigate(new DashboardView(), "Dashboard", DashboardButton);

    private void ShowPlanningCalendar_Click(object sender, RoutedEventArgs e)
    {
        if (SessionService.IsPlannerOrAdmin)
            Navigate(new PlanningCalendarView(), "Planungskalender", PlanningCalendarButton);
    }

    private void ShowDayPlanning_Click(object sender, RoutedEventArgs e) => OpenDayPlanning(DateTime.Today);

    private void ShowWeekPlanning_Click(object sender, RoutedEventArgs e)
    {
        if (SessionService.IsPlannerOrAdmin)
            Navigate(new WeekPlanningView(), "Wochenplanung", WeekPlanningButton);
    }

    private void ShowWorkTimeCalendar_Click(object sender, RoutedEventArgs e) => OpenWorkTimeCalendar();

    private void ShowProductionOrders_Click(object sender, RoutedEventArgs e)
    {
        if (SessionService.IsPlannerOrAdmin)
            Navigate(new ProductionOrdersView(), "Produktionsaufträge", ProductionOrdersButton);
    }

    private void ShowProductionActual_Click(object sender, RoutedEventArgs e)
    {
        if (SessionService.IsPlannerOrAdmin)
            Navigate(new ProductionActualView(), "Ist-Produktion / OEE", ProductionActualButton);
    }

    private void ShowAnalytics_Click(object sender, RoutedEventArgs e) =>
        Navigate(new AnalyticsView(), "Auswertungen / KPIs", AnalyticsButton);

    private void ShowEmployees_Click(object sender, RoutedEventArgs e)
    {
        if (SessionService.IsPlannerOrAdmin)
            Navigate(new EmployeesView(), "Mitarbeiter", EmployeesButton);
    }

    private void ShowSkills_Click(object sender, RoutedEventArgs e)
    {
        if (SessionService.IsPlannerOrAdmin)
            Navigate(new SkillMatrixView(), "Skill-Matrix", SkillsButton);
    }

    private void ShowWorkstations_Click(object sender, RoutedEventArgs e)
    {
        if (SessionService.IsPlannerOrAdmin)
            Navigate(new WorkstationsView(), "Arbeitsplätze", WorkstationsButton);
    }

    private void ShowShifts_Click(object sender, RoutedEventArgs e)
    {
        if (SessionService.IsPlannerOrAdmin)
            Navigate(new ShiftsView(), "Schichten", ShiftsButton);
    }

    private void ShowAbsences_Click(object sender, RoutedEventArgs e)
    {
        if (SessionService.IsPlannerOrAdmin)
            Navigate(new AbsencesView(), "Abwesenheiten", AbsencesButton);
    }

    private void ShowUserAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (SessionService.IsAdministrator)
            Navigate(new UserAdminView(), "Benutzer / Audit", UserAdminButton);
    }

    private void ShowSettings_Click(object sender, RoutedEventArgs e)
    {
        if (SessionService.IsAdministrator)
            Navigate(new SettingsView(), "Einstellungen", SettingsButton);
    }

    public void OpenDayPlanning(DateTime date)
    {
        if (SessionService.IsPlannerOrAdmin)
            Navigate(new DayPlanningView(date), $"Tagesplanung · {date:dd.MM.yyyy}", DayPlanningButton);
    }

    public void OpenWorkTimeCalendar()
    {
        if (SessionService.IsPlannerOrAdmin)
            Navigate(new WorkTimeCalendarView(), "Arbeitszeit / Betrieb", WorkTimeCalendarButton);
    }

    public void OpenEmployee(int employeeId)
    {
        if (SessionService.IsPlannerOrAdmin)
            Navigate(new EmployeesView(employeeId), "Mitarbeiter", EmployeesButton);
    }

    public void OpenEmployeeQuickCard(int employeeId, DateTime? contextDate = null)
    {
        if (!SessionService.IsPlannerOrAdmin)
            return;

        if (employeeQuickCardWindow is not null)
        {
            employeeQuickCardWindow.Close();
            employeeQuickCardWindow = null;
        }

        employeeQuickCardWindow = new EmployeeQuickCardWindow(employeeId, contextDate)
        {
            Owner = this
        };
        employeeQuickCardWindow.Closed += (_, _) => employeeQuickCardWindow = null;
        employeeQuickCardWindow.Show();
    }

    private void Navigate(object content, string title, Button navButton, bool addToHistory = true)
    {
        if (ContentHost.Content is not null && activeNavButton == navButton &&
            ContentHost.Content.GetType() == content.GetType())
        {
            CurrentPageTitle.Text = title;
            return;
        }

        if (addToHistory && ContentHost.Content is not null)
        {
            navigationHistory.Push(new NavigationEntry(
                ContentHost.Content,
                CurrentPageTitle.Text,
                activeNavButton?.Name));
        }

        ContentHost.Content = content;
        CurrentPageTitle.Text = title;
        SetActiveNavigation(navButton);
        BackButton.IsEnabled = navigationHistory.Count > 0;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (navigationHistory.Count == 0)
            return;

        var previous = navigationHistory.Pop();
        ContentHost.Content = previous.Content;
        CurrentPageTitle.Text = previous.Title;
        var button = string.IsNullOrWhiteSpace(previous.ButtonName)
            ? null
            : FindName(previous.ButtonName) as Button;
        SetActiveNavigation(button);
        BackButton.IsEnabled = navigationHistory.Count > 0;
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e) => SetSidebarCollapsed(!sidebarCollapsed);

    private void SetSidebarCollapsed(bool collapsed)
    {
        sidebarCollapsed = collapsed;
        SidebarColumn.Width = new GridLength(collapsed ? 74 : 260);
        FullBrand.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CompactBrand.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        FooterDetails.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CompactFooter.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;

        PlanningGroupLabel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        ProductionGroupLabel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        MasterDataGroupLabel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        AdminGroupLabel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

        foreach (var button in NavigationPanel.Children.OfType<Button>())
        {
            var parts = (button.Tag?.ToString() ?? string.Empty).Split('|', 2);
            if (parts.Length != 2)
                continue;

            button.Content = collapsed ? parts[0] : $"{parts[0]}   {parts[1]}";
            button.ToolTip = parts[1];
            button.HorizontalContentAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            button.Padding = collapsed ? new Thickness(0) : new Thickness(13, 0, 13, 0);
            button.FontSize = collapsed ? 17 : 13;
        }
    }

    private void SetActiveNavigation(Button? active)
    {
        foreach (var button in NavigationPanel.Children.OfType<Button>())
        {
            button.Background = Brushes.Transparent;
            button.Foreground = new SolidColorBrush(Color.FromRgb(217, 230, 242));
        }

        activeNavButton = active;
        if (active is null)
            return;

        active.Background = (Brush)FindResource("PrimaryBrush");
        active.Foreground = Brushes.White;
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ChangePasswordWindow { Owner = this };
        dialog.ShowDialog();
    }

    private sealed record NavigationEntry(object Content, string Title, string? ButtonName);
}

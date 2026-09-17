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
    private NavigationEntry? currentNavigation;
    private EmployeeQuickCardWindow? employeeQuickCardWindow;
    private Button? activeNavButton;
    private bool sidebarCollapsed;

    public MainWindow()
    {
        InitializeComponent();
        ApplyRolePermissions();
        SetSidebarCollapsed(false);
        Navigate(CreateEntry(NavigationRoute.Dashboard), addToHistory: false);
    }

    private void ApplyRolePermissions()
    {
        var user = SessionService.CurrentUser;
        SessionInfoBlock.Text = user is null ? "Nicht angemeldet" : $"{user.DisplayName} · {user.Role}";
        VersionBlock.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";
        var canOperate = SessionService.IsPlannerOrAdmin;
        PlanningCalendarButton.IsEnabled = canOperate; DayPlanningButton.IsEnabled = canOperate; WeekPlanningButton.IsEnabled = canOperate;
        WorkTimeCalendarButton.IsEnabled = canOperate; ProductionOrdersButton.IsEnabled = canOperate; ProductionActualButton.IsEnabled = canOperate;
        EmployeesButton.IsEnabled = canOperate; SkillsButton.IsEnabled = canOperate; WorkstationsButton.IsEnabled = canOperate;
        ShiftsButton.IsEnabled = canOperate; AbsencesButton.IsEnabled = canOperate;
        UserAdminButton.IsEnabled = SessionService.IsAdministrator; SettingsButton.IsEnabled = SessionService.IsAdministrator;
    }

    private void ShowDashboard_Click(object sender, RoutedEventArgs e) => Navigate(CreateEntry(NavigationRoute.Dashboard));
    private void ShowPlanningCalendar_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.PlanningCalendar)); }
    private void ShowDayPlanning_Click(object sender, RoutedEventArgs e) => OpenDayPlanning(DateTime.Today);
    private void ShowWeekPlanning_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.WeekPlanning)); }
    private void ShowWorkTimeCalendar_Click(object sender, RoutedEventArgs e) => OpenWorkTimeCalendar();
    private void ShowProductionOrders_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.ProductionOrders)); }
    private void ShowProductionActual_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.ProductionActual)); }
    private void ShowAnalytics_Click(object sender, RoutedEventArgs e) => Navigate(CreateEntry(NavigationRoute.Analytics));
    private void ShowEmployees_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.Employees)); }
    private void ShowSkills_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.Skills)); }
    private void ShowWorkstations_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.Workstations)); }
    private void ShowShifts_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.Shifts)); }
    private void ShowAbsences_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.Absences)); }
    private void ShowUserAdmin_Click(object sender, RoutedEventArgs e) { if (SessionService.IsAdministrator) Navigate(CreateEntry(NavigationRoute.UserAdmin)); }
    private void ShowSettings_Click(object sender, RoutedEventArgs e) { if (SessionService.IsAdministrator) Navigate(CreateEntry(NavigationRoute.Settings)); }

    public void OpenDayPlanning(DateTime date) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.DayPlanning, date: date.Date)); }
    public void OpenWorkTimeCalendar() { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.WorkTimeCalendar)); }
    public void OpenEmployee(int employeeId) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.Employees, employeeId: employeeId)); }

    public void OpenEmployeeQuickCard(int employeeId, DateTime? contextDate = null)
    {
        if (!SessionService.IsPlannerOrAdmin) return;
        if (employeeQuickCardWindow is not null) { employeeQuickCardWindow.Close(); employeeQuickCardWindow = null; }
        employeeQuickCardWindow = new EmployeeQuickCardWindow(employeeId, contextDate) { Owner = this };
        employeeQuickCardWindow.Closed += (_, _) => employeeQuickCardWindow = null;
        employeeQuickCardWindow.Show();
    }

    private NavigationEntry CreateEntry(NavigationRoute route, DateTime? date = null, int? employeeId = null) => route switch
    {
        NavigationRoute.Dashboard => new(route, "Dashboard", nameof(DashboardButton), () => new DashboardView()),
        NavigationRoute.PlanningCalendar => new(route, "Planungskalender", nameof(PlanningCalendarButton), () => new PlanningCalendarView()),
        NavigationRoute.DayPlanning => new(route, $"Tagesplanung · {(date ?? DateTime.Today):dd.MM.yyyy}", nameof(DayPlanningButton), () => new DayPlanningView((date ?? DateTime.Today).Date), date?.Date),
        NavigationRoute.WeekPlanning => new(route, "Wochenplanung", nameof(WeekPlanningButton), () => new WeekPlanningView()),
        NavigationRoute.WorkTimeCalendar => new(route, "Arbeitszeit / Betrieb", nameof(WorkTimeCalendarButton), () => new WorkTimeCalendarView()),
        NavigationRoute.ProductionOrders => new(route, "Produktionsaufträge", nameof(ProductionOrdersButton), () => new ProductionOrdersView()),
        NavigationRoute.ProductionActual => new(route, "Ist-Produktion / OEE", nameof(ProductionActualButton), () => new ProductionActualView()),
        NavigationRoute.Analytics => new(route, "Auswertungen / KPIs", nameof(AnalyticsButton), () => new AnalyticsView()),
        NavigationRoute.Employees when employeeId.HasValue => new(route, "Mitarbeiter", nameof(EmployeesButton), () => new EmployeesView(employeeId.Value), employeeId: employeeId),
        NavigationRoute.Employees => new(route, "Mitarbeiter", nameof(EmployeesButton), () => new EmployeesView()),
        NavigationRoute.Skills => new(route, "Skill-Matrix", nameof(SkillsButton), () => new SkillMatrixView()),
        NavigationRoute.Workstations => new(route, "Arbeitsplätze", nameof(WorkstationsButton), () => new WorkstationsView()),
        NavigationRoute.Shifts => new(route, "Schichten", nameof(ShiftsButton), () => new ShiftsView()),
        NavigationRoute.Absences => new(route, "Abwesenheiten", nameof(AbsencesButton), () => new AbsencesView()),
        NavigationRoute.UserAdmin => new(route, "Benutzer / Audit", nameof(UserAdminButton), () => new UserAdminView()),
        NavigationRoute.Settings => new(route, "Einstellungen", nameof(SettingsButton), () => new SettingsView()),
        _ => throw new ArgumentOutOfRangeException(nameof(route))
    };

    private void Navigate(NavigationEntry entry, bool addToHistory = true)
    {
        if (addToHistory && currentNavigation is not null) navigationHistory.Push(currentNavigation);
        ShowEntry(entry);
    }

    private void ShowEntry(NavigationEntry entry)
    {
        ContentHost.Content = entry.CreateContent();
        currentNavigation = entry;
        CurrentPageTitle.Text = entry.Title;
        var button = FindName(entry.ButtonName) as Button;
        SetActiveNavigation(button);
        BackButton.IsEnabled = navigationHistory.Count > 0;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (navigationHistory.Count == 0) return;
        ShowEntry(navigationHistory.Pop());
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e) => SetSidebarCollapsed(!sidebarCollapsed);
    private void SetSidebarCollapsed(bool collapsed)
    {
        sidebarCollapsed = collapsed; SidebarColumn.Width = new GridLength(collapsed ? 74 : 260);
        FullBrand.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible; CompactBrand.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        FooterDetails.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible; CompactFooter.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        PlanningGroupLabel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible; ProductionGroupLabel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        MasterDataGroupLabel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible; AdminGroupLabel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        foreach (var button in NavigationPanel.Children.OfType<Button>())
        {
            var parts = (button.Tag?.ToString() ?? string.Empty).Split('|', 2); if (parts.Length != 2) continue;
            button.Content = collapsed ? parts[0] : $"{parts[0]}   {parts[1]}"; button.ToolTip = parts[1];
            button.HorizontalContentAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            button.Padding = collapsed ? new Thickness(0) : new Thickness(13, 0, 13, 0); button.FontSize = collapsed ? 17 : 13;
        }
    }

    private void SetActiveNavigation(Button? active)
    {
        foreach (var button in NavigationPanel.Children.OfType<Button>()) { button.Background = Brushes.Transparent; button.Foreground = new SolidColorBrush(Color.FromRgb(217, 230, 242)); }
        activeNavButton = active; if (active is null) return; active.Background = (Brush)FindResource("PrimaryBrush"); active.Foreground = Brushes.White;
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e) { var dialog = new ChangePasswordWindow { Owner = this }; dialog.ShowDialog(); }

    private enum NavigationRoute { Dashboard, PlanningCalendar, DayPlanning, WeekPlanning, WorkTimeCalendar, ProductionOrders, ProductionActual, Analytics, Employees, Skills, Workstations, Shifts, Absences, UserAdmin, Settings }
    private sealed record NavigationEntry(NavigationRoute Route, string Title, string ButtonName, Func<object> CreateContent, DateTime? Date = null, int? EmployeeId = null);
}

using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;
using Produktionsplanung.App.Views;

namespace Produktionsplanung.App;

public partial class MainWindow : Window
{
    private readonly Stack<NavigationEntry> navigationHistory = new();
    private NavigationEntry? currentNavigation;
    private EmployeeQuickCardWindow? employeeQuickCardWindow;
    private bool sidebarCollapsed;
    private bool planningGroupCollapsed;
    private bool productionGroupCollapsed;
    private bool masterDataGroupCollapsed;
    private bool systemGroupCollapsed;
    private readonly DispatcherTimer inactivityTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private DateTime lastActivityUtc = DateTime.UtcNow;
    private bool sessionLocked;
    private bool bypassUnsavedChangesPrompt;
    private List<DashboardIssue> notificationIssues = new();

    public MainWindow()
    {
        InitializeComponent();
        ApplyRolePermissions();

        var preferences = AppSettingsService.LoadCurrentUserPreferences();
        planningGroupCollapsed = preferences.PlanningGroupCollapsed;
        productionGroupCollapsed = preferences.ProductionGroupCollapsed;
        masterDataGroupCollapsed = preferences.MasterDataGroupCollapsed;
        systemGroupCollapsed = preferences.SystemGroupCollapsed;
        SetSidebarCollapsed(preferences.SidebarCollapsed, persist: false);
        ApplyGroupVisibility();

        inactivityTimer.Tick += InactivityTimer_Tick;
        inactivityTimer.Start();
        InputManager.Current.PreProcessInput += InputManager_PreProcessInput;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;

        Navigate(CreateEntry(NavigationRoute.Dashboard), addToHistory: false);
        RefreshNotifications();
    }

    private void ApplyRolePermissions()
    {
        var user = SessionService.CurrentUser;
        SessionInfoBlock.Text = user is null ? "Nicht angemeldet" : $"{user.DisplayName} · {user.Role}";
        TopbarUserNameBlock.Text = user?.DisplayName ?? "Nicht angemeldet";
        TopbarRoleBlock.Text = user?.Role ?? "Keine Sitzung";
        TopbarInitialsBlock.Text = GetInitials(user?.DisplayName);
        VersionBlock.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";
        var canOperate = SessionService.IsPlannerOrAdmin;
        PlanningCalendarButton.IsEnabled = canOperate; DayPlanningButton.IsEnabled = canOperate; WeekPlanningButton.IsEnabled = canOperate;
        WorkTimeCalendarButton.IsEnabled = canOperate; ProductionOrdersButton.IsEnabled = canOperate; ManufacturingControlButton.IsEnabled = canOperate; ProductionActualButton.IsEnabled = canOperate;
        EmployeesButton.IsEnabled = canOperate; SkillsButton.IsEnabled = canOperate; WorkstationsButton.IsEnabled = canOperate;
        ShiftsButton.IsEnabled = canOperate; AbsencesButton.IsEnabled = canOperate;
        UserAdminButton.IsEnabled = SessionService.IsAdministrator; SettingsButton.IsEnabled = SessionService.IsAdministrator;
    }

    private static string GetInitials(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "SC";
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
    }

    private void ShowDashboard_Click(object sender, RoutedEventArgs e) => Navigate(CreateEntry(NavigationRoute.Dashboard));
    private void ShowPlanningCalendar_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.PlanningCalendar)); }
    private void ShowDayPlanning_Click(object sender, RoutedEventArgs e) => OpenDayPlanning(DateTime.Today);
    private void ShowWeekPlanning_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.WeekPlanning)); }
    private void ShowWorkTimeCalendar_Click(object sender, RoutedEventArgs e) => OpenWorkTimeCalendar();
    private void ShowProductionOrders_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.ProductionOrders)); }
    private void ShowManufacturingControl_Click(object sender, RoutedEventArgs e) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.ManufacturingControl)); }
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
    public void OpenProductionOrder(int productionOrderId) { if (SessionService.IsPlannerOrAdmin) Navigate(CreateEntry(NavigationRoute.ProductionOrders, productionOrderId: productionOrderId)); }

    public void OpenEmployeeQuickCard(int employeeId, DateTime? contextDate = null)
    {
        if (!SessionService.IsPlannerOrAdmin) return;
        if (employeeQuickCardWindow is not null) { employeeQuickCardWindow.Close(); employeeQuickCardWindow = null; }
        employeeQuickCardWindow = new EmployeeQuickCardWindow(employeeId, contextDate) { Owner = this };
        employeeQuickCardWindow.Closed += (_, _) => employeeQuickCardWindow = null;
        employeeQuickCardWindow.Show();
    }

    private NavigationEntry CreateEntry(
        NavigationRoute route,
        DateTime? date = null,
        int? employeeId = null,
        int? productionOrderId = null) => route switch
    {
        NavigationRoute.Dashboard => new(route, "Dashboard", nameof(DashboardButton), () => new DashboardView()),
        NavigationRoute.PlanningCalendar => new(route, "Planungskalender", nameof(PlanningCalendarButton), () => new PlanningCalendarView()),
        NavigationRoute.DayPlanning => new(route, $"Tagesplanung · {(date ?? DateTime.Today):dd.MM.yyyy}", nameof(DayPlanningButton), () => new DayPlanningView((date ?? DateTime.Today).Date), Date: date?.Date),
        NavigationRoute.WeekPlanning => new(route, "Wochenplanung", nameof(WeekPlanningButton), () => new WeekPlanningView()),
        NavigationRoute.WorkTimeCalendar => new(route, "Arbeitszeit / Betrieb", nameof(WorkTimeCalendarButton), () => new WorkTimeCalendarView()),
        NavigationRoute.ProductionOrders when productionOrderId.HasValue => new(route, "Produktionsaufträge", nameof(ProductionOrdersButton), () => new ProductionOrdersView(productionOrderId.Value), ProductionOrderId: productionOrderId),
        NavigationRoute.ProductionOrders => new(route, "Produktionsaufträge", nameof(ProductionOrdersButton), () => new ProductionOrdersView()),
        NavigationRoute.ManufacturingControl => new(route, "Fertigungssteuerung", nameof(ManufacturingControlButton), () => new ManufacturingControlView()),
        NavigationRoute.ProductionActual => new(route, "Ist-Produktion / OEE", nameof(ProductionActualButton), () => new ProductionActualView()),
        NavigationRoute.Analytics => new(route, "Auswertungen / KPIs", nameof(AnalyticsButton), () => new AnalyticsView()),
        NavigationRoute.Employees when employeeId.HasValue => new(route, "Mitarbeiter", nameof(EmployeesButton), () => new EmployeesView(employeeId.Value), EmployeeId: employeeId),
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
        if (!CanLeaveCurrentContent())
            return;

        if (addToHistory && currentNavigation is not null) navigationHistory.Push(currentNavigation);
        ShowEntry(entry);
    }

    private void ShowEntry(NavigationEntry entry)
    {
        ContentHost.Content = entry.GetContent();
        currentNavigation = entry;
        CurrentPageTitle.Text = entry.Title;
        SetActiveNavigation(FindName(entry.ButtonName) as Button);
        BackButton.IsEnabled = navigationHistory.Count > 0;
        RefreshNotifications();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (navigationHistory.Count == 0 || !CanLeaveCurrentContent()) return;
        ShowEntry(navigationHistory.Pop());
    }

    private bool CanLeaveCurrentContent()
    {
        if (bypassUnsavedChangesPrompt ||
            ContentHost.Content is not IUnsavedChangesAware dirtyAware ||
            !dirtyAware.HasUnsavedChanges)
            return true;

        var result = MessageBox.Show(
            this,
            $"Es gibt ungespeicherte Änderungen in „{dirtyAware.UnsavedChangesDescription}“.\n\n" +
            "Ja = speichern und fortfahren\nNein = Änderungen verwerfen\nAbbrechen = hier bleiben",
            "Ungespeicherte Änderungen",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
            return false;

        if (result == MessageBoxResult.No)
        {
            dirtyAware.DiscardChanges();
            return true;
        }

        return dirtyAware.TrySaveChanges();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!CanLeaveCurrentContent())
            e.Cancel = true;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        inactivityTimer.Stop();
        InputManager.Current.PreProcessInput -= InputManager_PreProcessInput;
    }

    private void InputManager_PreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (sessionLocked || !SessionService.IsAuthenticated)
            return;

        if (e.StagingItem.Input is KeyboardEventArgs or MouseEventArgs)
            lastActivityUtc = DateTime.UtcNow;
    }

    private void InactivityTimer_Tick(object? sender, EventArgs e)
    {
        if (sessionLocked || !SessionService.IsAuthenticated)
            return;

        var settings = AppSettingsService.Load();
        if (!settings.AutoLockEnabled)
        {
            lastActivityUtc = DateTime.UtcNow;
            return;
        }

        var timeoutMinutes = Math.Clamp(settings.AutoLockMinutes, 1, 240);
        if (DateTime.UtcNow - lastActivityUtc < TimeSpan.FromMinutes(timeoutMinutes))
            return;

        LockSession();
    }

    private void LockSession()
    {
        if (sessionLocked || SessionService.CurrentUser is null)
            return;

        sessionLocked = true;
        employeeQuickCardWindow?.Close();
        employeeQuickCardWindow = null;
        AuditService.Log("Sitzung gesperrt", "Session", SessionService.CurrentUser.Id.ToString(),
            "Automatische Sperre wegen Inaktivität.");

        try
        {
            var unlock = new SessionUnlockWindow { Owner = this };
            unlock.ShowDialog();
        }
        finally
        {
            sessionLocked = false;
            lastActivityUtc = DateTime.UtcNow;
            if (SessionService.IsAuthenticated)
                AuditService.Log("Sitzung entsperrt", "Session", SessionService.CurrentUser?.Id.ToString(), null);
        }
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e) => SetSidebarCollapsed(!sidebarCollapsed);

    private void SetSidebarCollapsed(bool collapsed, bool persist = true)
    {
        sidebarCollapsed = collapsed;
        SidebarColumn.Width = new GridLength(collapsed ? 74 : 260);
        FullBrand.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CompactBrand.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        FooterDetails.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CompactFooter.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;

        PlanningGroupHeader.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        ProductionGroupHeader.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        MasterDataGroupHeader.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SystemGroupHeader.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

        foreach (var button in NavigationPanel.Children.OfType<Button>().Where(x => x.Tag is not null))
        {
            button.Content = collapsed ? string.Empty : button.ToolTip?.ToString() ?? string.Empty;
            button.HorizontalContentAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            button.Padding = collapsed ? new Thickness(0) : new Thickness(9, 0, 9, 0);
        }

        ApplyGroupVisibility();
        if (persist)
            AppSettingsService.UpdateCurrentUserPreferences(preferences => preferences.SidebarCollapsed = collapsed);
    }

    private void TogglePlanningGroup_Click(object sender, RoutedEventArgs e)
    {
        planningGroupCollapsed = !planningGroupCollapsed;
        AppSettingsService.UpdateCurrentUserPreferences(preferences => preferences.PlanningGroupCollapsed = planningGroupCollapsed);
        ApplyGroupVisibility();
    }

    private void ToggleProductionGroup_Click(object sender, RoutedEventArgs e)
    {
        productionGroupCollapsed = !productionGroupCollapsed;
        AppSettingsService.UpdateCurrentUserPreferences(preferences => preferences.ProductionGroupCollapsed = productionGroupCollapsed);
        ApplyGroupVisibility();
    }

    private void ToggleMasterDataGroup_Click(object sender, RoutedEventArgs e)
    {
        masterDataGroupCollapsed = !masterDataGroupCollapsed;
        AppSettingsService.UpdateCurrentUserPreferences(preferences => preferences.MasterDataGroupCollapsed = masterDataGroupCollapsed);
        ApplyGroupVisibility();
    }

    private void ToggleSystemGroup_Click(object sender, RoutedEventArgs e)
    {
        systemGroupCollapsed = !systemGroupCollapsed;
        AppSettingsService.UpdateCurrentUserPreferences(preferences => preferences.SystemGroupCollapsed = systemGroupCollapsed);
        ApplyGroupVisibility();
    }

    private void ApplyGroupVisibility()
    {
        ApplyGroup(PlanningGroupHeader, "PLANUNG", planningGroupCollapsed,
            DashboardButton, PlanningCalendarButton, DayPlanningButton, WeekPlanningButton, WorkTimeCalendarButton);
        ApplyGroup(ProductionGroupHeader, "PRODUKTION", productionGroupCollapsed,
            ProductionOrdersButton, ManufacturingControlButton, ProductionActualButton, AnalyticsButton);
        ApplyGroup(MasterDataGroupHeader, "STAMMDATEN", masterDataGroupCollapsed,
            EmployeesButton, SkillsButton, WorkstationsButton, ShiftsButton, AbsencesButton);
        ApplyGroup(SystemGroupHeader, "SYSTEM", systemGroupCollapsed,
            UserAdminButton, SettingsButton);
    }

    private void ApplyGroup(Button header, string title, bool collapsed, params Button[] buttons)
    {
        header.Content = $"{(collapsed ? "▸" : "▾")}  {title}";
        var visibility = sidebarCollapsed || !collapsed ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in buttons)
            button.Visibility = visibility;
    }

    private void SetActiveNavigation(Button? active)
    {
        foreach (var button in NavigationPanel.Children.OfType<Button>().Where(x => x.Tag is not null))
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.Foreground = new SolidColorBrush(Color.FromRgb(217, 230, 242));
            button.FontWeight = FontWeights.Medium;
        }
        if (active is null) return;
        active.Background = (Brush)FindResource("SidebarActiveBrush");
        active.BorderBrush = (Brush)FindResource("PrimaryBrush");
        active.Foreground = Brushes.White;
        active.FontWeight = FontWeights.SemiBold;
    }

    private void RefreshNotifications()
    {
        try
        {
            var dashboard = new DashboardViewModel();
            notificationIssues = dashboard.Issues.ToList();
            NotificationCountText.Text = notificationIssues.Count > 99 ? "99+" : notificationIssues.Count.ToString();
            NotificationCountBadge.Visibility = notificationIssues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NotificationButton.ToolTip = notificationIssues.Count == 0
                ? "Keine offenen Hinweise"
                : $"{notificationIssues.Count} offene Hinweise";
        }
        catch
        {
            notificationIssues = new List<DashboardIssue>();
            NotificationCountBadge.Visibility = Visibility.Collapsed;
            NotificationButton.ToolTip = "Benachrichtigungen konnten nicht geladen werden";
        }
    }

    private void NotificationButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshNotifications();
        var menu = new ContextMenu { PlacementTarget = NotificationButton };

        if (notificationIssues.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "✓ Keine offenen Hinweise", IsEnabled = false });
        }
        else
        {
            foreach (var issue in notificationIssues.Take(12))
            {
                var prefix = issue.Severity == "Rot" ? "●" : "▲";
                var item = new MenuItem
                {
                    Header = $"{prefix}  {issue.Title}\n    {issue.Message}",
                    Tag = issue
                };
                item.Click += NotificationItem_Click;
                menu.Items.Add(item);
            }

            if (notificationIssues.Count > 12)
            {
                menu.Items.Add(new Separator());
                var moreItem = new MenuItem
                {
                    Header = $"+ {notificationIssues.Count - 12} weitere Hinweise im Dashboard",
                    Tag = new DashboardIssue { Route = "Dashboard" }
                };
                moreItem.Click += NotificationItem_Click;
                menu.Items.Add(moreItem);
            }
        }

        NotificationButton.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void NotificationItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: DashboardIssue issue })
            return;

        switch (issue.Route)
        {
            case "DayPlanning" when SessionService.IsPlannerOrAdmin:
                OpenDayPlanning(issue.Date ?? DateTime.Today);
                break;
            case "ProductionOrders" when SessionService.IsPlannerOrAdmin && issue.EntityId.HasValue:
                OpenProductionOrder(issue.EntityId.Value);
                break;
            case "ProductionOrders" when SessionService.IsPlannerOrAdmin:
                Navigate(CreateEntry(NavigationRoute.ProductionOrders));
                break;
            case "Settings" when SessionService.IsAdministrator:
                Navigate(CreateEntry(NavigationRoute.Settings));
                break;
            default:
                Navigate(CreateEntry(NavigationRoute.Dashboard));
                break;
        }
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ChangePasswordWindow { Owner = this };
        dialog.ShowDialog();
    }

    private enum NavigationRoute
    {
        Dashboard, PlanningCalendar, DayPlanning, WeekPlanning, WorkTimeCalendar, ProductionOrders, ManufacturingControl,
        ProductionActual, Analytics, Employees, Skills, Workstations, Shifts, Absences, UserAdmin, Settings
    }

    private sealed record NavigationEntry(
        NavigationRoute Route,
        string Title,
        string ButtonName,
        Func<object> CreateContent,
        DateTime? Date = null,
        int? EmployeeId = null,
        int? ProductionOrderId = null)
    {
        private object? content;
        public object GetContent() => content ??= CreateContent();
    }
}

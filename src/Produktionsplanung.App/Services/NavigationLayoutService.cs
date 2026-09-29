using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Produktionsplanung.App.Views;

namespace Produktionsplanung.App.Services;

internal static class NavigationLayoutService
{
    [ModuleInitializer]
    internal static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window.FindName("NavigationPanel") is not StackPanel navigation)
            return;

        if (navigation.Children.OfType<FrameworkElement>().Any(x => x.Name == "SettingsNavigationSeparator"))
            return;

        var planningHeader = Find<Button>(window, "PlanningGroupHeader");
        var workTime = Find<Button>(window, "WorkTimeCalendarButton");
        var absences = Find<Button>(window, "AbsencesButton");
        var settings = Find<Button>(window, "SettingsButton");
        var sidebarToggle = Find<Button>(window, "SidebarToggleButton");

        Remove(navigation, Find<Button>(window, "MasterDataGroupHeader"));
        Remove(navigation, Find<Button>(window, "ArticlesButton"));
        Remove(navigation, Find<Button>(window, "EmployeesButton"));
        Remove(navigation, Find<Button>(window, "WorkstationsButton"));
        Remove(navigation, Find<Button>(window, "SystemGroupHeader"));
        Remove(navigation, Find<Button>(window, "UserAdminButton"));

        SetPrivateBool(window, "masterDataGroupCollapsed", false);
        SetPrivateBool(window, "systemGroupCollapsed", false);

        if (absences is not null && workTime is not null)
        {
            navigation.Children.Remove(absences);
            var targetIndex = Math.Min(navigation.Children.Count, navigation.Children.IndexOf(workTime) + 1);
            navigation.Children.Insert(targetIndex, absences);
        }

        if (settings is not null)
        {
            navigation.Children.Remove(settings);
            var separator = new Border
            {
                Name = "SettingsNavigationSeparator",
                Height = 1,
                Margin = new Thickness(8, 13, 8, 9),
                Background = new SolidColorBrush(Color.FromRgb(39, 69, 95))
            };
            navigation.Children.Add(separator);
            navigation.Children.Add(settings);
            settings.ToolTip = "Einstellungen & Verwaltung";
            settings.IsEnabled = SessionService.IsPlannerOrAdmin;
            settings.Click += (_, _) =>
            {
                if (SessionService.IsPlannerOrAdmin && !SessionService.IsAdministrator)
                    NavigateToSettings(window);
            };
        }

        if (planningHeader is not null)
            planningHeader.Click += (_, _) => window.Dispatcher.BeginInvoke(new Action(() => ApplyMovedVisibility(window)));
        if (sidebarToggle is not null)
            sidebarToggle.Click += (_, _) => window.Dispatcher.BeginInvoke(new Action(() => ApplyMovedVisibility(window)));

        if (window.FindName("ContentHost") is ContentControl contentHost && settings is not null)
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl));
            descriptor?.AddValueChanged(contentHost, (_, _) =>
            {
                if (contentHost.Content is SettingsView or EmployeesView or WorkplacesShiftsView or UserAdminView or ArticlesView)
                    SetActiveNavigation(window, settings);
            });
        }

        ApplyMovedVisibility(window);
    }

    private static T? Find<T>(MainWindow window, string name) where T : FrameworkElement =>
        window.FindName(name) as T;

    private static void Remove(Panel panel, UIElement? element)
    {
        if (element is not null && panel.Children.Contains(element))
            panel.Children.Remove(element);
    }

    private static void ApplyMovedVisibility(MainWindow window)
    {
        var absences = Find<Button>(window, "AbsencesButton");
        var settings = Find<Button>(window, "SettingsButton");
        var sidebarCollapsed = GetPrivateBool(window, "sidebarCollapsed");
        var planningCollapsed = GetPrivateBool(window, "planningGroupCollapsed");

        if (absences is not null)
            absences.Visibility = sidebarCollapsed || !planningCollapsed ? Visibility.Visible : Visibility.Collapsed;
        if (settings is not null)
            settings.Visibility = Visibility.Visible;
    }

    private static void NavigateToSettings(MainWindow window)
    {
        try
        {
            var routeType = typeof(MainWindow).GetNestedType("NavigationRoute", BindingFlags.NonPublic);
            var createEntry = typeof(MainWindow).GetMethod("CreateEntry", BindingFlags.Instance | BindingFlags.NonPublic);
            var navigate = typeof(MainWindow).GetMethod("Navigate", BindingFlags.Instance | BindingFlags.NonPublic);
            if (routeType is null || createEntry is null || navigate is null)
                return;

            var route = Enum.Parse(routeType, "Settings");
            var entry = createEntry.Invoke(window, new object?[] { route, null, null, null });
            if (entry is not null)
                navigate.Invoke(window, new[] { entry, (object)true });
        }
        catch
        {
            // Navigation remains protected by the existing MainWindow permissions if reflection is unavailable.
        }
    }

    private static void SetActiveNavigation(MainWindow window, Button button)
    {
        try
        {
            typeof(MainWindow)
                .GetMethod("SetActiveNavigation", BindingFlags.Instance | BindingFlags.NonPublic)?
                .Invoke(window, new object?[] { button });
        }
        catch
        {
            // Cosmetic only; navigation itself is unaffected.
        }
    }

    private static void SetPrivateBool(MainWindow window, string fieldName, bool value) =>
        typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(window, value);

    private static bool GetPrivateBool(MainWindow window, string fieldName) =>
        typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as bool? ?? false;
}

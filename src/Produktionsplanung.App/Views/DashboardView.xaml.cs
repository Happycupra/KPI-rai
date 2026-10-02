using System.Windows;
using System.Windows.Controls;
using Produktionsplanung.App;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class DashboardView : UserControl
{
    private string? selectedDailyMetric;
    private string? dailyDetailRoute;

    public DashboardView()
    {
        InitializeComponent();
        DataContext = new DashboardViewModel();
        Loaded += (_, _) => ((DashboardViewModel)DataContext).RefreshCommand.Execute(null);
    }

    private void RefreshBatches_Click(object sender, RoutedEventArgs e)
    {
        ((BatchBrowserViewModel)Batches.DataContext).Refresh();
        if (!string.IsNullOrWhiteSpace(selectedDailyMetric))
            ShowDailyMetricDetails(selectedDailyMetric, allowToggle: false);
    }

    private void OpenArticles_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenArticles();
    private void OpenArchive_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenBatchArchive();
    private MainWindow? HostWindow => Window.GetWindow(this) as MainWindow;
    private void OpenPlanning_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenPlanningCalendar();
    private void OpenOrders_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenProductionOrders();
    private void OpenControl_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenManufacturingControl();
    private void OpenActual_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenProductionActual();

    private void DailyMetric_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string metric })
            ShowDailyMetricDetails(metric, allowToggle: true);
    }

    private void ShowDailyMetricDetails(string metric, bool allowToggle)
    {
        if (DataContext is not DashboardViewModel vm)
            return;

        if (allowToggle && selectedDailyMetric == metric && DailyDetailCard.Visibility == Visibility.Visible)
        {
            DailyDetailCard.Visibility = Visibility.Collapsed;
            selectedDailyMetric = null;
            dailyDetailRoute = null;
            return;
        }

        selectedDailyMetric = metric;
        DailyDetailCard.Visibility = Visibility.Visible;

        switch (metric)
        {
            case "Active":
                SetDailyDetails(
                    "Aktive Mitarbeitende",
                    "Alle aktuell aktiven Mitarbeitenden. Verfügbarkeit berücksichtigt Abwesenheiten; die Einsatzplanung zeigt, wer heute bereits zugeordnet ist.",
                    "Aktiv", vm.ActiveEmployees.ToString(),
                    "Verfügbar", vm.AvailableEmployees.ToString(),
                    "Abwesend", vm.AbsentEmployees.ToString(),
                    "Tagesplanung öffnen", "Planning");
                break;

            case "Available":
                SetDailyDetails(
                    "Verfügbare Mitarbeitende",
                    "Aktive Mitarbeitende ohne heutige Abwesenheit. Die freie Kapazität ergibt sich aus verfügbar minus bereits eingeplant.",
                    "Verfügbar", vm.AvailableEmployees.ToString(),
                    "Eingeplant", vm.PlannedEmployees.ToString(),
                    "Noch frei", Math.Max(0, vm.AvailableEmployees - vm.PlannedEmployees).ToString(),
                    "Tagesplanung öffnen", "Planning");
                break;

            case "Planned":
                SetDailyDetails(
                    "Heute eingeplant",
                    "So viele aktive Mitarbeitende sind heute bereits einer Planung zugeordnet. Personalabdeckung und Unterbesetzung zeigen den aktuellen Planungszustand.",
                    "Eingeplant", vm.PlannedEmployees.ToString(),
                    "Personalabdeckung", $"{vm.PersonnelCoveragePercent}%",
                    "Unterbesetzt", vm.UnderstaffedOrders.ToString(),
                    "Tagesplanung prüfen", "Planning");
                break;

            case "Absent":
                SetDailyDetails(
                    "Heute abwesend",
                    "Abwesenheiten reduzieren die verfügbare Kapazität für den heutigen Tag. In der Tagesplanung kannst du die Auswirkungen direkt prüfen.",
                    "Abwesend", vm.AbsentEmployees.ToString(),
                    "Aktiv", vm.ActiveEmployees.ToString(),
                    "Verfügbar", vm.AvailableEmployees.ToString(),
                    "Tagesplanung prüfen", "Planning");
                break;

            case "Completed":
                SetDailyDetails(
                    "Heute abgeschlossen",
                    "Heute abgeschlossene Produktionsaufträge im Verhältnis zu den geplanten und aktuell laufenden Aufträgen.",
                    "Abgeschlossen", vm.CompletedOrdersToday.ToString(),
                    "Heute geplant", vm.OrdersToday.ToString(),
                    "Laufend", vm.RunningOrders.ToString(),
                    "Abgeschlossene Chargen öffnen", "Archive");
                break;

            case "Understaffed":
                SetDailyDetails(
                    "Unterbesetzte Aufträge",
                    "Aufträge, bei denen für mindestens eine heutige Produktionsschicht weniger Personal eingeplant ist als benötigt.",
                    "Unterbesetzt", vm.UnderstaffedOrders.ToString(),
                    "Personalabdeckung", $"{vm.PersonnelCoveragePercent}%",
                    "Eingeplant", vm.PlannedEmployees.ToString(),
                    "Tagesplanung korrigieren", "Planning");
                break;

            default:
                DailyDetailCard.Visibility = Visibility.Collapsed;
                selectedDailyMetric = null;
                dailyDetailRoute = null;
                break;
        }
    }

    private void SetDailyDetails(
        string title,
        string description,
        string stat1Label,
        string stat1Value,
        string stat2Label,
        string stat2Value,
        string stat3Label,
        string stat3Value,
        string actionText,
        string route)
    {
        DailyDetailTitle.Text = title;
        DailyDetailDescription.Text = description;
        DailyDetailStat1Label.Text = stat1Label;
        DailyDetailStat1Value.Text = stat1Value;
        DailyDetailStat2Label.Text = stat2Label;
        DailyDetailStat2Value.Text = stat2Value;
        DailyDetailStat3Label.Text = stat3Label;
        DailyDetailStat3Value.Text = stat3Value;
        DailyDetailActionButton.Content = actionText;
        dailyDetailRoute = route;
    }

    private void CloseDailyDetails_Click(object sender, RoutedEventArgs e)
    {
        DailyDetailCard.Visibility = Visibility.Collapsed;
        selectedDailyMetric = null;
        dailyDetailRoute = null;
    }

    private void DailyDetailAction_Click(object sender, RoutedEventArgs e)
    {
        if (HostWindow is null)
            return;

        switch (dailyDetailRoute)
        {
            case "Planning":
                HostWindow.OpenDayPlanning(DateTime.Today);
                break;
            case "Archive":
                HostWindow.OpenBatchArchive();
                break;
            case "Orders":
                HostWindow.OpenProductionOrders();
                break;
        }
    }

    private void OpenIssue_Click(object sender, RoutedEventArgs e)
    {
        if (HostWindow is null || (sender as FrameworkElement)?.DataContext is not DashboardIssue issue) return;
        if (issue.Route == "DayPlanning")
        {
            HostWindow.OpenDayPlanning(issue.Date ?? DateTime.Today);
        }
        else if (issue.Route == "ProductionOrders")
        {
            if (issue.EntityId.HasValue) HostWindow.OpenProductionOrder(issue.EntityId.Value);
            else HostWindow.OpenProductionOrders();
        }
        else if (issue.Route == "Settings")
        {
            HostWindow.OpenSettings();
        }
        else if (issue.Route == "ShiftHandover")
        {
            HostWindow.OpenShiftHandover(issue.EntityId);
        }
    }
}

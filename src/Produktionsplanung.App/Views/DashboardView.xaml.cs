using System.Windows;
using System.Windows.Controls;
using Produktionsplanung.App;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        DataContext = new DashboardViewModel();
        Loaded += (_, _) => ((DashboardViewModel)DataContext).RefreshCommand.Execute(null);
    }

    private void RefreshBatches_Click(object sender, RoutedEventArgs e) => ((BatchBrowserViewModel)Batches.DataContext).Refresh();
    private void OpenArticles_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenArticles();
    private void OpenArchive_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenBatchArchive();
    private MainWindow? HostWindow => Window.GetWindow(this) as MainWindow;
    private void OpenPlanning_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenPlanningCalendar();
    private void OpenOrders_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenProductionOrders();
    private void OpenControl_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenManufacturingControl();
    private void OpenActual_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenProductionActual();

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
    }
}

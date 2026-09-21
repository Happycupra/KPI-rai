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
    }

    private MainWindow? HostWindow => Window.GetWindow(this) as MainWindow;
    private void OpenPlanning_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenPlanningCalendar();
    private void OpenOrders_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenProductionOrders();
    private void OpenControl_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenManufacturingControl();
    private void OpenActual_Click(object sender, RoutedEventArgs e) => HostWindow?.OpenProductionActual();
}

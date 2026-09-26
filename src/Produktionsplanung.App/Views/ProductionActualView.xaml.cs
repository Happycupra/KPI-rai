using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class ProductionActualView : UserControl
{
    private readonly ProductionActualViewModel viewModel;

    public ProductionActualView(int? orderId = null)
    {
        InitializeComponent();
        viewModel = new ProductionActualViewModel();
        DataContext = viewModel;
        if (orderId.HasValue) viewModel.FocusOrder(orderId.Value);
    }

    private void ActualRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: ProductionActualRow row } gridRow)
        {
            gridRow.IsSelected = true;
            viewModel.SelectedActual = row;
        }
    }

    private static ProductionActualRow? ContextActual(object sender)
    {
        if (sender is not MenuItem item ||
            item.Parent is not ContextMenu menu ||
            menu.PlacementTarget is not DataGridRow { DataContext: ProductionActualRow row })
            return null;
        return row;
    }

    private MainWindow? Host => Window.GetWindow(this) as MainWindow;

    private void ActualBatchContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextActual(sender) is { } row) Host?.OpenBatch(row.ProductionOrderId);
    }

    private void ActualOrderContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextActual(sender) is { } row) Host?.OpenProductionOrder(row.ProductionOrderId);
    }

    private void ActualControlContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextActual(sender) is { } row) Host?.OpenManufacturingControl(row.ProductionOrderId);
    }

    private void ActualArticlesContext_Click(object sender, RoutedEventArgs e) => Host?.OpenArticles();
}

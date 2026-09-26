using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class ProductionOrdersView : UserControl, IUnsavedChangesAware
{
    private readonly ProductionOrderManagementViewModel viewModel;
    private string baseline = string.Empty;
    private bool? compactLayout;

    public ProductionOrdersView(int? selectedOrderId = null)
    {
        InitializeComponent();
        viewModel = new ProductionOrderManagementViewModel();
        DataContext = viewModel;

        if (selectedOrderId.HasValue)
            viewModel.SelectedOrder = viewModel.Orders.FirstOrDefault(x => x.Id == selectedOrderId.Value);

        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        OrderEditorPanel.IsEnabled = SessionService.IsPlannerOrAdmin;
        CaptureBaseline();
        Loaded += (_, _) => UpdateResponsiveLayout(ActualWidth);
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedOrder is { } order) (Window.GetWindow(this) as MainWindow)?.OpenBatch(order.Id);
    }
    private void NewBatch_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.CreateBatch();

    private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateResponsiveLayout(e.NewSize.Width);

    private void OrderRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow { DataContext: ProductionOrderRow row } gridRow)
            return;
        gridRow.IsSelected = true;
        viewModel.SelectedOrder = row;
    }

    private static ProductionOrderRow? ContextOrder(object sender)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu contextMenu ||
            contextMenu.PlacementTarget is not DataGridRow { DataContext: ProductionOrderRow row })
            return null;
        return row;
    }

    private void EditOrderContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextOrder(sender) is { } row)
        {
            viewModel.SelectedOrder = row;
            OrderEditorPanel.BringIntoView();
        }
    }

    private void OpenBatchContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextOrder(sender) is { } row)
            (Window.GetWindow(this) as MainWindow)?.OpenBatch(row.Id);
    }

    private void OpenControlContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextOrder(sender) is { } row)
            (Window.GetWindow(this) as MainWindow)?.OpenManufacturingControl(row.Id);
    }

    private void OpenActualContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextOrder(sender) is { } row)
            (Window.GetWindow(this) as MainWindow)?.OpenProductionActual(row.Id);
    }

    private void OpenArticlesContext_Click(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.OpenArticles();

    private void DeleteOrderContext_Click(object sender, RoutedEventArgs e)
    {
        if (ContextOrder(sender) is not { } row)
            return;
        viewModel.SelectedOrder = row;
        viewModel.DeleteCommand.Execute(null);
    }

    private void UpdateResponsiveLayout(double width)
    {
        var compact = width < 1100;
        if (compactLayout == compact)
            return;

        compactLayout = compact;
        if (compact)
        {
            MasterDetailGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            MasterDetailGrid.ColumnDefinitions[1].Width = new GridLength(0);
            MasterDetailGrid.RowDefinitions[0].Height = new GridLength(55, GridUnitType.Star);
            MasterDetailGrid.RowDefinitions[1].Height = new GridLength(45, GridUnitType.Star);

            Grid.SetRow(OrdersListPanel, 0);
            Grid.SetColumn(OrdersListPanel, 0);
            OrdersListPanel.Margin = new Thickness(0, 0, 0, 12);

            Grid.SetRow(OrderEditorPanel, 1);
            Grid.SetColumn(OrderEditorPanel, 0);
            OrderEditorPanel.Margin = new Thickness(0);
        }
        else
        {
            MasterDetailGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            MasterDetailGrid.ColumnDefinitions[1].Width = new GridLength(430);
            MasterDetailGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            MasterDetailGrid.RowDefinitions[1].Height = new GridLength(0);

            Grid.SetRow(OrdersListPanel, 0);
            Grid.SetColumn(OrdersListPanel, 0);
            OrdersListPanel.Margin = new Thickness(0, 0, 18, 0);

            Grid.SetRow(OrderEditorPanel, 0);
            Grid.SetColumn(OrderEditorPanel, 1);
            OrderEditorPanel.Margin = new Thickness(0);
        }
    }

    public bool HasUnsavedChanges => baseline != BuildSnapshot();
    public string UnsavedChangesDescription => "Produktionsauftrag";

    public bool TrySaveChanges()
    {
        viewModel.SaveCommand.Execute(null);
        return !HasUnsavedChanges;
    }

    public void DiscardChanges()
    {
        var id = viewModel.SelectedOrder?.Id;
        if (id.HasValue)
        {
            viewModel.SelectedOrder = null;
            viewModel.SelectedOrder = viewModel.Orders.FirstOrDefault(x => x.Id == id.Value);
        }
        else
        {
            viewModel.NewOrderCommand.Execute(null);
        }
        CaptureBaseline();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProductionOrderManagementViewModel.SelectedOrder))
            CaptureBaseline();
    }

    private void CaptureBaseline() => baseline = BuildSnapshot();

    private string BuildSnapshot() => string.Join("\u001f",
        viewModel.SelectedOrder?.Id ?? 0,
        viewModel.OrderNumber,
        viewModel.Product,
        viewModel.ArticleNumber,
        viewModel.BatchNumber,
        viewModel.Description,
        viewModel.Quantity,
        viewModel.Unit,
        viewModel.Priority,
        viewModel.PlannedDate.Date,
        viewModel.SelectedWorkstation?.Id ?? 0,
        viewModel.SelectedShift?.Id ?? 0,
        viewModel.PlannedShiftCount,
        viewModel.RequiredStaff,
        viewModel.Status,
        viewModel.Comment);
}

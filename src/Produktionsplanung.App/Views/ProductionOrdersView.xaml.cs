using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
        CaptureBaseline();
        Loaded += (_, _) => UpdateResponsiveLayout(ActualWidth);
    }

    private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateResponsiveLayout(e.NewSize.Width);

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

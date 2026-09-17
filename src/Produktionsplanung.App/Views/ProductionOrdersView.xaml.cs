using System.ComponentModel;
using System.Windows.Controls;
using Produktionsplanung.App.Services;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class ProductionOrdersView : UserControl, IUnsavedChangesAware
{
    private readonly ProductionOrderManagementViewModel viewModel;
    private string baseline = string.Empty;

    public ProductionOrdersView(int? selectedOrderId = null)
    {
        InitializeComponent();
        viewModel = new ProductionOrderManagementViewModel();
        DataContext = viewModel;

        if (selectedOrderId.HasValue)
            viewModel.SelectedOrder = viewModel.Orders.FirstOrDefault(x => x.Id == selectedOrderId.Value);

        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        CaptureBaseline();
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

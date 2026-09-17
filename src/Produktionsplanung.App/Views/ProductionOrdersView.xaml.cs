using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class ProductionOrdersView : UserControl
{
    public ProductionOrdersView(int? selectedOrderId = null)
    {
        InitializeComponent();
        var viewModel = new ProductionOrderManagementViewModel();
        DataContext = viewModel;

        if (selectedOrderId.HasValue)
            viewModel.SelectedOrder = viewModel.Orders.FirstOrDefault(x => x.Id == selectedOrderId.Value);
    }
}

using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class ProductionActualView : UserControl
{
    public ProductionActualView(int? orderId = null)
    {
        InitializeComponent();
        var vm = new ProductionActualViewModel();
        DataContext = vm;
        if (orderId.HasValue) vm.FocusOrder(orderId.Value);
    }
}

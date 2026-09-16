using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class ProductionOrdersView : UserControl
{
    public ProductionOrdersView()
    {
        InitializeComponent();
        DataContext = new ProductionOrderManagementViewModel();
    }
}

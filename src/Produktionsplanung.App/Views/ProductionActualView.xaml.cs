using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class ProductionActualView : UserControl
{
    public ProductionActualView()
    {
        InitializeComponent();
        DataContext = new ProductionActualViewModel();
    }
}

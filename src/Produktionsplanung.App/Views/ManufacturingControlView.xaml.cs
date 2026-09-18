using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class ManufacturingControlView : UserControl
{
    public ManufacturingControlView()
    {
        InitializeComponent();
        DataContext = new ManufacturingControlViewModel();
    }
}

using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class ShiftHandoverView : UserControl
{
    public ShiftHandoverView(int? selectedHandoverId = null)
    {
        InitializeComponent();
        DataContext = new ShiftHandoverViewModel(selectedHandoverId);
    }
}

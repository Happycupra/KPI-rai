using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class ShiftsView : UserControl
{
    public ShiftsView()
    {
        InitializeComponent();
        DataContext = new ShiftManagementViewModel();
    }
}

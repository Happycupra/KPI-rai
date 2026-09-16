using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class DayPlanningView : UserControl
{
    public DayPlanningView()
    {
        InitializeComponent();
        DataContext = new DayPlanningViewModel();
    }
}

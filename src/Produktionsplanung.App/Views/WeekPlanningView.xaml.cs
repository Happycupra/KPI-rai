using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class WeekPlanningView : UserControl
{
    public WeekPlanningView()
    {
        InitializeComponent();
        DataContext = new WeekPlanningViewModel();
    }
}

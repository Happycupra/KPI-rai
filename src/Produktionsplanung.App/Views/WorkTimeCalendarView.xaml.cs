using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class WorkTimeCalendarView : UserControl
{
    public WorkTimeCalendarView()
    {
        InitializeComponent();
        DataContext = new WorkTimeCalendarViewModel();
    }
}

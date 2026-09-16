using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class WorkstationsView : UserControl
{
    public WorkstationsView()
    {
        InitializeComponent();
        DataContext = new WorkstationManagementViewModel();
    }
}

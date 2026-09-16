using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class AbsencesView : UserControl
{
    public AbsencesView()
    {
        InitializeComponent();
        DataContext = new AbsenceManagementViewModel();
    }
}

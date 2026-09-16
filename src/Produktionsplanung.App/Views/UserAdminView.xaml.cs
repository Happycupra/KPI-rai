using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class UserAdminView : UserControl
{
    public UserAdminView()
    {
        InitializeComponent();
        DataContext = new UserAdminViewModel();
    }
}

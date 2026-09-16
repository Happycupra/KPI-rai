using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel();
    }
}

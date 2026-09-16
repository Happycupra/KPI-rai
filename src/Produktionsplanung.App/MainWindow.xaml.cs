using System.Windows;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new DashboardViewModel();
    }
}

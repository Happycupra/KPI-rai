using System.Windows;
using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class PlanningCalendarView : UserControl
{
    private readonly PlanningCalendarViewModel viewModel;

    public PlanningCalendarView()
    {
        InitializeComponent();
        viewModel = new PlanningCalendarViewModel();
        DataContext = viewModel;
    }

    private void SelectCalendarDate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateTime date })
            return;

        viewModel.SelectDate(date);
    }

    private void OpenDayPlanning_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mainWindow)
            mainWindow.OpenDayPlanning(viewModel.SelectedDate);
    }
}

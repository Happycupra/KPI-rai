using System.ComponentModel;
using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class DayPlanningView : UserControl
{
    public DayPlanningView()
    {
        InitializeComponent();
        var viewModel = new DayPlanningViewModel();
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = viewModel;
        viewModel.RefreshProductionOrderCoverage();
    }

    private static void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DayPlanningViewModel viewModel) return;
        if (e.PropertyName is nameof(DayPlanningViewModel.SelectedDate) or nameof(DayPlanningViewModel.StatusMessage))
            viewModel.RefreshProductionOrderCoverage();
    }
}

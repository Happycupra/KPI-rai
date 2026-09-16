using System.ComponentModel;
using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class WeekPlanningView : UserControl
{
    public WeekPlanningView()
    {
        InitializeComponent();
        var viewModel = new WeekPlanningViewModel();
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = viewModel;
        viewModel.RefreshProductionOrderCoverage();
    }

    private static void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not WeekPlanningViewModel viewModel) return;
        if (e.PropertyName is nameof(WeekPlanningViewModel.WeekStart) or nameof(WeekPlanningViewModel.StatusMessage))
            viewModel.RefreshProductionOrderCoverage();
    }
}

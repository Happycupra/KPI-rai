using System.ComponentModel;
using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class DayPlanningView : UserControl
{
    public DayPlanningView() : this(DateTime.Today)
    {
    }

    public DayPlanningView(DateTime initialDate)
    {
        InitializeComponent();
        var viewModel = new DayPlanningViewModel
        {
            SelectedDate = initialDate.Date
        };
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = viewModel;
        RefreshSupplementalPlanningData(viewModel);
    }

    private static void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DayPlanningViewModel viewModel) return;
        if (e.PropertyName is nameof(DayPlanningViewModel.SelectedDate) or nameof(DayPlanningViewModel.StatusMessage))
            RefreshSupplementalPlanningData(viewModel);
    }

    private static void RefreshSupplementalPlanningData(DayPlanningViewModel viewModel)
    {
        viewModel.RefreshProductionOrderCoverage();
        viewModel.RefreshSkillAlerts();
        viewModel.RefreshOperatingCalendarAlert();
        viewModel.RefreshEmployeeSuggestions();
    }
}

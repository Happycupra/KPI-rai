using System.ComponentModel;
using System.Windows.Controls;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Views;

public partial class AnalyticsView : UserControl
{
    private readonly AnalyticsViewModel viewModel;

    public AnalyticsView()
    {
        InitializeComponent();
        viewModel = new AnalyticsViewModel();
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        viewModel.RefreshOeeAnalytics();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnalyticsViewModel.PeriodText))
            viewModel.RefreshOeeAnalytics();
    }
}

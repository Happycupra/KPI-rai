using System.Collections.ObjectModel;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class DayPlanningViewModel
{
    public ObservableCollection<ProductionOrderCoverageRow> ProductionOrderCoverage { get; } = new();

    public void RefreshProductionOrderCoverage()
    {
        ProductionOrderCoverage.Clear();
        foreach (var row in ProductionOrderCoverageService.Load(SelectedDate, SelectedDate))
            ProductionOrderCoverage.Add(row);
    }
}

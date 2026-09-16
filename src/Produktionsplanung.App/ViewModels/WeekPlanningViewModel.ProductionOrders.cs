using System.Collections.ObjectModel;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class WeekPlanningViewModel
{
    public ObservableCollection<ProductionOrderCoverageRow> ProductionOrderCoverage { get; } = new();

    public void RefreshProductionOrderCoverage()
    {
        ProductionOrderCoverage.Clear();
        foreach (var row in ProductionOrderCoverageService.Load(WeekStart, WeekStart.AddDays(6)))
            ProductionOrderCoverage.Add(row);
    }
}

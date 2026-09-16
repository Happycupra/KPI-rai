using System.Collections.ObjectModel;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class WeekPlanningViewModel
{
    private const string OrderAlertPrefix = "[Auftrag] ";

    public ObservableCollection<ProductionOrderCoverageRow> ProductionOrderCoverage { get; } = new();

    public void RefreshProductionOrderCoverage()
    {
        ProductionOrderCoverage.Clear();
        foreach (var row in ProductionOrderCoverageService.Load(WeekStart, WeekStart.AddDays(6)))
            ProductionOrderCoverage.Add(row);

        for (var i = Alerts.Count - 1; i >= 0; i--)
        {
            if (Alerts[i].Message.StartsWith(OrderAlertPrefix, StringComparison.Ordinal))
                Alerts.RemoveAt(i);
        }

        foreach (var row in ProductionOrderCoverage.Where(x => x.PlannedStaff < x.RequiredStaff))
        {
            Alerts.Add(new PlanningAlert
            {
                Severity = "Rot",
                Message = $"{OrderAlertPrefix}{row.Date:ddd dd.MM.} · {row.OrderNumber} · {row.Product}: {row.WorkstationName} / {row.ShiftName} benötigt {row.RequiredStaff}, eingeplant sind {row.PlannedStaff}."
            });
        }
    }
}

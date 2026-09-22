using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public static class ShiftHandoverService
{
    public static IReadOnlyList<ShiftHandover> GetOpen(DateTime? date = null)
    {
        using var db = new AppDbContext();
        var query = db.ShiftHandovers.AsNoTracking()
            .Include(x => x.FromShift).Include(x => x.ToShift)
            .Include(x => x.Workstation).Include(x => x.ProductionOrder)
            .Where(x => x.Status != "Erledigt");
        if (date.HasValue) query = query.Where(x => x.HandoverDate.Date == date.Value.Date);
        return query.OrderByDescending(x => x.Priority == "Kritisch")
            .ThenByDescending(x => x.Priority == "Hoch")
            .ThenBy(x => x.HandoverDate).ThenBy(x => x.CreatedAtUtc).ToList();
    }

    public static ShiftHandover Create(DateTime date, int? fromShiftId, int? toShiftId, int? workstationId,
        int? productionOrderId, string priority, string subject, string details)
    {
        if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("Betreff ist erforderlich.", nameof(subject));
        if (string.IsNullOrWhiteSpace(details)) throw new ArgumentException("Übergabeinformation ist erforderlich.", nameof(details));
        using var db = new AppDbContext();
        ValidateReferences(db, fromShiftId, toShiftId, workstationId, productionOrderId);
        var item = new ShiftHandover {
            HandoverDate = date.Date, FromShiftId = fromShiftId, ToShiftId = toShiftId,
            WorkstationId = workstationId, ProductionOrderId = productionOrderId,
            Priority = NormalizePriority(priority), Subject = subject.Trim(), Details = details.Trim(),
            CreatedBy = SessionService.CurrentUser?.Username ?? "System"
        };
        db.ShiftHandovers.Add(item); db.SaveChanges(); return item;
    }

    public static void Acknowledge(int id)
    {
        using var db = new AppDbContext();
        var item = db.ShiftHandovers.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Übergabe existiert nicht mehr.");
        if (item.Status == "Erledigt") return;
        item.Status = "Bestätigt"; item.AcknowledgedBy = SessionService.CurrentUser?.Username ?? "System";
        item.AcknowledgedAtUtc = DateTime.UtcNow; db.SaveChanges();
    }

    public static void Resolve(int id, string resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution)) throw new ArgumentException("Abschlussnotiz ist erforderlich.", nameof(resolution));
        using var db = new AppDbContext();
        var item = db.ShiftHandovers.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Übergabe existiert nicht mehr.");
        item.Status = "Erledigt"; item.Resolution = resolution.Trim(); item.ResolvedAtUtc = DateTime.UtcNow;
        if (!item.AcknowledgedAtUtc.HasValue) { item.AcknowledgedBy = SessionService.CurrentUser?.Username ?? "System"; item.AcknowledgedAtUtc = DateTime.UtcNow; }
        db.SaveChanges();
    }

    private static string NormalizePriority(string value) => value?.Trim() switch { "Kritisch" => "Kritisch", "Hoch" => "Hoch", "Niedrig" => "Niedrig", _ => "Normal" };
    private static void ValidateReferences(AppDbContext db,int? from,int? to,int? workstation,int? order)
    {
        if(from.HasValue&&!db.Shifts.Any(x=>x.Id==from)) throw new ArgumentException("Abgebende Schicht existiert nicht.");
        if(to.HasValue&&!db.Shifts.Any(x=>x.Id==to)) throw new ArgumentException("Übernehmende Schicht existiert nicht.");
        if(workstation.HasValue&&!db.Workstations.Any(x=>x.Id==workstation)) throw new ArgumentException("Arbeitsplatz existiert nicht.");
        if(order.HasValue&&!db.ProductionOrders.Any(x=>x.Id==order)) throw new ArgumentException("Produktionsauftrag existiert nicht.");
    }
}

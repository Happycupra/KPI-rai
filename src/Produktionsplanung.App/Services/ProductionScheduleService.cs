using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public static class ProductionScheduleService
{
    public const int MaxPlannedShiftCount = 63;

    public static IReadOnlyList<ProductionScheduleSlotDefinition> Build(
        DateTime startDate,
        int startShiftId,
        int shiftCount,
        IEnumerable<Shift> sourceShifts)
    {
        var shifts = sourceShifts
            .OrderBy(x => x.StartTime)
            .ThenBy(x => x.Name)
            .ToList();

        if (shifts.Count == 0)
            return Array.Empty<ProductionScheduleSlotDefinition>();

        var startIndex = shifts.FindIndex(x => x.Id == startShiftId);
        if (startIndex < 0)
            return Array.Empty<ProductionScheduleSlotDefinition>();

        var count = Math.Clamp(shiftCount, 1, MaxPlannedShiftCount);
        var result = new List<ProductionScheduleSlotDefinition>(count);
        for (var i = 0; i < count; i++)
        {
            var absoluteIndex = startIndex + i;
            var dayOffset = absoluteIndex / shifts.Count;
            var shift = shifts[absoluteIndex % shifts.Count];
            result.Add(new ProductionScheduleSlotDefinition(
                i + 1,
                startDate.Date.AddDays(dayOffset),
                shift.Id,
                shift.Name,
                shift.StartTime,
                shift.EndTime));
        }

        return result;
    }

    public static IReadOnlyList<ProductionScheduleSlotDefinition> BuildPreview(
        DateTime startDate,
        int startShiftId,
        int shiftCount)
    {
        using var db = new AppDbContext();
        var shifts = db.Shifts.AsNoTracking().ToList();
        return Build(startDate, startShiftId, shiftCount, shifts);
    }

    public static void SyncRunSlots(AppDbContext db, ProductionOrder order)
    {
        if (!order.ShiftId.HasValue)
            return;

        var shifts = db.Shifts.AsNoTracking().ToList();
        var definitions = Build(order.PlannedDate, order.ShiftId.Value, order.PlannedShiftCount, shifts);

        var existing = db.ProductionRunSlots.Where(x => x.ProductionOrderId == order.Id).ToList();
        if (existing.Count > 0)
            db.ProductionRunSlots.RemoveRange(existing);

        foreach (var slot in definitions)
        {
            db.ProductionRunSlots.Add(new ProductionRunSlot
            {
                ProductionOrderId = order.Id,
                SequenceNumber = slot.SequenceNumber,
                Date = slot.Date,
                ShiftId = slot.ShiftId
            });
        }
    }

    public static void EnsureMissingRunSlots(AppDbContext db)
    {
        var orders = db.ProductionOrders
            .Where(x => x.ShiftId.HasValue && !db.ProductionRunSlots.Any(s => s.ProductionOrderId == x.Id))
            .ToList();

        foreach (var order in orders)
            SyncRunSlots(db, order);

        if (orders.Count > 0)
            db.SaveChanges();
    }
}

public sealed record ProductionScheduleSlotDefinition(
    int SequenceNumber,
    DateTime Date,
    int ShiftId,
    string ShiftName,
    TimeSpan StartTime,
    TimeSpan EndTime)
{
    public string DateText => Date.ToString("ddd dd.MM.yyyy");
    public string TimeText => $"{StartTime:hh\\:mm}-{EndTime:hh\\:mm}";
    public string DisplayText => $"{SequenceNumber}. · {Date:ddd dd.MM.} · {ShiftName} · {TimeText}";
}

using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public static class ProductionScheduleService
{
    public const int MaxPlannedShiftCount = 63;
    private const int MaxSearchDays = 730;

    public static IReadOnlyList<ProductionScheduleSlotDefinition> Build(
        DateTime startDate,
        int startShiftId,
        int shiftCount,
        IEnumerable<Shift> sourceShifts,
        IEnumerable<WorkstationShiftRule>? sourceRules = null,
        IEnumerable<OperatingCalendarDay>? sourceOperatingDays = null)
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

        var rules = sourceRules?.ToList() ?? new List<WorkstationShiftRule>();
        var operatingDays = sourceOperatingDays?
            .GroupBy(x => x.Date.Date)
            .ToDictionary(x => x.Key, x => x.Last())
            ?? new Dictionary<DateTime, OperatingCalendarDay>();

        var normalizedStart = startDate.Date;
        if (!IsShiftAllowedOnDate(startShiftId, normalizedStart, rules, operatingDays))
            return Array.Empty<ProductionScheduleSlotDefinition>();

        var count = Math.Clamp(shiftCount, 1, MaxPlannedShiftCount);
        var result = new List<ProductionScheduleSlotDefinition>(count);
        var date = normalizedStart;
        var searchedDays = 0;

        while (result.Count < count && searchedDays < MaxSearchDays)
        {
            if (!IsClosedByOperatingCalendar(date, operatingDays))
            {
                var firstIndex = date == normalizedStart ? startIndex : 0;
                for (var i = firstIndex; i < shifts.Count && result.Count < count; i++)
                {
                    var shift = shifts[i];
                    if (!IsShiftAllowedOnDate(shift.Id, date, rules, operatingDays))
                        continue;

                    result.Add(new ProductionScheduleSlotDefinition(
                        result.Count + 1,
                        date,
                        shift.Id,
                        shift.Name,
                        shift.StartTime,
                        shift.EndTime));
                }
            }

            date = date.AddDays(1);
            searchedDays++;
        }

        return result;
    }

    public static IReadOnlyList<ProductionScheduleSlotDefinition> BuildPreview(
        DateTime startDate,
        int workstationId,
        int startShiftId,
        int shiftCount)
    {
        using var db = new AppDbContext();
        var shifts = db.Shifts.AsNoTracking().ToList();
        var rules = db.WorkstationShiftRules.AsNoTracking()
            .Where(x => x.WorkstationId == workstationId)
            .ToList();
        var operatingDays = db.OperatingCalendarDays.AsNoTracking()
            .Where(x => x.Date.Date >= startDate.Date && x.Date.Date <= startDate.Date.AddDays(MaxSearchDays))
            .ToList();
        return Build(startDate, startShiftId, shiftCount, shifts, rules, operatingDays);
    }

    public static IReadOnlyList<Shift> LoadAllowedShiftsForDate(AppDbContext db, int workstationId, DateTime date)
    {
        var shifts = db.Shifts.AsNoTracking()
            .AsEnumerable()
            .OrderBy(x => x.StartTime)
            .ThenBy(x => x.Name)
            .ToList();
        var rules = db.WorkstationShiftRules.AsNoTracking()
            .Where(x => x.WorkstationId == workstationId)
            .ToList();
        var operatingDay = db.OperatingCalendarDays.AsNoTracking()
            .FirstOrDefault(x => x.Date.Date == date.Date);
        var calendar = operatingDay is null
            ? new Dictionary<DateTime, OperatingCalendarDay>()
            : new Dictionary<DateTime, OperatingCalendarDay> { [date.Date] = operatingDay };

        return shifts.Where(x => IsShiftAllowedOnDate(x.Id, date.Date, rules, calendar)).ToList();
    }

    public static bool IsShiftAllowedOnDate(AppDbContext db, int workstationId, int shiftId, DateTime date)
    {
        var rules = db.WorkstationShiftRules.AsNoTracking()
            .Where(x => x.WorkstationId == workstationId)
            .ToList();
        var operatingDay = db.OperatingCalendarDays.AsNoTracking()
            .FirstOrDefault(x => x.Date.Date == date.Date);
        var calendar = operatingDay is null
            ? new Dictionary<DateTime, OperatingCalendarDay>()
            : new Dictionary<DateTime, OperatingCalendarDay> { [date.Date] = operatingDay };
        return IsShiftAllowedOnDate(shiftId, date.Date, rules, calendar);
    }

    public static bool IsRuleAllowedOnDay(WorkstationShiftRule rule, DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => rule.Monday,
        DayOfWeek.Tuesday => rule.Tuesday,
        DayOfWeek.Wednesday => rule.Wednesday,
        DayOfWeek.Thursday => rule.Thursday,
        DayOfWeek.Friday => rule.Friday,
        DayOfWeek.Saturday => rule.Saturday,
        DayOfWeek.Sunday => rule.Sunday,
        _ => false
    };

    public static void SyncRunSlots(AppDbContext db, ProductionOrder order)
    {
        if (!order.ShiftId.HasValue)
            return;

        var shifts = db.Shifts.AsNoTracking().ToList();
        var rules = db.WorkstationShiftRules.AsNoTracking()
            .Where(x => x.WorkstationId == order.WorkstationId)
            .ToList();
        var operatingDays = db.OperatingCalendarDays.AsNoTracking()
            .Where(x => x.Date.Date >= order.PlannedDate.Date && x.Date.Date <= order.PlannedDate.Date.AddDays(MaxSearchDays))
            .ToList();
        var definitions = Build(order.PlannedDate, order.ShiftId.Value, order.PlannedShiftCount, shifts, rules, operatingDays);

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

    private static bool IsShiftAllowedOnDate(
        int shiftId,
        DateTime date,
        IReadOnlyList<WorkstationShiftRule> rules,
        IReadOnlyDictionary<DateTime, OperatingCalendarDay> operatingDays)
    {
        if (IsClosedByOperatingCalendar(date, operatingDays))
            return false;

        // Compatibility fallback for databases/workstations not configured yet:
        // no rules means all global shifts remain available on all days.
        if (rules.Count == 0)
            return true;

        var rule = rules.FirstOrDefault(x => x.ShiftId == shiftId);
        return rule is not null && IsRuleAllowedOnDay(rule, date.DayOfWeek);
    }

    private static bool IsClosedByOperatingCalendar(
        DateTime date,
        IReadOnlyDictionary<DateTime, OperatingCalendarDay> operatingDays) =>
        operatingDays.TryGetValue(date.Date, out var day) && !day.IsWorkingDay;
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

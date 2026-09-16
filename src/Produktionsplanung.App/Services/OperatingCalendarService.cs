using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public static class OperatingCalendarService
{
    public static OperatingDayInfo GetDayInfo(AppDbContext db, DateTime date)
    {
        var day = date.Date;
        var exception = db.OperatingCalendarDays.AsNoTracking().FirstOrDefault(x => x.Date.Date == day);
        if (exception is not null)
        {
            return new OperatingDayInfo(
                day,
                exception.IsWorkingDay,
                Math.Clamp(exception.TargetHoursFactor, 0, 2),
                exception.Name,
                true);
        }

        var isWorkingDay = day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
        return new OperatingDayInfo(
            day,
            isWorkingDay,
            isWorkingDay ? 1 : 0,
            isWorkingDay ? "Regulärer Arbeitstag" : "Wochenende",
            false);
    }

    public static double GetTargetHours(AppDbContext db, Employee employee, DateTime start, DateTime end)
    {
        var dailyBase = employee.WeeklyTargetHours / 5d;
        var total = 0d;
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            var info = GetDayInfo(db, day);
            if (info.IsWorkingDay)
                total += dailyBase * info.TargetHoursFactor;
        }
        return total;
    }

    public static double CalculateNetHours(TimeSpan start, TimeSpan end, int breakMinutes)
    {
        var duration = end - start;
        if (duration <= TimeSpan.Zero)
            duration += TimeSpan.FromDays(1);
        return Math.Max(0, duration.TotalHours - breakMinutes / 60d);
    }
}

public sealed record OperatingDayInfo(
    DateTime Date,
    bool IsWorkingDay,
    double TargetHoursFactor,
    string Name,
    bool IsException);

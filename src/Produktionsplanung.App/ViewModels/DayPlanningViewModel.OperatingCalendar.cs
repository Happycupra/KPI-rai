using Produktionsplanung.App.Data;
using Produktionsplanung.App.Services;

namespace Produktionsplanung.App.ViewModels;

public partial class DayPlanningViewModel
{
    private const string CalendarAlertPrefix = "[Kalender] ";

    public void RefreshOperatingCalendarAlert()
    {
        for (var i = Alerts.Count - 1; i >= 0; i--)
        {
            if (Alerts[i].Message.StartsWith(CalendarAlertPrefix, StringComparison.Ordinal))
                Alerts.RemoveAt(i);
        }

        using var db = new AppDbContext();
        var info = OperatingCalendarService.GetDayInfo(db, SelectedDate);
        if (!info.IsException)
            return;

        Alerts.Add(new PlanningAlert
        {
            Severity = info.IsWorkingDay ? "Hinweis" : "Gelb",
            Message = info.IsWorkingDay
                ? $"{CalendarAlertPrefix}{info.Name}: Sonderarbeitstag, Sollstunden-Faktor {info.TargetHoursFactor:0.##}×."
                : $"{CalendarAlertPrefix}{info.Name}: Im Betriebskalender als arbeitsfrei markiert. Vorhandene Einsätze bitte bewusst prüfen."
        });
    }
}

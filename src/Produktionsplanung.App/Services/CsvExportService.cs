using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App.Services;

public static class CsvExportService
{
    private static readonly CultureInfo ExportCulture = CultureInfo.GetCultureInfo("de-CH");

    public static string ExportAll(string baseDirectory, AppSettings settings)
    {
        var exportDirectory = Path.Combine(baseDirectory, $"KPI-rai-Export-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(exportDirectory);

        var delimiter = string.IsNullOrEmpty(settings.CsvDelimiter) ? ';' : settings.CsvDelimiter[0];
        var encoding = new UTF8Encoding(settings.IncludeUtf8Bom);

        using var db = new AppDbContext();

        ExportEmployees(db, exportDirectory, delimiter, encoding);
        ExportWorkstations(db, exportDirectory, delimiter, encoding);
        ExportSkills(db, exportDirectory, delimiter, encoding);
        ExportShifts(db, exportDirectory, delimiter, encoding);
        ExportAbsences(db, exportDirectory, delimiter, encoding);
        ExportPlanning(db, exportDirectory, delimiter, encoding);
        ExportOrders(db, exportDirectory, delimiter, encoding);
        ExportActuals(db, exportDirectory, delimiter, encoding);
        ExportDowntimes(db, exportDirectory, delimiter, encoding);

        return exportDirectory;
    }

    private static void ExportEmployees(AppDbContext db, string directory, char delimiter, Encoding encoding)
    {
        var lines = new List<string>
        {
            Join(delimiter, "Personalnummer", "Nachname", "Vorname", "Funktion", "Abteilung", "Pensum %", "Sollstunden/Woche", "Aktiv")
        };

        foreach (var x in db.Employees.AsNoTracking().OrderBy(x => x.LastName).ThenBy(x => x.FirstName))
        {
            lines.Add(Join(delimiter,
                x.PersonnelNumber,
                x.LastName,
                x.FirstName,
                x.Role,
                x.Department,
                x.WorkloadPercent.ToString(ExportCulture),
                x.WeeklyTargetHours.ToString("0.##", ExportCulture),
                x.IsActive ? "Ja" : "Nein"));
        }

        File.WriteAllLines(Path.Combine(directory, "mitarbeiter.csv"), lines, encoding);
    }

    private static void ExportWorkstations(AppDbContext db, string directory, char delimiter, Encoding encoding)
    {
        var lines = new List<string>
        {
            Join(delimiter, "Arbeitsplatz", "Bereich", "Minimum", "Optimal", "Maximum", "Pflichtqualifikation", "Mindest-Level", "Aktiv")
        };

        foreach (var x in db.Workstations.AsNoTracking().Include(x => x.RequiredQualification).OrderBy(x => x.Name))
        {
            lines.Add(Join(delimiter,
                x.Name,
                x.Area,
                x.MinimumStaff.ToString(ExportCulture),
                x.OptimalStaff.ToString(ExportCulture),
                x.MaximumStaff.ToString(ExportCulture),
                x.RequiredQualification?.Name ?? string.Empty,
                x.RequiredQualificationLevel > 0 ? x.RequiredQualificationLevel.ToString(ExportCulture) : string.Empty,
                x.IsActive ? "Ja" : "Nein"));
        }

        File.WriteAllLines(Path.Combine(directory, "arbeitsplaetze.csv"), lines, encoding);
    }

    private static void ExportSkills(AppDbContext db, string directory, char delimiter, Encoding encoding)
    {
        var lines = new List<string>
        {
            Join(delimiter, "Personalnummer", "Mitarbeiter", "Qualifikation", "Level")
        };

        var rows = db.EmployeeQualifications.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Qualification)
            .OrderBy(x => x.Employee.LastName)
            .ThenBy(x => x.Employee.FirstName)
            .ThenBy(x => x.Qualification.Name)
            .ToList();

        foreach (var x in rows)
        {
            lines.Add(Join(delimiter,
                x.Employee.PersonnelNumber,
                $"{x.Employee.LastName}, {x.Employee.FirstName}",
                x.Qualification.Name,
                x.Level.ToString(ExportCulture)));
        }

        File.WriteAllLines(Path.Combine(directory, "skill_matrix.csv"), lines, encoding);
    }

    private static void ExportShifts(AppDbContext db, string directory, char delimiter, Encoding encoding)
    {
        var lines = new List<string>
        {
            Join(delimiter, "Schicht", "Start", "Ende", "Pause min")
        };

        foreach (var x in db.Shifts.AsNoTracking().AsEnumerable().OrderBy(x => x.StartTime).ThenBy(x => x.Name))
        {
            lines.Add(Join(delimiter,
                x.Name,
                x.StartTime.ToString(@"hh\:mm"),
                x.EndTime.ToString(@"hh\:mm"),
                x.BreakMinutes.ToString(ExportCulture)));
        }

        File.WriteAllLines(Path.Combine(directory, "schichten.csv"), lines, encoding);
    }

    private static void ExportAbsences(AppDbContext db, string directory, char delimiter, Encoding encoding)
    {
        var lines = new List<string>
        {
            Join(delimiter, "Personalnummer", "Mitarbeiter", "Typ", "Von", "Bis", "Kommentar")
        };

        foreach (var x in db.Absences.AsNoTracking().Include(x => x.Employee).OrderBy(x => x.StartDate))
        {
            lines.Add(Join(delimiter,
                x.Employee.PersonnelNumber,
                $"{x.Employee.LastName}, {x.Employee.FirstName}",
                x.Type,
                x.StartDate.ToString("dd.MM.yyyy"),
                x.EndDate.ToString("dd.MM.yyyy"),
                x.Comment ?? string.Empty));
        }

        File.WriteAllLines(Path.Combine(directory, "abwesenheiten.csv"), lines, encoding);
    }

    private static void ExportPlanning(AppDbContext db, string directory, char delimiter, Encoding encoding)
    {
        var lines = new List<string>
        {
            Join(delimiter, "Datum", "Personalnummer", "Mitarbeiter", "Arbeitsplatz", "Schicht", "Start", "Ende", "Pause min", "Kommentar")
        };

        var rows = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .AsEnumerable()
            .OrderBy(x => x.Date)
            .ThenBy(x => x.StartTime)
            .ToList();

        foreach (var x in rows)
        {
            lines.Add(Join(delimiter,
                x.Date.ToString("dd.MM.yyyy"),
                x.Employee.PersonnelNumber,
                $"{x.Employee.LastName}, {x.Employee.FirstName}",
                x.Workstation.Name,
                x.Shift?.Name ?? "Individuell",
                x.StartTime.ToString(@"hh\:mm"),
                x.EndTime.ToString(@"hh\:mm"),
                x.BreakMinutes.ToString(ExportCulture),
                x.Comment ?? string.Empty));
        }

        File.WriteAllLines(Path.Combine(directory, "planung.csv"), lines, encoding);
    }

    private static void ExportOrders(AppDbContext db, string directory, char delimiter, Encoding encoding)
    {
        var lines = new List<string>
        {
            Join(delimiter, "Auftrag", "Produkt", "Menge", "Einheit", "Priorität", "Plan-Datum", "Arbeitsplatz", "Schicht", "Personalbedarf", "Status", "Kommentar")
        };

        var rows = db.ProductionOrders.AsNoTracking()
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .OrderBy(x => x.PlannedDate)
            .ThenBy(x => x.OrderNumber)
            .ToList();

        foreach (var x in rows)
        {
            lines.Add(Join(delimiter,
                x.OrderNumber,
                x.Product,
                x.Quantity.ToString("0.##", ExportCulture),
                x.Unit,
                x.Priority,
                x.PlannedDate.ToString("dd.MM.yyyy"),
                x.Workstation.Name,
                x.Shift?.Name ?? "Individuell",
                x.RequiredStaff.ToString(ExportCulture),
                x.Status,
                x.Comment ?? string.Empty));
        }

        File.WriteAllLines(Path.Combine(directory, "produktionsauftraege.csv"), lines, encoding);
    }

    private static void ExportActuals(AppDbContext db, string directory, char delimiter, Encoding encoding)
    {
        var lines = new List<string>
        {
            Join(delimiter, "Datum", "Auftrag", "Produkt", "Arbeitsplatz", "Schicht", "Gesamtmenge", "Gutmenge", "Ausschuss", "Planzeit min", "Laufzeit min", "Stillstand min", "Sollrate/h", "Verfügbarkeit %", "Leistung %", "Qualität %", "OEE %", "Kommentar")
        };

        var rows = db.ProductionActuals.AsNoTracking()
            .Include(x => x.ProductionOrder).ThenInclude(x => x.Workstation)
            .Include(x => x.ProductionOrder).ThenInclude(x => x.Shift)
            .Include(x => x.Downtimes)
            .OrderBy(x => x.Date)
            .ToList();

        foreach (var x in rows)
        {
            var metrics = OeeAnalyticsService.Calculate(
                x.PlannedProductionMinutes,
                x.RunMinutes,
                x.TotalQuantity,
                x.GoodQuantity,
                x.IdealRatePerHour);

            lines.Add(Join(delimiter,
                x.Date.ToString("dd.MM.yyyy"),
                x.ProductionOrder.OrderNumber,
                x.ProductionOrder.Product,
                x.ProductionOrder.Workstation.Name,
                x.ProductionOrder.Shift?.Name ?? "Individuell",
                x.TotalQuantity.ToString("0.##", ExportCulture),
                x.GoodQuantity.ToString("0.##", ExportCulture),
                x.ScrapQuantity.ToString("0.##", ExportCulture),
                x.PlannedProductionMinutes.ToString("0.##", ExportCulture),
                x.RunMinutes.ToString("0.##", ExportCulture),
                x.Downtimes.Sum(d => d.Minutes).ToString("0.##", ExportCulture),
                x.IdealRatePerHour.ToString("0.##", ExportCulture),
                metrics.AvailabilityPercent.ToString("0.0", ExportCulture),
                metrics.PerformancePercent.ToString("0.0", ExportCulture),
                metrics.QualityPercent.ToString("0.0", ExportCulture),
                metrics.OeePercent.ToString("0.0", ExportCulture),
                x.Comment ?? string.Empty));
        }

        File.WriteAllLines(Path.Combine(directory, "ist_produktion_oee.csv"), lines, encoding);
    }

    private static void ExportDowntimes(AppDbContext db, string directory, char delimiter, Encoding encoding)
    {
        var lines = new List<string>
        {
            Join(delimiter, "Datum", "Auftrag", "Arbeitsplatz", "Grund", "Minuten", "Kommentar")
        };

        var rows = db.DowntimeEntries.AsNoTracking()
            .Include(x => x.ProductionActual)
                .ThenInclude(x => x.ProductionOrder)
                    .ThenInclude(x => x.Workstation)
            .OrderBy(x => x.ProductionActual.Date)
            .ToList();

        foreach (var x in rows)
        {
            lines.Add(Join(delimiter,
                x.ProductionActual.Date.ToString("dd.MM.yyyy"),
                x.ProductionActual.ProductionOrder.OrderNumber,
                x.ProductionActual.ProductionOrder.Workstation.Name,
                x.Reason,
                x.Minutes.ToString("0.##", ExportCulture),
                x.Comment ?? string.Empty));
        }

        File.WriteAllLines(Path.Combine(directory, "stillstaende.csv"), lines, encoding);
    }

    private static string Join(char delimiter, params string[] values) =>
        string.Join(delimiter, values.Select(value => Escape(value, delimiter)));

    private static string Escape(string value, char delimiter)
    {
        value ??= string.Empty;
        if (value.Contains('"') || value.Contains(delimiter) || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
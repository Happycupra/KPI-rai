using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public sealed record MasterDataImportResult(string Area, int NewCount, int UpdatedCount, int SkippedCount, IReadOnlyList<string> Errors)
{
    public string Summary
    {
        get
        {
            var parts = new List<string> { $"{NewCount} neu", $"{UpdatedCount} aktualisiert" };
            if (SkippedCount > 0) parts.Add($"{SkippedCount} übersprungen");
            if (Errors.Count > 0) parts.Add($"{Errors.Count} Fehler");
            return $"{Area}: {string.Join(", ", parts)}.";
        }
    }
}

public static class MasterDataExcelImportService
{
    private static readonly CultureInfo DeCh = CultureInfo.GetCultureInfo("de-CH");
    private static readonly string[] Priorities = { "Niedrig", "Normal", "Hoch", "Dringend" };

    public static void CreateTemplate(string path)
    {
        using var wb = new XLWorkbook();

        var e = wb.Worksheets.Add("Mitarbeitende");
        Header(e, "Personalnummer", "Vorname", "Nachname", "Funktion", "Abteilung", "PensumProzent", "SollstundenWoche", "Aktiv", "Qualifikationen");
        e.Cell(2, 1).Value = "P001"; e.Cell(2, 2).Value = "Max"; e.Cell(2, 3).Value = "Muster";
        e.Cell(2, 4).Value = "Operator"; e.Cell(2, 5).Value = "Produktion"; e.Cell(2, 6).Value = 100;
        e.Cell(2, 7).Value = 40; e.Cell(2, 8).Value = "Ja"; e.Cell(2, 9).Value = "Abfüllung:2;Reinigung:5";
        e.Column(1).Style.NumberFormat.Format = "@";

        var w = wb.Worksheets.Add("Arbeitsplätze");
        Header(w, "Name", "Bereich", "Minimum", "Optimal", "Maximum", "Aktiv", "Pflichtqualifikation", "SkillLevel");
        w.Cell(2, 1).Value = "Import Linie"; w.Cell(2, 2).Value = "Produktion"; w.Cell(2, 3).Value = 1;
        w.Cell(2, 4).Value = 2; w.Cell(2, 5).Value = 3; w.Cell(2, 6).Value = "Ja";
        w.Cell(2, 7).Value = "Abfüllung"; w.Cell(2, 8).Value = 2;

        var s = wb.Worksheets.Add("Schichten");
        Header(s, "Name", "Start", "Ende", "PauseMinuten");
        s.Cell(2, 1).Value = "Import Früh"; s.Cell(2, 2).Value = "06:00"; s.Cell(2, 3).Value = "14:00"; s.Cell(2, 4).Value = 30;

        var rules = wb.Worksheets.Add("ArbeitsplatzSchichten");
        Header(rules, "Arbeitsplatz", "Schicht", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag");
        rules.Cell(2, 1).Value = "Import Linie"; rules.Cell(2, 2).Value = "Import Früh";
        for (var col = 3; col <= 7; col++) rules.Cell(2, col).Value = "Ja";
        rules.Cell(2, 8).Value = "Nein"; rules.Cell(2, 9).Value = "Nein";

        var o = wb.Worksheets.Add("Produktionsaufträge");
        Header(o, "Auftragsnummer", "Artikelnummer", "Chargennummer", "Menge", "Datum", "Arbeitsplatz", "Schicht", "AnzahlSchichten", "Personalbedarf", "Priorität", "Kommentar");
        o.Cell(2, 1).Value = "IMPORT-AUF-001"; o.Cell(2, 2).Value = "0001"; o.Cell(2, 3).Value = "IMPORT-CH-001";
        o.Cell(2, 4).Value = 1000; o.Cell(2, 5).Value = DateTime.Today; o.Cell(2, 6).Value = "Import Linie";
        o.Cell(2, 7).Value = "Import Früh"; o.Cell(2, 8).Value = 1; o.Cell(2, 9).Value = 2; o.Cell(2, 10).Value = "Normal";

        foreach (var ws in wb.Worksheets)
        {
            ws.SheetView.FreezeRows(1);
            ws.RangeUsed()?.SetAutoFilter();
            ws.Columns().AdjustToContents();
        }

        wb.SaveAs(path);
    }

    public static IReadOnlyList<MasterDataImportResult> Import(string path, bool dryRun)
    {
        BatchService.RequirePlanner();
        using var wb = new XLWorkbook(path);
        var results = new List<MasterDataImportResult>();
        using var db = new AppDbContext();
        using var tx = db.Database.BeginTransaction();

        if (wb.TryGetWorksheet("Mitarbeitende", out var employees)) results.Add(ImportEmployees(db, employees));
        if (wb.TryGetWorksheet("Arbeitsplätze", out var workstations)) results.Add(ImportWorkstations(db, workstations));
        if (wb.TryGetWorksheet("Schichten", out var shifts)) results.Add(ImportShifts(db, shifts));
        if (wb.TryGetWorksheet("ArbeitsplatzSchichten", out var rules)) results.Add(ImportWorkstationShiftRules(db, rules));
        if (wb.TryGetWorksheet("Produktionsaufträge", out var orders)) results.Add(ImportOrders(db, orders));

        if (results.Count == 0)
            throw new InvalidOperationException("Keine unterstützten Tabellenblätter gefunden. Bitte die SolutionCompakt-Stammdatenvorlage verwenden.");

        if (dryRun) tx.Rollback(); else tx.Commit();
        return results;
    }

    private static MasterDataImportResult ImportEmployees(AppDbContext db, IXLWorksheet ws)
    {
        var map = Map(ws);
        RequireHeaders(map, "Personalnummer", "Vorname", "Nachname");
        var added = 0; var updated = 0; var skipped = 0; var errors = new List<string>();

        for (var row = 2; row <= LastRow(ws); row++)
        {
            var number = Cell(ws, row, map, "Personalnummer");
            if (number.Length == 0) { skipped++; continue; }

            var savepoint = $"employee_{row}";
            CreateSavepoint(db, savepoint);
            try
            {
                var first = Required(Cell(ws, row, map, "Vorname"), "Vorname");
                var last = Required(Cell(ws, row, map, "Nachname"), "Nachname");
                var workload = Int(Cell(ws, row, map, "PensumProzent"), 100);
                var targetHours = Number(Cell(ws, row, map, "SollstundenWoche"), 40);
                if (workload is < 1 or > 100) throw new InvalidOperationException("PensumProzent muss zwischen 1 und 100 liegen.");
                if (!double.IsFinite(targetHours) || targetHours is < 0 or > 80) throw new InvalidOperationException("SollstundenWoche muss zwischen 0 und 80 liegen.");

                var skillSpecs = ParseSkillSpecs(Cell(ws, row, map, "Qualifikationen"));

                var employee = db.Employees.FirstOrDefault(x => x.PersonnelNumber == number);
                var isNew = employee is null;
                employee ??= new Employee { PersonnelNumber = number };
                if (isNew) db.Employees.Add(employee);

                employee.FirstName = first;
                employee.LastName = last;
                employee.Role = Cell(ws, row, map, "Funktion");
                employee.Department = Cell(ws, row, map, "Abteilung");
                employee.WorkloadPercent = workload;
                employee.WeeklyTargetHours = targetHours;
                employee.IsActive = Bool(Cell(ws, row, map, "Aktiv"), defaultValue: true);
                db.SaveChanges();

                foreach (var (qualificationName, level) in skillSpecs)
                {
                    var qualification = db.Qualifications.FirstOrDefault(x => x.Name.ToLower() == qualificationName.ToLower());
                    if (qualification is null)
                    {
                        qualification = new Qualification { Name = qualificationName };
                        db.Qualifications.Add(qualification);
                        db.SaveChanges();
                    }

                    var link = db.EmployeeQualifications.FirstOrDefault(x =>
                        x.EmployeeId == employee.Id && x.QualificationId == qualification.Id);
                    if (level == QualificationLevelCatalog.None)
                    {
                        if (link is not null) db.EmployeeQualifications.Remove(link);
                    }
                    else if (link is null)
                    {
                        db.EmployeeQualifications.Add(new EmployeeQualification
                        {
                            EmployeeId = employee.Id,
                            QualificationId = qualification.Id,
                            Level = level
                        });
                    }
                    else link.Level = level;
                }
                db.SaveChanges();
                if (isNew) added++; else updated++;
                ReleaseSavepoint(db, savepoint);
            }
            catch (Exception ex)
            {
                RollbackSavepoint(db, savepoint);
                errors.Add($"Zeile {row} ({number}): {ex.Message}");
            }
        }

        return new("Mitarbeitende", added, updated, skipped, errors);
    }

    private static MasterDataImportResult ImportWorkstations(AppDbContext db, IXLWorksheet ws)
    {
        var map = Map(ws);
        RequireHeaders(map, "Name");
        var added = 0; var updated = 0; var skipped = 0; var errors = new List<string>();

        for (var row = 2; row <= LastRow(ws); row++)
        {
            var name = Cell(ws, row, map, "Name");
            if (name.Length == 0) { skipped++; continue; }

            var savepoint = $"workstation_{row}";
            CreateSavepoint(db, savepoint);
            try
            {
                var minimum = Int(Cell(ws, row, map, "Minimum"), 1);
                var optimal = Int(Cell(ws, row, map, "Optimal"), Math.Max(1, minimum));
                var maximum = Int(Cell(ws, row, map, "Maximum"), Math.Max(optimal, 1));
                if (minimum < 0 || optimal < minimum || maximum < optimal)
                    throw new InvalidOperationException("Besetzung muss gelten: Minimum ≤ Optimal ≤ Maximum.");

                var qualificationName = Cell(ws, row, map, "Pflichtqualifikation");
                var requiredLevel = qualificationName.Length == 0 ? 0 : Int(Cell(ws, row, map, "SkillLevel"), QualificationLevelCatalog.Qualified);
                if (qualificationName.Length > 0 && !QualificationLevelCatalog.IsSupportedRequirementLevel(requiredLevel))
                    throw new InvalidOperationException("SkillLevel muss zwischen 1 und 5 liegen.");

                var workstation = db.Workstations.FirstOrDefault(x => x.Name == name);
                var isNew = workstation is null;
                workstation ??= new Workstation { Name = name };
                if (isNew) db.Workstations.Add(workstation);

                workstation.Area = Cell(ws, row, map, "Bereich");
                workstation.MinimumStaff = minimum;
                workstation.OptimalStaff = optimal;
                workstation.MaximumStaff = maximum;
                workstation.IsActive = Bool(Cell(ws, row, map, "Aktiv"), defaultValue: true);

                if (qualificationName.Length == 0)
                {
                    workstation.RequiredQualificationId = null;
                    workstation.RequiredQualificationLevel = 0;
                }
                else
                {
                    var qualification = db.Qualifications.FirstOrDefault(x => x.Name.ToLower() == qualificationName.ToLower());
                    if (qualification is null)
                    {
                        qualification = new Qualification { Name = qualificationName };
                        db.Qualifications.Add(qualification);
                        db.SaveChanges();
                    }
                    workstation.RequiredQualificationId = qualification.Id;
                    workstation.RequiredQualificationLevel = requiredLevel;
                }

                db.SaveChanges();
                if (isNew) added++; else updated++;
                ReleaseSavepoint(db, savepoint);
            }
            catch (Exception ex)
            {
                RollbackSavepoint(db, savepoint);
                errors.Add($"Zeile {row} ({name}): {ex.Message}");
            }
        }

        return new("Arbeitsplätze", added, updated, skipped, errors);
    }

    private static MasterDataImportResult ImportShifts(AppDbContext db, IXLWorksheet ws)
    {
        var map = Map(ws);
        RequireHeaders(map, "Name", "Start", "Ende");
        var added = 0; var updated = 0; var skipped = 0; var errors = new List<string>();

        for (var row = 2; row <= LastRow(ws); row++)
        {
            var name = Cell(ws, row, map, "Name");
            if (name.Length == 0) { skipped++; continue; }

            var savepoint = $"shift_{row}";
            CreateSavepoint(db, savepoint);
            try
            {
                var start = Time(Cell(ws, row, map, "Start"), "Start");
                var end = Time(Cell(ws, row, map, "Ende"), "Ende");
                var breakMinutes = Int(Cell(ws, row, map, "PauseMinuten"), 0);
                if (start == end) throw new InvalidOperationException("Start und Ende dürfen nicht identisch sein.");
                if (breakMinutes is < 0 or > 1440) throw new InvalidOperationException("PauseMinuten ist ungültig.");

                var shift = db.Shifts.FirstOrDefault(x => x.Name == name);
                var isNew = shift is null;
                shift ??= new Shift { Name = name };
                if (isNew) db.Shifts.Add(shift);
                shift.StartTime = start;
                shift.EndTime = end;
                shift.BreakMinutes = breakMinutes;
                db.SaveChanges();
                if (isNew) added++; else updated++;
                ReleaseSavepoint(db, savepoint);
            }
            catch (Exception ex)
            {
                RollbackSavepoint(db, savepoint);
                errors.Add($"Zeile {row} ({name}): {ex.Message}");
            }
        }

        return new("Schichten", added, updated, skipped, errors);
    }

    private static MasterDataImportResult ImportWorkstationShiftRules(AppDbContext db, IXLWorksheet ws)
    {
        var map = Map(ws);
        RequireHeaders(map, "Arbeitsplatz", "Schicht", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag");
        var added = 0; var updated = 0; var skipped = 0; var errors = new List<string>();

        for (var row = 2; row <= LastRow(ws); row++)
        {
            var workstationName = Cell(ws, row, map, "Arbeitsplatz");
            var shiftName = Cell(ws, row, map, "Schicht");
            if (workstationName.Length == 0 && shiftName.Length == 0) { skipped++; continue; }

            var savepoint = $"rule_{row}";
            CreateSavepoint(db, savepoint);
            try
            {
                workstationName = Required(workstationName, "Arbeitsplatz");
                shiftName = Required(shiftName, "Schicht");
                var workstation = db.Workstations.FirstOrDefault(x => x.Name == workstationName)
                    ?? throw new InvalidOperationException("Arbeitsplatz nicht gefunden.");
                var shift = db.Shifts.FirstOrDefault(x => x.Name == shiftName)
                    ?? throw new InvalidOperationException("Schicht nicht gefunden.");

                var rule = db.WorkstationShiftRules.FirstOrDefault(x => x.WorkstationId == workstation.Id && x.ShiftId == shift.Id);
                var isNew = rule is null;
                rule ??= new WorkstationShiftRule { WorkstationId = workstation.Id, ShiftId = shift.Id };
                if (isNew) db.WorkstationShiftRules.Add(rule);

                rule.Monday = Bool(Cell(ws, row, map, "Montag"), false);
                rule.Tuesday = Bool(Cell(ws, row, map, "Dienstag"), false);
                rule.Wednesday = Bool(Cell(ws, row, map, "Mittwoch"), false);
                rule.Thursday = Bool(Cell(ws, row, map, "Donnerstag"), false);
                rule.Friday = Bool(Cell(ws, row, map, "Freitag"), false);
                rule.Saturday = Bool(Cell(ws, row, map, "Samstag"), false);
                rule.Sunday = Bool(Cell(ws, row, map, "Sonntag"), false);
                if (!(rule.Monday || rule.Tuesday || rule.Wednesday || rule.Thursday || rule.Friday || rule.Saturday || rule.Sunday))
                    throw new InvalidOperationException("Mindestens ein Wochentag muss freigegeben sein.");

                db.SaveChanges();
                if (isNew) added++; else updated++;
                ReleaseSavepoint(db, savepoint);
            }
            catch (Exception ex)
            {
                RollbackSavepoint(db, savepoint);
                errors.Add($"Zeile {row} ({workstationName} / {shiftName}): {ex.Message}");
            }
        }

        return new("Arbeitsplatz-Schichten", added, updated, skipped, errors);
    }

    private static MasterDataImportResult ImportOrders(AppDbContext db, IXLWorksheet ws)
    {
        var map = Map(ws);
        RequireHeaders(map, "Auftragsnummer", "Artikelnummer", "Chargennummer", "Menge", "Datum", "Arbeitsplatz", "Schicht");
        var added = 0; var updated = 0; var skipped = 0; var errors = new List<string>();

        for (var row = 2; row <= LastRow(ws); row++)
        {
            var orderNumber = Cell(ws, row, map, "Auftragsnummer");
            if (orderNumber.Length == 0) { skipped++; continue; }

            var savepoint = $"order_{row}";
            CreateSavepoint(db, savepoint);
            try
            {
                if (db.ProductionOrders.Any(x => x.OrderNumber == orderNumber))
                {
                    skipped++;
                    ReleaseSavepoint(db, savepoint);
                    continue;
                }

                var articleNumber = Required(Cell(ws, row, map, "Artikelnummer"), "Artikelnummer");
                var batchNumber = Required(Cell(ws, row, map, "Chargennummer"), "Chargennummer");
                var article = db.ArticleMasters.FirstOrDefault(x => x.ArticleNumber == articleNumber && x.IsActive)
                    ?? throw new InvalidOperationException("Aktiver Artikel nicht gefunden.");
                if (db.ProductionOrders.Any(x => x.ArticleMasterId == article.Id && x.BatchNumber == batchNumber))
                    throw new InvalidOperationException("Diese Chargennummer existiert für den Artikel bereits.");

                var workstationName = Required(Cell(ws, row, map, "Arbeitsplatz"), "Arbeitsplatz");
                var shiftName = Required(Cell(ws, row, map, "Schicht"), "Schicht");
                var workstation = db.Workstations.FirstOrDefault(x => x.Name == workstationName && x.IsActive)
                    ?? throw new InvalidOperationException("Aktiver Arbeitsplatz nicht gefunden.");
                var shift = db.Shifts.FirstOrDefault(x => x.Name == shiftName)
                    ?? throw new InvalidOperationException("Schicht nicht gefunden.");

                var date = Date(Cell(ws, row, map, "Datum"), "Datum");
                var quantity = Number(Cell(ws, row, map, "Menge"), article.DefaultQuantity ?? 1);
                if (!double.IsFinite(quantity) || quantity <= 0) throw new InvalidOperationException("Menge muss grösser als 0 sein.");
                var shiftCount = Int(Cell(ws, row, map, "AnzahlSchichten"), 1);
                if (shiftCount is < 1 or > ProductionScheduleService.MaxPlannedShiftCount)
                    throw new InvalidOperationException($"AnzahlSchichten muss zwischen 1 und {ProductionScheduleService.MaxPlannedShiftCount} liegen.");
                var requiredStaff = Int(Cell(ws, row, map, "Personalbedarf"), Math.Max(1, workstation.OptimalStaff));
                if (requiredStaff < 1) throw new InvalidOperationException("Personalbedarf muss mindestens 1 sein.");

                var priority = Cell(ws, row, map, "Priorität");
                priority = string.IsNullOrWhiteSpace(priority) ? "Normal" : priority;
                if (!Priorities.Contains(priority, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Priorität muss Niedrig, Normal, Hoch oder Dringend sein.");
                priority = Priorities.First(x => x.Equals(priority, StringComparison.OrdinalIgnoreCase));

                var shifts = db.Shifts.AsNoTracking().ToList();
                var rules = db.WorkstationShiftRules.AsNoTracking().Where(x => x.WorkstationId == workstation.Id).ToList();
                var operatingDays = db.OperatingCalendarDays.AsNoTracking()
                    .Where(x => x.Date.Date >= date && x.Date.Date <= date.AddDays(730)).ToList();
                var schedule = ProductionScheduleService.Build(date, shift.Id, shiftCount, shifts, rules, operatingDays);
                if (schedule.Count != shiftCount)
                    throw new InvalidOperationException("Terminierung ist für Arbeitsplatz/Schicht nicht vollständig freigegeben.");

                var order = new ProductionOrder
                {
                    OrderNumber = orderNumber,
                    ArticleMasterId = article.Id,
                    ArticleNumber = article.ArticleNumber,
                    Product = article.Name,
                    Unit = article.Unit,
                    ManufacturingRoutingId = article.DefaultRoutingId,
                    IdealRatePerHourSnapshot = article.DefaultIdealRatePerHour,
                    BatchNumber = batchNumber,
                    Quantity = quantity,
                    PlannedDate = date,
                    WorkstationId = workstation.Id,
                    ShiftId = shift.Id,
                    PlannedStart = shift.StartTime,
                    PlannedEnd = shift.EndTime,
                    PlannedShiftCount = shiftCount,
                    RequiredStaff = requiredStaff,
                    Priority = priority,
                    Status = "Geplant",
                    Comment = NullIfEmpty(Cell(ws, row, map, "Kommentar"))
                };
                db.ProductionOrders.Add(order);
                db.SaveChanges();
                ProductionScheduleService.SyncRunSlots(db, order);
                db.SaveChanges();
                if (db.ProductionRunSlots.Count(x => x.ProductionOrderId == order.Id) != shiftCount)
                    throw new InvalidOperationException("Produktionsschichten konnten nicht vollständig erzeugt werden.");
                added++;
                ReleaseSavepoint(db, savepoint);
            }
            catch (Exception ex)
            {
                RollbackSavepoint(db, savepoint);
                errors.Add($"Zeile {row} ({orderNumber}): {ex.Message}");
            }
        }

        return new("Produktionsaufträge", added, updated, skipped, errors);
    }

    private static IReadOnlyList<(string Name, int Level)> ParseSkillSpecs(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<(string, int)>();
        var result = new List<(string, int)>();
        foreach (var raw in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
            var name = Required(parts[0], "Qualifikationsname");
            var level = parts.Length == 1 ? QualificationLevelCatalog.Qualified : Int(parts[1], int.MinValue);
            if (!QualificationLevelCatalog.IsSupportedEmployeeLevel(level))
                throw new InvalidOperationException($"Skill-Level für „{name}“ muss zwischen 0 und 5 liegen.");
            result.Add((name, level));
        }
        return result;
    }

    private static void CreateSavepoint(AppDbContext db, string name) => db.Database.CurrentTransaction?.CreateSavepoint(name);

    private static void ReleaseSavepoint(AppDbContext db, string name) => db.Database.CurrentTransaction?.ReleaseSavepoint(name);

    private static void RollbackSavepoint(AppDbContext db, string name)
    {
        db.Database.CurrentTransaction?.RollbackToSavepoint(name);
        db.Database.CurrentTransaction?.ReleaseSavepoint(name);
        db.ChangeTracker.Clear();
    }

    private static void Header(IXLWorksheet ws, params string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }
    }

    private static Dictionary<string, int> Map(IXLWorksheet ws) =>
        ws.Row(1).CellsUsed().ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);

    private static void RequireHeaders(IReadOnlyDictionary<string, int> map, params string[] headers)
    {
        var missing = headers.Where(x => !map.ContainsKey(x)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Pflichtspalte(n) fehlen: {string.Join(", ", missing)}.");
    }

    private static int LastRow(IXLWorksheet ws) => ws.LastRowUsed()?.RowNumber() ?? 1;

    private static string Cell(IXLWorksheet ws, int row, IReadOnlyDictionary<string, int> map, string name) =>
        map.TryGetValue(name, out var column) ? ws.Cell(row, column).GetFormattedString().Trim() : string.Empty;

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{name} fehlt.") : value.Trim();

    private static int Int(string value, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return int.TryParse(value, NumberStyles.Integer, DeCh, out var number) ||
               int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : throw new InvalidOperationException($"„{value}“ ist keine gültige Ganzzahl.");
    }

    private static double Number(string value, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, DeCh, out var number) ||
               double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out number)
            ? number
            : throw new InvalidOperationException($"„{value}“ ist keine gültige Zahl.");
    }

    private static TimeSpan Time(string value, string field)
    {
        if (TimeSpan.TryParse(value, DeCh, out var time) || TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out time))
            return time;
        throw new InvalidOperationException($"{field} „{value}“ ist keine gültige Uhrzeit.");
    }

    private static DateTime Date(string value, string field)
    {
        if (DateTime.TryParse(value, DeCh, DateTimeStyles.AllowWhiteSpaces, out var date) ||
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
            return date.Date;
        throw new InvalidOperationException($"{field} „{value}“ ist kein gültiges Datum.");
    }

    private static bool Bool(string value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        if (value.Equals("Ja", StringComparison.OrdinalIgnoreCase) || value.Equals("True", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("X", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("Nein", StringComparison.OrdinalIgnoreCase) || value.Equals("False", StringComparison.OrdinalIgnoreCase) || value == "0" || value.Equals("-", StringComparison.OrdinalIgnoreCase)) return false;
        throw new InvalidOperationException($"„{value}“ ist kein gültiger Ja/Nein-Wert.");
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

using System.Globalization;
using ClosedXML.Excel;
using Produktionsplanung.App.Data;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public static class EmployeeExcelImportService
{
    private static readonly CultureInfo DeCh = CultureInfo.GetCultureInfo("de-CH");

    public static void CreateTemplate(string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Mitarbeitende");

        Header(ws,
            "Personalnummer",
            "Vorname",
            "Nachname",
            "Funktion",
            "Abteilung",
            "PensumProzent",
            "SollstundenWoche",
            "Aktiv",
            "Qualifikationen");

        ws.Cell(2, 1).Value = "P001";
        ws.Cell(2, 2).Value = "Max";
        ws.Cell(2, 3).Value = "Muster";
        ws.Cell(2, 4).Value = "Operator";
        ws.Cell(2, 5).Value = "Produktion";
        ws.Cell(2, 6).Value = 100;
        ws.Cell(2, 7).Value = 40;
        ws.Cell(2, 8).Value = "Ja";
        ws.Cell(2, 9).Value = "Abfüllung:2;Reinigung:5";
        ws.Column(1).Style.NumberFormat.Format = "@";

        var info = wb.Worksheets.Add("Hinweise");
        info.Cell(1, 1).Value = "SolutionCompakt Mitarbeiter-Import";
        info.Cell(2, 1).Value = "Beispielzeile vor dem Import durch eigene Daten ersetzen oder löschen.";
        info.Cell(3, 1).Value = "Pflichtfelder: Personalnummer, Vorname und Nachname.";
        info.Cell(4, 1).Value = "Bestehende Mitarbeitende werden anhand der Personalnummer aktualisiert.";
        info.Cell(5, 1).Value = "Qualifikationen: Name:Level;Name:Level, z. B. Abfüllung:2;Reinigung:5.";
        info.Cell(6, 1).Value = "Skill-Level: 0 = keine, 1 = in Ausbildung, 2 = qualifiziert, 3 = Experte/Trainer, 4 = Level 4, 5 = Admin.";
        info.Cell(7, 1).Value = "Nicht aufgeführte Qualifikationen bleiben bei bestehenden Mitarbeitenden unverändert.";
        info.Cell(8, 1).Value = "Vor dem echten Import zeigt SolutionCompakt immer eine Vorschau/Testlauf an.";

        foreach (var sheet in wb.Worksheets)
        {
            sheet.SheetView.FreezeRows(1);
            sheet.RangeUsed()?.SetAutoFilter();
            sheet.Columns().AdjustToContents();
        }

        wb.SaveAs(path);
    }

    public static MasterDataImportResult Import(string path, bool dryRun)
    {
        BatchService.RequirePlanner();
        using var wb = new XLWorkbook(path);
        if (!wb.TryGetWorksheet("Mitarbeitende", out var ws))
            throw new InvalidOperationException("Das Tabellenblatt „Mitarbeitende“ fehlt. Bitte die SolutionCompakt Mitarbeiter-Vorlage verwenden.");

        using var db = new AppDbContext();
        using var tx = db.Database.BeginTransaction();
        var result = ImportEmployees(db, ws);

        if (dryRun)
            tx.Rollback();
        else
            tx.Commit();

        return result;
    }

    private static MasterDataImportResult ImportEmployees(AppDbContext db, IXLWorksheet ws)
    {
        var map = Map(ws);
        RequireHeaders(map, "Personalnummer", "Vorname", "Nachname");

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

        for (var row = 2; row <= LastRow(ws); row++)
        {
            var number = Cell(ws, row, map, "Personalnummer");
            if (number.Length == 0)
            {
                skipped++;
                continue;
            }

            var savepoint = $"employee_{row}";
            db.Database.CurrentTransaction?.CreateSavepoint(savepoint);
            try
            {
                var first = Required(Cell(ws, row, map, "Vorname"), "Vorname");
                var last = Required(Cell(ws, row, map, "Nachname"), "Nachname");
                var workload = Int(Cell(ws, row, map, "PensumProzent"), 100);
                var targetHours = Number(Cell(ws, row, map, "SollstundenWoche"), 40);

                if (workload is < 1 or > 100)
                    throw new InvalidOperationException("PensumProzent muss zwischen 1 und 100 liegen.");
                if (!double.IsFinite(targetHours) || targetHours is < 0 or > 80)
                    throw new InvalidOperationException("SollstundenWoche muss zwischen 0 und 80 liegen.");

                var skillSpecs = ParseSkillSpecs(Cell(ws, row, map, "Qualifikationen"));

                var employee = db.Employees.FirstOrDefault(x => x.PersonnelNumber == number);
                var isNew = employee is null;
                employee ??= new Employee { PersonnelNumber = number };
                if (isNew)
                    db.Employees.Add(employee);

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
                        if (link is not null)
                            db.EmployeeQualifications.Remove(link);
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
                    else
                    {
                        link.Level = level;
                    }
                }

                db.SaveChanges();
                if (isNew)
                    added++;
                else
                    updated++;

                db.Database.CurrentTransaction?.ReleaseSavepoint(savepoint);
            }
            catch (Exception ex)
            {
                db.Database.CurrentTransaction?.RollbackToSavepoint(savepoint);
                db.Database.CurrentTransaction?.ReleaseSavepoint(savepoint);
                db.ChangeTracker.Clear();
                errors.Add($"Zeile {row} ({number}): {ex.Message}");
            }
        }

        return new MasterDataImportResult("Mitarbeitende", added, updated, skipped, errors);
    }

    private static IReadOnlyList<(string Name, int Level)> ParseSkillSpecs(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<(string, int)>();

        var result = new List<(string, int)>();
        foreach (var raw in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split(':', 2, StringSplitOptions.TrimEntries);
            var name = Required(parts[0], "Qualifikationsname");
            var level = parts.Length == 1
                ? QualificationLevelCatalog.Qualified
                : Int(parts[1], int.MinValue);

            if (!QualificationLevelCatalog.IsSupportedEmployeeLevel(level))
                throw new InvalidOperationException($"Skill-Level für „{name}“ muss zwischen 0 und 5 liegen.");

            result.Add((name, level));
        }

        return result;
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
        ws.Row(1).CellsUsed().ToDictionary(
            cell => cell.GetString().Trim(),
            cell => cell.Address.ColumnNumber,
            StringComparer.OrdinalIgnoreCase);

    private static void RequireHeaders(IReadOnlyDictionary<string, int> map, params string[] headers)
    {
        var missing = headers.Where(x => !map.ContainsKey(x)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Pflichtspalte(n) fehlen: {string.Join(", ", missing)}.");
    }

    private static int LastRow(IXLWorksheet ws) => ws.LastRowUsed()?.RowNumber() ?? 1;

    private static string Cell(IXLWorksheet ws, int row, IReadOnlyDictionary<string, int> map, string name) =>
        map.TryGetValue(name, out var column)
            ? ws.Cell(row, column).GetFormattedString().Trim()
            : string.Empty;

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{name} fehlt.")
            : value.Trim();

    private static int Int(string value, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return int.TryParse(value, NumberStyles.Integer, DeCh, out var number) ||
               int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : throw new InvalidOperationException($"„{value}“ ist keine gültige Ganzzahl.");
    }

    private static double Number(string value, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, DeCh, out var number) ||
               double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out number)
            ? number
            : throw new InvalidOperationException($"„{value}“ ist keine gültige Zahl.");
    }

    private static bool Bool(string value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        if (value.Equals("Ja", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("True", StringComparison.OrdinalIgnoreCase) ||
            value == "1" ||
            value.Equals("X", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.Equals("Nein", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("False", StringComparison.OrdinalIgnoreCase) ||
            value == "0" ||
            value == "-")
            return false;

        throw new InvalidOperationException($"„{value}“ ist kein gültiger Ja/Nein-Wert.");
    }
}

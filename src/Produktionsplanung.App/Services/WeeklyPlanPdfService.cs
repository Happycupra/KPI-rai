using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App.Services;

public static class WeeklyPlanPdfService
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("de-CH");

    public static string BuildFileName(DateTime date)
    {
        var monday = Monday(date);
        return $"SolutionCompakt-Wochenplan-KW{ISOWeek.GetWeekOfYear(monday):00}-{monday:yyyy}.pdf";
    }

    public static WeeklyPlanPdfResult Export(DateTime date, string filePath, bool includeWeekends = true)
    {
        var monday = Monday(date);
        var rangeEnd = monday.AddDays(includeWeekends ? 6 : 4);
        var data = LoadData(monday, monday.AddDays(6));
        var settings = AppSettingsService.Load();
        var document = new PdfDocument();
        document.Info.Title = $"SolutionCompakt Wochenplan KW {ISOWeek.GetWeekOfYear(monday):00}";
        document.Info.Subject = "Produktions- und Personalwochenplan";
        document.Info.Author = settings.CompanyName;

        var rowsPerPage = 9;
        var pages = Math.Max(1, (int)Math.Ceiling(data.Rows.Count / (double)rowsPerPage));
        for (var pageIndex = 0; pageIndex < pages; pageIndex++)
        {
            DrawPage(
                document,
                data.Rows.Skip(pageIndex * rowsPerPage).Take(rowsPerPage).ToList(),
                data,
                settings,
                monday,
                rangeEnd,
                includeWeekends,
                pageIndex + 1,
                pages);
        }

        var pageCount = document.PageCount;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        // PdfSharp finalizes the in-memory document on Save. Do not access it afterwards.
        document.Save(filePath);

        return new WeeklyPlanPdfResult(
            filePath,
            pageCount,
            data.Rows.Count,
            data.RunSlotCount,
            data.AssignmentCount,
            includeWeekends);
    }

    private static WeeklyPlanData LoadData(DateTime monday, DateTime sunday)
    {
        using var db = new AppDbContext();
        ProductionScheduleService.EnsureMissingRunSlots(db);

        var slots = db.ProductionRunSlots.AsNoTracking()
            .Include(x => x.Shift)
            .Include(x => x.ProductionOrder)
                .ThenInclude(x => x.Workstation)
            .Where(x => x.Date.Date >= monday && x.Date.Date <= sunday)
            .AsEnumerable()
            .ToList();

        var assignments = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.Date.Date >= monday && x.Date.Date <= sunday)
            .AsEnumerable()
            .ToList();

        var keys = slots
            .Select(x => new WeeklyPlanRowKey(
                x.ProductionOrder.WorkstationId,
                x.ShiftId,
                x.ProductionOrder.Workstation.Name,
                x.ProductionOrder.Workstation.Area,
                x.Shift.Name,
                x.Shift.StartTime,
                x.Shift.EndTime))
            .Concat(assignments
                .Where(x => x.ShiftId.HasValue && x.Shift is not null)
                .Select(x => new WeeklyPlanRowKey(
                    x.WorkstationId,
                    x.ShiftId,
                    x.Workstation.Name,
                    x.Workstation.Area,
                    x.Shift!.Name,
                    x.Shift.StartTime,
                    x.Shift.EndTime)))
            .Concat(assignments
                .Where(x => !x.ShiftId.HasValue)
                .Select(x => new WeeklyPlanRowKey(
                    x.WorkstationId,
                    null,
                    x.Workstation.Name,
                    x.Workstation.Area,
                    "Individuell",
                    x.StartTime,
                    x.EndTime)))
            .DistinctBy(x => new { x.WorkstationId, x.ShiftId, x.ShiftStart, x.ShiftEnd })
            .OrderBy(x => x.WorkstationName)
            .ThenBy(x => x.ShiftStart)
            .ToList();

        var coverage = ProductionOrderCoverageService.Load(monday, sunday)
            .Where(x => x.RunSlotId > 0)
            .ToDictionary(x => x.RunSlotId);

        var rows = new List<WeeklyPlanPdfRow>();
        foreach (var key in keys)
        {
            var cells = new List<WeeklyPlanPdfCell>();
            for (var i = 0; i < 7; i++)
            {
                var day = monday.AddDays(i);
                var cellSlots = slots
                    .Where(x => day == x.Date.Date &&
                                x.ProductionOrder.WorkstationId == key.WorkstationId &&
                                key.ShiftId.HasValue &&
                                x.ShiftId == key.ShiftId)
                    .ToList();

                var cellAssignments = assignments
                    .Where(x => day == x.Date.Date &&
                                x.WorkstationId == key.WorkstationId &&
                                (key.ShiftId.HasValue
                                    ? x.ShiftId == key.ShiftId
                                    : !x.ShiftId.HasValue &&
                                      x.StartTime == key.ShiftStart &&
                                      x.EndTime == key.ShiftEnd))
                    .OrderBy(x => x.Employee.LastName)
                    .ToList();

                var products = string.Join(" / ", cellSlots
                    .Select(x => $"{x.ProductionOrder.Product} [{x.ProductionOrder.OrderNumber}] L{x.SequenceNumber}/{Math.Max(1, x.ProductionOrder.PlannedShiftCount)}")
                    .Distinct());
                var people = string.Join(", ", cellAssignments
                    .Select(x => Compact(x.Employee.FirstName, x.Employee.LastName))
                    .Distinct());
                var required = cellSlots.Count == 0 ? 0 : cellSlots.Max(x => x.ProductionOrder.RequiredStaff);
                var planned = cellAssignments.Select(x => x.EmployeeId).Distinct().Count();
                var understaffed = cellSlots.Any(x =>
                    coverage.TryGetValue(x.Id, out var row) && row.CoverageStatus == "Unterbesetzt");

                cells.Add(new WeeklyPlanPdfCell(day, products, people, planned, required, understaffed));
            }

            rows.Add(new WeeklyPlanPdfRow(key, cells));
        }

        return new WeeklyPlanData(rows, slots.Count, assignments.Count);
    }

    private static void DrawPage(
        PdfDocument document,
        IReadOnlyList<WeeklyPlanPdfRow> rows,
        WeeklyPlanData data,
        AppSettings settings,
        DateTime monday,
        DateTime rangeEnd,
        bool includeWeekends,
        int pageNumber,
        int pageCount)
    {
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        page.Orientation = PdfSharp.PageOrientation.Landscape;

        using var graphics = XGraphics.FromPdfPage(page);

        var navy = XColor.FromArgb(23, 58, 94);
        var blue = XColor.FromArgb(46, 111, 167);
        var border = XColor.FromArgb(210, 224, 238);
        var header = XColor.FromArgb(237, 244, 250);
        var weekend = XColor.FromArgb(248, 250, 252);
        var production = XColor.FromArgb(239, 246, 255);
        var warning = XColor.FromArgb(255, 247, 237);
        var muted = XColor.FromArgb(90, 107, 125);

        const double margin = 22;
        var titleFont = new XFont("Segoe UI", 16, XFontStyleEx.Bold);
        var smallFont = new XFont("Segoe UI", 6.5, XFontStyleEx.Regular);
        var boldFont = new XFont("Segoe UI", 6.7, XFontStyleEx.Bold);

        graphics.DrawRectangle(new XSolidBrush(navy), 0, 0, page.Width.Point, 9);
        graphics.DrawString("SolutionCompakt", titleFont, new XSolidBrush(navy),
            new XRect(margin, 22, 220, 22), XStringFormats.TopLeft);
        graphics.DrawString(
            $"WOCHENPLAN · KW {ISOWeek.GetWeekOfYear(monday):00} · {monday:dd.MM.yyyy}–{rangeEnd:dd.MM.yyyy}",
            new XFont("Segoe UI", 8, XFontStyleEx.Bold),
            new XSolidBrush(blue),
            new XRect(margin, 48, 340, 14),
            XStringFormats.TopLeft);

        var company = string.IsNullOrWhiteSpace(settings.SiteName)
            ? settings.CompanyName
            : $"{settings.CompanyName} · {settings.SiteName}";
        graphics.DrawString(
            $"{company} · Seite {pageNumber}/{pageCount} · {data.RunSlotCount} Produktionsschichten · {data.AssignmentCount} Personaleinsätze",
            smallFont,
            new XSolidBrush(muted),
            new XRect(400, 28, page.Width.Point - margin - 400, 20),
            XStringFormats.TopRight);

        var dayIndexes = includeWeekends
            ? Enumerable.Range(0, 7).ToArray()
            : Enumerable.Range(0, 5).ToArray();

        const double top = 80;
        const double workstationWidth = 105;
        const double shiftWidth = 78;
        var dayWidth = (page.Width.Point - 2 * margin - workstationWidth - shiftWidth) / dayIndexes.Length;
        const double headerHeight = 32;
        var rowHeight = rows.Count == 0
            ? 45
            : Math.Min(48, (page.Height.Point - top - headerHeight - 45) / rows.Count);

        var x = margin;
        Header("MASCHINE / ORT", workstationWidth);
        Header("SCHICHT / ZEIT", shiftWidth);
        foreach (var index in dayIndexes)
        {
            var day = monday.AddDays(index);
            Header(
                $"{day.ToString("ddd", Culture).ToUpperInvariant()}\n{day:dd.MM.}",
                dayWidth,
                day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? weekend : header);
        }

        var y = top + headerHeight;
        if (rows.Count == 0)
        {
            graphics.DrawString(
                "Für diese Woche sind keine Produktionsschichten oder Personaleinsätze geplant.",
                new XFont("Segoe UI", 9, XFontStyleEx.Regular),
                new XSolidBrush(muted),
                new XRect(margin, y, page.Width.Point - 2 * margin, 50),
                XStringFormats.Center);
            return;
        }

        foreach (var row in rows)
        {
            x = margin;
            Cell($"{row.Key.WorkstationName}\n{row.Key.Area}", workstationWidth, XColors.White, boldFont);
            Cell($"{row.Key.ShiftName}\n{row.Key.ShiftStart:hh\\:mm}–{row.Key.ShiftEnd:hh\\:mm}", shiftWidth, XColors.White, boldFont);

            foreach (var index in dayIndexes)
            {
                var cell = row.Cells[index];
                var fill = cell.Understaffed
                    ? warning
                    : cell.HasProduction
                        ? production
                        : cell.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                            ? weekend
                            : XColors.White;
                var text = cell.HasProduction
                    ? $"{cell.ProductText}\n{cell.EmployeeText}\nMA {cell.PlannedStaff}/{cell.RequiredStaff}"
                    : cell.EmployeeText;
                Cell(text, dayWidth, fill, cell.HasProduction ? boldFont : smallFont);
            }

            y += rowHeight;
        }

        void Header(string text, double width, XColor? fill = null)
        {
            graphics.DrawRectangle(new XPen(border), new XSolidBrush(fill ?? header), x, top, width, headerHeight);
            DrawText(text, new XRect(x + 3, top + 3, width - 6, headerHeight - 6), boldFont, navy, 2);
            x += width;
        }

        void Cell(string text, double width, XColor fill, XFont font)
        {
            graphics.DrawRectangle(new XPen(border), new XSolidBrush(fill), x, y, width, rowHeight);
            DrawText(text, new XRect(x + 3, y + 3, width - 6, rowHeight - 6), font, navy, 3);
            x += width;
        }

        void DrawText(string text, XRect rectangle, XFont font, XColor color, int maxLines)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var lines = text.Split('\n')
                .SelectMany(value => Wrap(value, font, rectangle.Width))
                .Take(maxLines)
                .ToList();
            for (var i = 0; i < lines.Count; i++)
            {
                graphics.DrawString(
                    lines[i],
                    font,
                    new XSolidBrush(color),
                    new XRect(rectangle.X, rectangle.Y + i * (font.Size + 2), rectangle.Width, font.Size + 3),
                    XStringFormats.TopLeft);
            }
        }

        IEnumerable<string> Wrap(string text, XFont font, double width)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = string.Empty;
            foreach (var word in words)
            {
                var next = line.Length == 0 ? word : $"{line} {word}";
                if (line.Length == 0 || graphics.MeasureString(next, font).Width <= width)
                {
                    line = next;
                }
                else
                {
                    yield return line;
                    line = word;
                }
            }

            if (line.Length > 0)
                yield return line;
        }
    }

    private static string Compact(string firstName, string lastName) =>
        string.IsNullOrWhiteSpace(firstName)
            ? lastName.Trim()
            : $"{lastName.Trim()} {char.ToUpperInvariant(firstName.Trim()[0])}.";

    private static DateTime Monday(DateTime date)
    {
        var days = (7 + ((int)date.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-days);
    }
}

public sealed record WeeklyPlanPdfResult(
    string FilePath,
    int PageCount,
    int RowCount,
    int ProductionShiftCount,
    int AssignmentCount,
    bool IncludesWeekends);

internal sealed record WeeklyPlanData(
    IReadOnlyList<WeeklyPlanPdfRow> Rows,
    int RunSlotCount,
    int AssignmentCount);

internal sealed record WeeklyPlanRowKey(
    int WorkstationId,
    int? ShiftId,
    string WorkstationName,
    string Area,
    string ShiftName,
    TimeSpan ShiftStart,
    TimeSpan ShiftEnd);

internal sealed record WeeklyPlanPdfRow(
    WeeklyPlanRowKey Key,
    IReadOnlyList<WeeklyPlanPdfCell> Cells);

internal sealed record WeeklyPlanPdfCell(
    DateTime Date,
    string ProductText,
    string EmployeeText,
    int PlannedStaff,
    int RequiredStaff,
    bool Understaffed)
{
    public bool HasProduction => !string.IsNullOrWhiteSpace(ProductText);
}

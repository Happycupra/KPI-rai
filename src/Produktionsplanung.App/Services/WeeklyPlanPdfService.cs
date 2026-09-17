using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App.Services;

public static class WeeklyPlanPdfService
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("de-CH");

    public static string BuildFileName(DateTime anyDateInWeek)
    {
        var monday = GetMonday(anyDateInWeek);
        return $"OpsCompact-Wochenplan-KW{ISOWeek.GetWeekOfYear(monday):00}-{monday:yyyy}.pdf";
    }

    public static WeeklyPlanPdfResult Export(DateTime anyDateInWeek, string filePath)
    {
        var monday = GetMonday(anyDateInWeek);
        var sunday = monday.AddDays(6);
        var data = LoadData(monday, sunday);
        var settings = AppSettingsService.Load();

        var document = new PdfDocument();
        document.Info.Title = $"OpsCompact Wochenplan KW {ISOWeek.GetWeekOfYear(monday):00}";
        document.Info.Subject = "Produktions- und Personalwochenplan";
        document.Info.Author = settings.CompanyName;

        var rows = data.Rows;
        var pageCount = rows.Count <= 9 ? 1 : 2;
        if (rows.Count == 0)
            pageCount = 1;

        var rowsPerPage = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)pageCount));
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var pageRows = rows.Skip(pageIndex * rowsPerPage).Take(rowsPerPage).ToList();
            DrawPage(document, pageRows, data, settings, monday, sunday, pageIndex + 1, pageCount);
        }

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        document.Save(filePath);

        return new WeeklyPlanPdfResult(filePath, document.PageCount, rows.Count, data.RunSlotCount, data.AssignmentCount);
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
            .OrderBy(x => x.ProductionOrder.Workstation.Name)
            .ThenBy(x => x.Shift.StartTime)
            .ThenBy(x => x.Date)
            .ToList();

        var assignments = db.PlanningAssignments.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.Workstation)
            .Include(x => x.Shift)
            .Where(x => x.Date.Date >= monday && x.Date.Date <= sunday)
            .AsEnumerable()
            .ToList();

        var rowKeys = slots
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
                    x.ShiftId!.Value,
                    x.Workstation.Name,
                    x.Workstation.Area,
                    x.Shift!.Name,
                    x.Shift.StartTime,
                    x.Shift.EndTime)))
            .DistinctBy(x => new { x.WorkstationId, x.ShiftId })
            .OrderBy(x => x.WorkstationName)
            .ThenBy(x => x.ShiftStart)
            .ToList();

        var coverage = ProductionOrderCoverageService.Load(monday, sunday)
            .Where(x => x.RunSlotId > 0)
            .ToDictionary(x => x.RunSlotId);

        var rows = new List<WeeklyPlanPdfRow>();
        foreach (var key in rowKeys)
        {
            var cells = new List<WeeklyPlanPdfCell>();
            for (var dayOffset = 0; dayOffset < 7; dayOffset++)
            {
                var date = monday.AddDays(dayOffset);
                var cellSlots = slots.Where(x =>
                        x.Date.Date == date &&
                        x.ProductionOrder.WorkstationId == key.WorkstationId &&
                        x.ShiftId == key.ShiftId)
                    .ToList();
                var cellAssignments = assignments.Where(x =>
                        x.Date.Date == date &&
                        x.WorkstationId == key.WorkstationId &&
                        x.ShiftId == key.ShiftId)
                    .OrderBy(x => x.Employee.LastName)
                    .ThenBy(x => x.Employee.FirstName)
                    .ToList();

                var productParts = cellSlots
                    .Select(x => $"{x.ProductionOrder.Product} [{x.ProductionOrder.OrderNumber}] L{x.SequenceNumber}/{Math.Max(1, x.ProductionOrder.PlannedShiftCount)}")
                    .Distinct()
                    .ToList();
                var employeeParts = cellAssignments
                    .Select(x => CompactEmployeeName(x.Employee.FirstName, x.Employee.LastName))
                    .Distinct()
                    .ToList();

                var required = cellSlots.Count == 0 ? 0 : cellSlots.Max(x => x.ProductionOrder.RequiredStaff);
                var planned = cellAssignments.Select(x => x.EmployeeId).Distinct().Count();
                var understaffed = cellSlots.Any(x => coverage.TryGetValue(x.Id, out var c) && c.CoverageStatus == "Unterbesetzt");

                cells.Add(new WeeklyPlanPdfCell(
                    date,
                    string.Join(" / ", productParts),
                    string.Join(", ", employeeParts),
                    planned,
                    required,
                    understaffed));
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
        DateTime sunday,
        int pageNumber,
        int pageCount)
    {
        var page = document.AddPage();
        page.Orientation = PdfSharp.PageOrientation.Landscape;
        page.Size = PdfSharp.PageSize.A4;

        using var gfx = XGraphics.FromPdfPage(page);
        var pageWidth = page.Width.Point;
        var pageHeight = page.Height.Point;

        var navy = XColor.FromArgb(23, 58, 94);
        var blue = XColor.FromArgb(46, 111, 167);
        var paleBlue = XColor.FromArgb(239, 246, 255);
        var headerFill = XColor.FromArgb(237, 244, 250);
        var border = XColor.FromArgb(210, 224, 238);
        var muted = XColor.FromArgb(90, 107, 125);
        var weekendFill = XColor.FromArgb(248, 250, 252);
        var warningFill = XColor.FromArgb(255, 247, 237);
        var warning = XColor.FromArgb(194, 65, 12);

        var margin = 22d;
        var headerHeight = 58d;
        var tableHeaderHeight = 34d;
        var footerHeight = 24d;
        var tableTop = margin + headerHeight;
        var tableBottom = pageHeight - margin - footerHeight;
        var availableRowsHeight = tableBottom - tableTop - tableHeaderHeight;
        var rowHeight = rows.Count == 0 ? 44d : Math.Min(52d, availableRowsHeight / rows.Count);
        var baseFontSize = rowHeight < 24 ? 5.2 : rowHeight < 31 ? 6.0 : rowHeight < 39 ? 6.7 : 7.3;

        var titleFont = new XFont("Segoe UI", 16, XFontStyleEx.Bold);
        var subFont = new XFont("Segoe UI", 8.5, XFontStyleEx.Regular);
        var headerFont = new XFont("Segoe UI", 7.4, XFontStyleEx.Bold);
        var workstationFont = new XFont("Segoe UI", Math.Max(6, baseFontSize), XFontStyleEx.Bold);
        var smallFont = new XFont("Segoe UI", Math.Max(5.2, baseFontSize - 0.7), XFontStyleEx.Regular);
        var productFont = new XFont("Segoe UI", baseFontSize, XFontStyleEx.Bold);
        var employeeFont = new XFont("Segoe UI", Math.Max(5.2, baseFontSize - 0.5), XFontStyleEx.Regular);
        var badgeFont = new XFont("Segoe UI", Math.Max(5.0, baseFontSize - 0.8), XFontStyleEx.Bold);

        gfx.DrawRectangle(new XSolidBrush(navy), 0, 0, pageWidth, 9);
        gfx.DrawString("OpsCompact", titleFont, new XSolidBrush(navy), new XRect(margin, margin, 180, 22), XStringFormats.TopLeft);
        gfx.DrawString($"WOCHENPLAN · KW {ISOWeek.GetWeekOfYear(monday):00}", headerFont, new XSolidBrush(blue), new XRect(margin, margin + 25, 230, 14), XStringFormats.TopLeft);
        gfx.DrawString($"{monday:dd.MM.yyyy} - {sunday:dd.MM.yyyy}", subFont, new XSolidBrush(muted), new XRect(margin, margin + 39, 230, 13), XStringFormats.TopLeft);

        var company = string.IsNullOrWhiteSpace(settings.SiteName)
            ? settings.CompanyName
            : $"{settings.CompanyName} · {settings.SiteName}";
        gfx.DrawString(company, subFont, new XSolidBrush(muted), new XRect(pageWidth - margin - 260, margin + 4, 260, 14), XStringFormats.TopRight);
        gfx.DrawString($"Seite {pageNumber}/{pageCount} · {data.RunSlotCount} Produktionsschichten · {data.AssignmentCount} Personaleinsätze",
            subFont, new XSolidBrush(muted), new XRect(pageWidth - margin - 330, margin + 25, 330, 14), XStringFormats.TopRight);

        var workstationWidth = 92d;
        var shiftWidth = 68d;
        var dayWidth = (pageWidth - 2 * margin - workstationWidth - shiftWidth) / 7d;
        var x = margin;
        var y = tableTop;

        DrawHeaderCell(gfx, x, y, workstationWidth, tableHeaderHeight, "MASCHINE / ORT", headerFont, navy, headerFill, border);
        x += workstationWidth;
        DrawHeaderCell(gfx, x, y, shiftWidth, tableHeaderHeight, "SCHICHT", headerFont, navy, headerFill, border);
        x += shiftWidth;

        for (var day = 0; day < 7; day++)
        {
            var date = monday.AddDays(day);
            var label = $"{date.ToString("ddd", Culture).ToUpperInvariant()}\n{date:dd.MM.}";
            var fill = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? weekendFill : headerFill;
            DrawHeaderCell(gfx, x, y, dayWidth, tableHeaderHeight, label, headerFont, navy, fill, border);
            x += dayWidth;
        }

        y += tableHeaderHeight;
        if (rows.Count == 0)
        {
            gfx.DrawRectangle(new XPen(border), new XSolidBrush(XColors.White), margin, y, pageWidth - 2 * margin, 60);
            gfx.DrawString("Für diese Woche sind noch keine Produktionsschichten oder Personaleinsätze geplant.",
                subFont, new XSolidBrush(muted), new XRect(margin + 10, y + 20, pageWidth - 2 * margin - 20, 20), XStringFormats.Center);
        }
        else
        {
            string? previousWorkstation = null;
            foreach (var row in rows)
            {
                var sameWorkstation = string.Equals(previousWorkstation, row.Key.WorkstationName, StringComparison.Ordinal);
                var rowFill = sameWorkstation ? XColors.White : XColor.FromArgb(252, 253, 255);
                x = margin;

                gfx.DrawRectangle(new XPen(border), new XSolidBrush(rowFill), x, y, workstationWidth, rowHeight);
                if (!sameWorkstation)
                {
                    DrawWrapped(gfx, row.Key.WorkstationName, workstationFont, new XSolidBrush(navy),
                        new XRect(x + 5, y + 5, workstationWidth - 10, rowHeight * 0.54), 2);
                    DrawWrapped(gfx, row.Key.Area, smallFont, new XSolidBrush(muted),
                        new XRect(x + 5, y + rowHeight * 0.55, workstationWidth - 10, rowHeight * 0.35), 1);
                }
                x += workstationWidth;

                gfx.DrawRectangle(new XPen(border), new XSolidBrush(rowFill), x, y, shiftWidth, rowHeight);
                DrawWrapped(gfx, row.Key.ShiftName, workstationFont, new XSolidBrush(navy), new XRect(x + 4, y + 5, shiftWidth - 8, rowHeight * 0.5), 2);
                gfx.DrawString($"{row.Key.ShiftStart:hh\\:mm}-{row.Key.ShiftEnd:hh\\:mm}", smallFont, new XSolidBrush(muted),
                    new XRect(x + 4, y + rowHeight * 0.61, shiftWidth - 8, 10), XStringFormats.TopLeft);
                x += shiftWidth;

                for (var day = 0; day < 7; day++)
                {
                    var cell = row.Cells[day];
                    var isWeekend = cell.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                    var fill = cell.Understaffed ? warningFill : cell.HasProduction ? paleBlue : isWeekend ? weekendFill : XColors.White;
                    gfx.DrawRectangle(new XPen(border), new XSolidBrush(fill), x, y, dayWidth, rowHeight);

                    var inner = new XRect(x + 4, y + 4, dayWidth - 8, rowHeight - 8);
                    if (cell.HasProduction)
                    {
                        DrawWrapped(gfx, cell.ProductText, productFont, new XSolidBrush(cell.Understaffed ? warning : navy),
                            new XRect(inner.X, inner.Y, inner.Width, Math.Max(10, rowHeight * 0.42)), 2);
                        DrawWrapped(gfx, cell.EmployeeText, employeeFont, new XSolidBrush(XColor.FromArgb(51, 65, 85)),
                            new XRect(inner.X, inner.Y + rowHeight * 0.42, inner.Width, Math.Max(9, rowHeight * 0.34)), 2);
                        var badge = cell.RequiredStaff > 0 ? $"MA {cell.PlannedStaff}/{cell.RequiredStaff}" : $"MA {cell.PlannedStaff}";
                        gfx.DrawString(badge, badgeFont, new XSolidBrush(cell.Understaffed ? warning : blue),
                            new XRect(inner.X, y + rowHeight - 12, inner.Width, 9), XStringFormats.BottomRight);
                    }
                    else if (!string.IsNullOrWhiteSpace(cell.EmployeeText))
                    {
                        DrawWrapped(gfx, cell.EmployeeText, employeeFont, new XSolidBrush(XColor.FromArgb(71, 85, 105)), inner, 3);
                    }

                    x += dayWidth;
                }

                previousWorkstation = row.Key.WorkstationName;
                y += rowHeight;
            }
        }

        var footerY = pageHeight - margin - 14;
        gfx.DrawString("Produkt/Auftrag und Laufnummer oben · Mitarbeitende darunter · MA geplant/benötigt · Orange = Unterbesetzung",
            new XFont("Segoe UI", 6.5, XFontStyleEx.Regular), new XSolidBrush(muted),
            new XRect(margin, footerY, pageWidth - 2 * margin, 10), XStringFormats.BottomLeft);
    }

    private static void DrawHeaderCell(XGraphics gfx, double x, double y, double width, double height, string text,
        XFont font, XColor textColor, XColor fillColor, XColor borderColor)
    {
        gfx.DrawRectangle(new XPen(borderColor), new XSolidBrush(fillColor), x, y, width, height);
        var lines = text.Split('\n');
        if (lines.Length == 1)
        {
            gfx.DrawString(text, font, new XSolidBrush(textColor), new XRect(x + 3, y, width - 6, height), XStringFormats.Center);
            return;
        }
        gfx.DrawString(lines[0], font, new XSolidBrush(textColor), new XRect(x + 3, y + 5, width - 6, 11), XStringFormats.TopCenter);
        gfx.DrawString(lines[1], font, new XSolidBrush(textColor), new XRect(x + 3, y + 18, width - 6, 11), XStringFormats.TopCenter);
    }

    private static void DrawWrapped(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text) || rect.Width <= 1 || rect.Height <= 1)
            return;

        var lines = WrapLines(gfx, text, font, rect.Width, maxLines);
        var lineHeight = Math.Max(font.Size + 1.2, rect.Height / Math.Max(1, maxLines));
        for (var i = 0; i < lines.Count; i++)
        {
            var y = rect.Y + i * lineHeight;
            if (y + lineHeight > rect.Bottom + 1)
                break;
            gfx.DrawString(lines[i], font, brush, new XRect(rect.X, y, rect.Width, lineHeight), XStringFormats.TopLeft);
        }
    }

    private static List<string> WrapLines(XGraphics gfx, string text, XFont font, double maxWidth, int maxLines)
    {
        var words = text.Replace("\r", " ").Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in words)
        {
            var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
            if (gfx.MeasureString(candidate, font).Width <= maxWidth || string.IsNullOrEmpty(current))
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = word;
            if (lines.Count >= maxLines)
                break;
        }
        if (lines.Count < maxLines && !string.IsNullOrWhiteSpace(current))
            lines.Add(current);

        if (lines.Count > maxLines)
            lines = lines.Take(maxLines).ToList();

        var reconstructed = string.Join(" ", lines);
        if (reconstructed.Length < text.Replace("\r", " ").Replace("\n", " ").Trim().Length && lines.Count > 0)
        {
            var last = lines[^1];
            while (last.Length > 1 && gfx.MeasureString(last + "…", font).Width > maxWidth)
                last = last[..^1];
            lines[^1] = last.TrimEnd() + "…";
        }

        return lines;
    }

    private static string CompactEmployeeName(string firstName, string lastName)
    {
        var initial = string.IsNullOrWhiteSpace(firstName) ? string.Empty : $" {char.ToUpperInvariant(firstName.Trim()[0])}.";
        return $"{lastName.Trim()}{initial}";
    }

    private static DateTime GetMonday(DateTime date)
    {
        var days = (7 + ((int)date.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-days);
    }
}

public sealed record WeeklyPlanPdfResult(string FilePath, int PageCount, int RowCount, int ProductionShiftCount, int AssignmentCount);

internal sealed record WeeklyPlanData(IReadOnlyList<WeeklyPlanPdfRow> Rows, int RunSlotCount, int AssignmentCount);

internal sealed record WeeklyPlanRowKey(
    int WorkstationId,
    int ShiftId,
    string WorkstationName,
    string Area,
    string ShiftName,
    TimeSpan ShiftStart,
    TimeSpan ShiftEnd);

internal sealed record WeeklyPlanPdfRow(WeeklyPlanRowKey Key, IReadOnlyList<WeeklyPlanPdfCell> Cells);

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

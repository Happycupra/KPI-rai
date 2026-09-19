using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Produktionsplanung.App.ViewModels;

namespace Produktionsplanung.App.Services;

public static class PlanningCalendarPdfService
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("de-CH");

    public static string BuildFileName(PlanningCalendarViewModel viewModel)
    {
        var suffix = viewModel.SelectedViewIndex switch
        {
            0 => $"Tag-{viewModel.SelectedDate:yyyy-MM-dd}",
            1 => $"Woche-KW{ISOWeek.GetWeekOfYear(viewModel.SelectedDate):00}-{viewModel.SelectedDate:yyyy}",
            _ => $"Monat-{viewModel.SelectedDate:yyyy-MM}"
        };
        return $"SolutionCompakt-Planungskalender-{suffix}.pdf";
    }

    public static PlanningCalendarPdfResult Export(PlanningCalendarViewModel viewModel, string filePath)
    {
        var document = new PdfDocument();
        var settings = AppSettingsService.Load();
        document.Info.Title = $"SolutionCompakt Planungskalender · {viewModel.HeaderText}";
        document.Info.Subject = "Planungskalender";
        document.Info.Author = settings.CompanyName;

        switch (viewModel.SelectedViewIndex)
        {
            case 0:
                DrawDay(document, viewModel, settings);
                break;
            case 1:
                DrawWeek(document, viewModel, settings);
                break;
            default:
                DrawMonth(document, viewModel, settings);
                break;
        }

        var pageCount = document.PageCount;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        document.Save(filePath);
        return new PlanningCalendarPdfResult(filePath, pageCount, viewModel.SelectedViewIndex, viewModel.ShowWeekends);
    }

    private static void DrawDay(PdfDocument document, PlanningCalendarViewModel viewModel, AppSettings settings)
    {
        var entries = viewModel.DayAllDayEntries.Concat(viewModel.DayTimedEntries).ToList();
        const int perPage = 18;
        var pages = Math.Max(1, (int)Math.Ceiling(entries.Count / (double)perPage));

        for (var pageIndex = 0; pageIndex < pages; pageIndex++)
        {
            var pageEntries = entries.Skip(pageIndex * perPage).Take(perPage).ToList();
            var (page, graphics, palette) = BeginPage(document, settings, "PLANUNGSKALENDER · TAG",
                viewModel.HeaderText, pageIndex + 1, pages);

            using (graphics)
            {
                var y = 82d;
                if (pageEntries.Count == 0)
                {
                    DrawEmpty(graphics, page, y, "Keine sichtbaren Einträge für diesen Tag.", palette.Muted);
                    continue;
                }

                foreach (var entry in pageEntries)
                {
                    var accent = ParseColor(entry.Accent, palette.Blue);
                    graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(248, 251, 255)), 24, y, page.Width.Point - 48, 27);
                    graphics.DrawRectangle(new XSolidBrush(accent), 24, y, 4, 27);
                    graphics.DrawString(entry.TypeLabel, palette.TinyBold, new XSolidBrush(accent),
                        new XRect(35, y + 4, 105, 10), XStringFormats.TopLeft);
                    graphics.DrawString(entry.TimeText, palette.Tiny, new XSolidBrush(palette.Muted),
                        new XRect(140, y + 4, 90, 10), XStringFormats.TopLeft);
                    graphics.DrawString(entry.Title, palette.BodyBold, new XSolidBrush(palette.Navy),
                        new XRect(35, y + 14, 260, 11), XStringFormats.TopLeft);
                    graphics.DrawString(entry.Subtitle, palette.Tiny, new XSolidBrush(palette.Muted),
                        new XRect(305, y + 14, page.Width.Point - 330, 11), XStringFormats.TopLeft);
                    y += 31;
                }
            }
        }
    }

    private static void DrawWeek(PdfDocument document, PlanningCalendarViewModel viewModel, AppSettings settings)
    {
        var (page, graphics, palette) = BeginPage(document, settings, "PLANUNGSKALENDER · WOCHE",
            viewModel.HeaderText, 1, 1);

        using (graphics)
        {
            var days = viewModel.WeekDays.ToList();
            if (days.Count == 0)
            {
                DrawEmpty(graphics, page, 82, "Keine sichtbaren Kalendertage.", palette.Muted);
                return;
            }

            const double margin = 22;
            const double top = 82;
            var width = (page.Width.Point - margin * 2 - (days.Count - 1) * 5) / days.Count;
            var height = page.Height.Point - top - 30;

            for (var dayIndex = 0; dayIndex < days.Count; dayIndex++)
            {
                var day = days[dayIndex];
                var x = margin + dayIndex * (width + 5);
                graphics.DrawRectangle(new XPen(palette.Border), new XSolidBrush(palette.Header), x, top, width, 35);
                graphics.DrawString(day.DayName.ToUpperInvariant(), palette.TinyBold, new XSolidBrush(palette.Muted),
                    new XRect(x + 7, top + 5, width - 14, 10), XStringFormats.TopLeft);
                graphics.DrawString(day.DateText, palette.BodyBold, new XSolidBrush(palette.Navy),
                    new XRect(x + 7, top + 16, width - 14, 14), XStringFormats.TopLeft);

                var entries = day.AllDayEntries.Concat(day.TimedEntries).Take(11).ToList();
                var y = top + 40;
                foreach (var entry in entries)
                {
                    graphics.DrawRectangle(new XPen(palette.Border), new XSolidBrush(XColors.White), x, y, width, 34);
                    graphics.DrawRectangle(new XSolidBrush(ParseColor(entry.Accent, palette.Blue)), x, y, 3, 34);
                    DrawClipped(graphics, entry.TimeText, palette.Tiny, palette.Muted,
                        new XRect(x + 7, y + 4, width - 14, 9));
                    DrawClipped(graphics, entry.Title, palette.SmallBold, palette.Navy,
                        new XRect(x + 7, y + 14, width - 14, 11));
                    DrawClipped(graphics, entry.Subtitle, palette.Tiny, palette.Muted,
                        new XRect(x + 7, y + 24, width - 14, 9));
                    y += 37;
                    if (y + 38 > top + height)
                        break;
                }

                var total = day.EntryCount;
                if (total > entries.Count)
                    graphics.DrawString($"+ {total - entries.Count} weitere", palette.TinyBold, new XSolidBrush(palette.Muted),
                        new XRect(x + 7, Math.Min(y, top + height - 12), width - 14, 10), XStringFormats.TopLeft);
            }
        }
    }

    private static void DrawMonth(PdfDocument document, PlanningCalendarViewModel viewModel, AppSettings settings)
    {
        var (page, graphics, palette) = BeginPage(document, settings, "PLANUNGSKALENDER · MONAT",
            viewModel.HeaderText, 1, 1);

        using (graphics)
        {
            var columns = viewModel.ShowWeekends ? 7 : 5;
            var days = viewModel.MonthDays.ToList();
            const double margin = 22;
            const double top = 82;
            const double gap = 3;
            var cellWidth = (page.Width.Point - margin * 2 - gap * (columns - 1)) / columns;
            var cellHeight = (page.Height.Point - top - 28 - gap * 5) / 6;
            var names = viewModel.ShowWeekends
                ? new[] { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" }
                : new[] { "Mo", "Di", "Mi", "Do", "Fr" };

            for (var col = 0; col < columns; col++)
            {
                var x = margin + col * (cellWidth + gap);
                graphics.DrawString(names[col], palette.SmallBold, new XSolidBrush(palette.Muted),
                    new XRect(x, 68, cellWidth, 12), XStringFormats.Center);
            }

            for (var index = 0; index < Math.Min(days.Count, columns * 6); index++)
            {
                var row = index / columns;
                var col = index % columns;
                var day = days[index];
                var x = margin + col * (cellWidth + gap);
                var y = top + row * (cellHeight + gap);

                graphics.DrawRectangle(new XPen(palette.Border), new XSolidBrush(XColors.White), x, y, cellWidth, cellHeight);
                graphics.DrawString(day.DayNumber, palette.BodyBold, new XSolidBrush(palette.Navy),
                    new XRect(x + 6, y + 5, 22, 13), XStringFormats.TopLeft);

                var entryY = y + 22;
                foreach (var entry in day.Entries.Take(3))
                {
                    var accent = ParseColor(entry.Accent, palette.Blue);
                    graphics.DrawRectangle(new XSolidBrush(accent), x + 6, entryY + 2, 3, 10);
                    DrawClipped(graphics, entry.Title, palette.Tiny, palette.Navy,
                        new XRect(x + 13, entryY, cellWidth - 18, 12));
                    entryY += 14;
                }

                if (day.HiddenEntryCount > 0)
                    graphics.DrawString(day.MoreText, palette.Tiny, new XSolidBrush(palette.Muted),
                        new XRect(x + 13, entryY, cellWidth - 18, 10), XStringFormats.TopLeft);
            }
        }
    }

    private static (PdfPage Page, XGraphics Graphics, CalendarPdfPalette Palette) BeginPage(
        PdfDocument document,
        AppSettings settings,
        string title,
        string subtitle,
        int pageNumber,
        int pageCount)
    {
        var page = document.AddPage();
        page.Orientation = PageOrientation.Landscape;
        page.Size = PageSize.A4;
        var graphics = XGraphics.FromPdfPage(page);
        var palette = CalendarPdfPalette.Create();

        graphics.DrawRectangle(new XSolidBrush(palette.Navy), 0, 0, page.Width.Point, 9);
        graphics.DrawString("SolutionCompakt", palette.Title, new XSolidBrush(palette.Navy),
            new XRect(24, 22, 220, 22), XStringFormats.TopLeft);
        graphics.DrawString(title, palette.SmallBold, new XSolidBrush(palette.Blue),
            new XRect(24, 48, 250, 12), XStringFormats.TopLeft);
        graphics.DrawString(subtitle, palette.Small, new XSolidBrush(palette.Muted),
            new XRect(280, 48, 300, 12), XStringFormats.TopLeft);

        var company = string.IsNullOrWhiteSpace(settings.SiteName)
            ? settings.CompanyName
            : $"{settings.CompanyName} · {settings.SiteName}";
        graphics.DrawString($"{company} · Seite {pageNumber}/{pageCount}", palette.Tiny,
            new XSolidBrush(palette.Muted),
            new XRect(580, 28, page.Width.Point - 604, 16), XStringFormats.TopRight);

        return (page, graphics, palette);
    }

    private static void DrawEmpty(XGraphics graphics, PdfPage page, double y, string text, XColor muted)
    {
        graphics.DrawString(text, new XFont("Segoe UI", 10, XFontStyleEx.Regular), new XSolidBrush(muted),
            new XRect(24, y, page.Width.Point - 48, 50), XStringFormats.Center);
    }

    private static void DrawClipped(XGraphics graphics, string text, XFont font, XColor color, XRect rect)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var value = text;
        while (value.Length > 1 && graphics.MeasureString(value, font).Width > rect.Width)
            value = value[..^1];

        if (value.Length < text.Length && value.Length > 2)
            value = value[..Math.Max(1, value.Length - 1)] + "…";

        graphics.DrawString(value, font, new XSolidBrush(color), rect, XStringFormats.TopLeft);
    }

    private static XColor ParseColor(string value, XColor fallback)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith('#'))
            return fallback;

        try
        {
            var hex = value.TrimStart('#');
            if (hex.Length == 6)
            {
                return XColor.FromArgb(
                    Convert.ToByte(hex[..2], 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16));
            }
        }
        catch
        {
            // Fall back to the application palette.
        }

        return fallback;
    }

    private sealed class CalendarPdfPalette
    {
        public XColor Navy { get; init; }
        public XColor Blue { get; init; }
        public XColor Muted { get; init; }
        public XColor Border { get; init; }
        public XColor Header { get; init; }
        public XFont Title { get; init; } = null!;
        public XFont BodyBold { get; init; } = null!;
        public XFont Small { get; init; } = null!;
        public XFont SmallBold { get; init; } = null!;
        public XFont Tiny { get; init; } = null!;
        public XFont TinyBold { get; init; } = null!;

        public static CalendarPdfPalette Create() => new()
        {
            Navy = XColor.FromArgb(23, 58, 94),
            Blue = XColor.FromArgb(46, 111, 167),
            Muted = XColor.FromArgb(90, 107, 125),
            Border = XColor.FromArgb(210, 224, 238),
            Header = XColor.FromArgb(237, 244, 250),
            Title = new XFont("Segoe UI", 16, XFontStyleEx.Bold),
            BodyBold = new XFont("Segoe UI", 8, XFontStyleEx.Bold),
            Small = new XFont("Segoe UI", 7, XFontStyleEx.Regular),
            SmallBold = new XFont("Segoe UI", 7, XFontStyleEx.Bold),
            Tiny = new XFont("Segoe UI", 5.8, XFontStyleEx.Regular),
            TinyBold = new XFont("Segoe UI", 5.8, XFontStyleEx.Bold)
        };
    }
}

public sealed record PlanningCalendarPdfResult(
    string FilePath,
    int PageCount,
    int ViewIndex,
    bool IncludesWeekends);

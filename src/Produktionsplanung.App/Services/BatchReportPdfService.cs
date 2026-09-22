using System.Globalization;
using System.IO;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App.Services;

public sealed record BatchReportResult(string FilePath, int PageCount);

public static class BatchReportPdfService
{
    public static string BuildFileName(int orderId) => $"SolutionCompakt-Chargenbericht-{orderId}.pdf";

    public static BatchReportResult Export(int orderId, string filePath)
    {
        var details = BatchService.GetDetails(orderId);
        using var db = new AppDbContext();
        var order = db.ProductionOrders.AsNoTracking().Single(x => x.Id == orderId);
        var slots = db.ProductionRunSlots.AsNoTracking().Where(x => x.ProductionOrderId == orderId).Select(x => x.Id).ToArray();
        var summary = BatchComparisonService.Summarize(details.Batch, details.Actuals, slots);
        var batch = details.Batch;
        using var report = new ReportWriter();
        report.Section(batch.Status == "Abgeschlossen" ? "Abgeschlossene Charge" : "Zwischenbericht - laufender Datenstand");
        report.Line($"Artikel: {batch.ArticleNumber} - {batch.Product}", bold: true);
        report.Line($"Charge: {batch.BatchLabel} | Auftrag: {batch.OrderNumber}");
        report.Line($"Status: {batch.Status} | Sollmenge: {batch.QuantityText}");
        report.Line($"Plantermin: {batch.PlannedDate:dd.MM.yyyy} | Arbeitsplatz: {batch.Workstation}");
        report.Line($"Produktionsbeginn: {batch.StartedText}");
        report.Line($"Abschluss: {batch.CompletedText}");
        report.Line($"Arbeitsfortschritt: {batch.ProgressText}");
        if (!batch.ArticleId.HasValue) report.Line("Artikelzuordnung offen");
        report.Section("Produktionskennzahlen aus Ist-Erfassungen");
        report.Line($"Gutmenge: {summary.GoodText} | Ausschuss: {summary.ScrapText}");
        report.Line($"Ausschussquote: {summary.ScrapPercentText} | Gutmenge pro Laufstunde: {summary.RateText}");
        report.Line($"Laufzeit: {summary.RunText} | Erfasste Stillstände: {summary.DowntimeText}");
        report.Line($"Datenstand: {summary.DataNote}");
        report.Line("Arbeitskartenmengen werden nicht zu den Ist-Erfassungen addiert. Null erfasste Stillstandsminuten belegen keinen störungsfreien Lauf.");
        report.Section("Beschreibung und Bemerkungen zum Auftrag");
        report.Line(Empty(order.Description));
        report.Line(Empty(order.Comment));
        report.Section("Arbeitsgänge");
        if (details.JobCards.Count == 0) report.Line("Nicht erfasst");
        foreach (var card in details.JobCards)
        {
            report.Subheading($"{card.SequenceNumber}. {card.OperationCode} - {card.OperationName}");
            report.Line($"Status: {card.Status} | Arbeitsplatz: {card.Workstation.Name}");
            report.Line($"Mitarbeiter: {(card.Employee is null ? "Nicht erfasst" : card.Employee.FirstName + " " + card.Employee.LastName)}");
            report.Line($"Sollzeit: {Number(card.PlannedMinutes)} min | Laufzeit: {Number(card.RunMinutes)} min");
            report.Line($"Fertigmeldung: {BatchRow.FormatDate(card.CompletedAtUtc, "Nicht erfasst")}");
            report.Line($"Arbeitskartenmengen: Gut {Number(card.GoodQuantity)} / Ausschuss {Number(card.ScrapQuantity)} {batch.Unit}");
            report.Line($"Bemerkung: {Empty(card.Comment)}");
        }
        report.Section("Ist-Erfassungen und Stillstände");
        if (details.Actuals.Count == 0) report.Line("Nicht erfasst");
        foreach (var actual in details.Actuals)
        {
            report.Subheading($"{actual.Date:dd.MM.yyyy} | Erfassung {actual.Id}");
            report.Line($"Artikel (historisch): {Empty(actual.ArticleNumberSnapshot)} | Charge (historisch): {Empty(actual.BatchNumber)}");
            report.Line($"Einheit (historisch): {Empty(actual.UnitSnapshot)}");
            if ((!string.IsNullOrWhiteSpace(actual.BatchNumber) && actual.BatchNumber != batch.BatchNumber) ||
                (!string.IsNullOrWhiteSpace(actual.ArticleNumberSnapshot) && actual.ArticleNumberSnapshot != batch.ArticleNumber) ||
                (!string.IsNullOrWhiteSpace(actual.UnitSnapshot) && actual.UnitSnapshot != batch.Unit))
                report.Line("Hinweis: Historische Artikel-, Chargen- oder Einheitenangaben weichen vom Auftrag ab.", bold: true);
            report.Line($"Gutmenge: {Number(actual.GoodQuantity)} | Ausschuss: {Number(actual.ScrapQuantity)} | Gesamtmenge: {Number(actual.TotalQuantity)}");
            report.Line($"Planzeit: {Number(actual.PlannedProductionMinutes)} min | Laufzeit: {Number(actual.RunMinutes)} min");
            report.Line($"Bemerkung: {Empty(actual.Comment)}");
            if (actual.Downtimes.Count == 0) report.Line("Stillstandsdetails: Nicht erfasst");
            foreach (var downtime in actual.Downtimes.OrderBy(x => x.Id))
                report.Line($"Stillstand: {downtime.Reason} | {Number(downtime.Minutes)} min | {Empty(downtime.Comment)}");
        }
        var pages = report.Save(filePath);
        return new(filePath, pages);
    }

    private static string Empty(string? value) => string.IsNullOrWhiteSpace(value) ? "Nicht erfasst" : value;
    private static string Number(double value) => double.IsFinite(value) ? value.ToString("N2", CultureInfo.GetCultureInfo("de-CH")) : "Ungültiger Wert";

    // Explicit wrapping and pagination keep long product names and comments intact.
    private sealed class ReportWriter : IDisposable
    {
        private readonly PdfDocument document = new();
        private readonly XFont body = new("Arial", 10);
        private readonly XFont bold = new("Arial", 10, XFontStyleEx.Bold);
        private readonly XFont heading = new("Arial", 14, XFontStyleEx.Bold);
        private XGraphics? graphics;
        private PdfPage page = null!;
        private double y;
        private const double Margin = 42;
        private double Width => page.Width.Point - 2 * Margin;
        public ReportWriter()
        {
            document.Info.Title = "SolutionCompakt Chargenbericht";
            document.Info.Author = "SolutionCompakt";
            NewPage();
        }
        private void NewPage()
        {
            graphics?.Dispose();
            page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            graphics = XGraphics.FromPdfPage(page);
            graphics.DrawString("SolutionCompakt | Chargenbericht", heading, XBrushes.DarkSlateBlue,
                new XRect(Margin, 30, Width, 22), XStringFormats.TopLeft);
            graphics.DrawLine(XPens.LightGray, Margin, 61, page.Width.Point - Margin, 61);
            y = 79;
        }
        private void EnsureSpace(double height) { if (y + height > page.Height.Point - 55) NewPage(); }
        public void Section(string text)
        {
            EnsureSpace(62);
            y += 12;
            DrawWrapped(text, heading, 20, XBrushes.DarkSlateBlue);
            y += 6;
        }
        public void Subheading(string text) { EnsureSpace(48); y += 8; Line(text, true); }
        public void Line(string text, bool bold = false) => DrawWrapped(text, bold ? this.bold : body, 15, XBrushes.Black);
        private void DrawWrapped(string text, XFont font, double lineHeight, XBrush brush)
        {
            foreach (var paragraph in text.Replace("\r", "").Replace("\t", "    ").Split('\n'))
            {
                var remaining = new string(paragraph.Where(c => !char.IsControl(c)).ToArray());
                if (remaining.Length == 0) { EnsureSpace(lineHeight); y += lineHeight; continue; }
                while (remaining.Length > 0)
                {
                    EnsureSpace(lineHeight);
                    var low = 1;
                    var high = remaining.Length;
                    while (low < high)
                    {
                        var mid = (low + high + 1) / 2;
                        if (graphics!.MeasureString(remaining[..mid], font).Width <= Width) low = mid;
                        else high = mid - 1;
                    }
                    var length = low;
                    if (length < remaining.Length)
                    {
                        var space = remaining.LastIndexOf(' ', length - 1, length);
                        if (space > 0) length = space;
                        if (length > 1 && char.IsHighSurrogate(remaining[length - 1])) length--;
                    }
                    graphics!.DrawString(remaining[..length], font, brush, new XRect(Margin, y, Width, lineHeight), XStringFormats.TopLeft);
                    remaining = remaining[length..].TrimStart();
                    y += lineHeight;
                }
            }
        }
        public int Save(string path)
        {
            graphics?.Dispose();
            graphics = null;
            var count = document.PageCount;
            var stamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            for (var i = 0; i < count; i++)
            {
                var p = document.Pages[i];
                using var footer = XGraphics.FromPdfPage(p, XGraphicsPdfPageOptions.Append);
                footer.DrawString($"Erstellt: {stamp} | Seite {i + 1} / {count}", body, XBrushes.Gray,
                    new XRect(Margin, p.Height.Point - 35, p.Width.Point - 2 * Margin, 18), XStringFormats.TopLeft);
            }
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { document.Save(temporary); File.Move(temporary, fullPath, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return count;
        }
        public void Dispose() { graphics?.Dispose(); document.Dispose(); }
    }
}

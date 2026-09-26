using PdfSharp.Drawing;
using PdfSharp.Pdf;
using QRCoder;

namespace Produktionsplanung.App.Services;

public static class UserLoginQrCodeService
{
    public const string LoginBaseUrl = "https://solution-compact.web.app/";

    public static string BuildLoginUrl(string companyCode, string username)
    {
        var company = Uri.EscapeDataString((companyCode ?? string.Empty).Trim().ToUpperInvariant());
        var user = Uri.EscapeDataString((username ?? string.Empty).Trim());
        return $"{LoginBaseUrl}?companyCode={company}&username={user}";
    }

    public static byte[] CreatePng(string loginUrl, int pixelsPerModule = 12)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(loginUrl, QRCodeGenerator.ECCLevel.Q);
        var qr = new PngByteQRCode(data);
        return qr.GetGraphic(pixelsPerModule);
    }

    public static void ExportPdf(
        string filePath,
        string companyName,
        string companyCode,
        string username,
        string displayName,
        string loginUrl,
        byte[] qrPng)
    {
        var document = new PdfDocument();
        document.Info.Title = $"SolutionCompakt Login QR-Code · {username}";
        document.Info.Subject = "Online-Wochenplan Login";
        document.Info.Author = companyName;

        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using var gfx = XGraphics.FromPdfPage(page);

        var title = new XFont("Arial", 22, XFontStyleEx.Bold);
        var heading = new XFont("Arial", 12, XFontStyleEx.Bold);
        var body = new XFont("Arial", 10);
        var muted = new XFont("Arial", 9);

        const double left = 55;
        double y = 62;
        gfx.DrawString("SolutionCompakt", title, XBrushes.Black, new XPoint(left, y));
        y += 28;
        gfx.DrawString("Persönlicher Login-QR-Code", heading, XBrushes.Black, new XPoint(left, y));
        y += 26;

        gfx.DrawString($"Firma: {companyName}", body, XBrushes.Black, new XPoint(left, y));
        y += 18;
        gfx.DrawString($"Firmen-Code: {companyCode}", body, XBrushes.Black, new XPoint(left, y));
        y += 18;
        gfx.DrawString($"Benutzer: {displayName} ({username})", body, XBrushes.Black, new XPoint(left, y));
        y += 28;

        var tempPath = Path.Combine(Path.GetTempPath(), "solutioncompakt-qr-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(tempPath, qrPng);
            using var image = XImage.FromFile(tempPath);
            const double size = 250;
            gfx.DrawImage(image, left, y, size, size);
            y += size + 22;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }

        gfx.DrawString("QR-Code scannen → Login-Seite öffnet sich mit Firmen-Code und Benutzername.", body, XBrushes.Black, new XPoint(left, y));
        y += 18;
        gfx.DrawString("Das Passwort ist nicht im QR-Code enthalten und muss immer separat eingegeben werden.", heading, XBrushes.Black, new XPoint(left, y));
        y += 24;

        var linkRect = new XRect(left, y, page.Width.Point - left * 2, 54);
        gfx.DrawRectangle(XPens.LightGray, linkRect);
        gfx.DrawString(loginUrl, muted, XBrushes.Black, new XRect(left + 8, y + 8, linkRect.Width - 16, 38), XStringFormats.TopLeft);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        document.Save(filePath);
    }
}

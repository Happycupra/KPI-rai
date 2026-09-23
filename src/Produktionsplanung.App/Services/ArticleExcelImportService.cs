using ClosedXML.Excel;
using Produktionsplanung.App.Models;

namespace Produktionsplanung.App.Services;

public sealed record ArticleImportResult(int Imported, int Updated, IReadOnlyList<string> Errors)
{
    public string Summary => Errors.Count == 0 ? $"Excel-Import abgeschlossen: {Imported} neu, {Updated} aktualisiert." : $"Excel-Import: {Imported} neu, {Updated} aktualisiert, {Errors.Count} Fehler.";
}
public static class ArticleExcelImportService
{
    public static readonly string[] Headers = { "Artikelnummer", "Name", "Einheit", "Standardmenge", "Sollrate_pro_Stunde", "Aktiv", "Notiz" };
    public static void CreateTemplate(string path)
    {
        using var workbook = new XLWorkbook(); var ws = workbook.Worksheets.Add("Artikel");
        for (var i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
        ws.Cell(2,1).Value="0001"; ws.Cell(2,2).Value="Beispielartikel"; ws.Cell(2,3).Value="Stück"; ws.Cell(2,4).Value=1000; ws.Cell(2,5).Value=120; ws.Cell(2,6).Value="Ja"; ws.Cell(2,7).Value="Beispielzeile vor Import löschen";
        ws.Column(1).Style.NumberFormat.Format="@"; ws.Range(1,1,1,Headers.Length).Style.Font.Bold=true; ws.SheetView.FreezeRows(1); ws.Columns().AdjustToContents(); workbook.SaveAs(path);
    }
    public static ArticleImportResult Import(string path)
    {
        BatchService.RequirePlanner(); using var workbook=new XLWorkbook(path); var ws=workbook.Worksheets.FirstOrDefault() ?? throw new InvalidOperationException("Die Excel-Datei enthält kein Tabellenblatt.");
        var map=ws.Row(1).CellsUsed().ToDictionary(c=>c.GetString().Trim(),c=>c.Address.ColumnNumber,StringComparer.OrdinalIgnoreCase);
        foreach(var required in new[]{"Artikelnummer","Name","Einheit","Sollrate_pro_Stunde"}) if(!map.ContainsKey(required)) throw new InvalidOperationException($"Pflichtspalte „{required}“ fehlt. Bitte die SolutionCompakt-Vorlage verwenden.");
        var imported=0; var updated=0; var errors=new List<string>(); var last=ws.LastRowUsed()?.RowNumber()??1;
        for(var row=2;row<=last;row++){ var number=Cell(ws,row,map,"Artikelnummer"); if(string.IsNullOrWhiteSpace(number)) continue;
            try { var existing=ArticleService.Search(number).FirstOrDefault(x=>string.Equals(x.ArticleNumber,number.Trim(),StringComparison.OrdinalIgnoreCase)); var article=existing is null?new ArticleMaster():ArticleService.GetDetails(existing.Id);
                article.ArticleNumber=number.Trim(); article.Name=Cell(ws,row,map,"Name").Trim(); article.Unit=Cell(ws,row,map,"Einheit").Trim(); article.DefaultQuantity=OptionalDouble(Cell(ws,row,map,"Standardmenge")); article.DefaultIdealRatePerHour=RequiredDouble(Cell(ws,row,map,"Sollrate_pro_Stunde"),"Sollrate_pro_Stunde"); article.IsActive=ParseActive(Cell(ws,row,map,"Aktiv")); article.Notes=NullIfEmpty(Cell(ws,row,map,"Notiz")); ArticleService.Save(article); if(existing is null) imported++; else updated++;
            } catch(Exception ex){errors.Add($"Zeile {row} ({number}): {ex.Message}");}
        } return new(imported,updated,errors);
    }
    private static string Cell(IXLWorksheet ws,int row,Dictionary<string,int> map,string name)=>map.TryGetValue(name,out var col)?ws.Cell(row,col).GetFormattedString().Trim():"";
    private static double RequiredDouble(string value,string field)=>(double.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.CurrentCulture,out var n)||double.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out n))?n:throw new InvalidOperationException($"{field} ist keine gültige Zahl.");
    private static double? OptionalDouble(string value)=>string.IsNullOrWhiteSpace(value)?null:RequiredDouble(value,"Standardmenge");
    private static bool ParseActive(string value)=>string.IsNullOrWhiteSpace(value)||value.Equals("Ja",StringComparison.OrdinalIgnoreCase)||value.Equals("1")||value.Equals("true",StringComparison.OrdinalIgnoreCase);
    private static string? NullIfEmpty(string value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
}
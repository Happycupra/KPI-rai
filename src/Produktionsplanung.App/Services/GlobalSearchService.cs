using Microsoft.EntityFrameworkCore;
using Produktionsplanung.App.Data;

namespace Produktionsplanung.App.Services;

internal enum GlobalSearchTargetKind
{
    Area,
    SettingsDashboard,
    SettingsTab,
    Employee,
    ProductionOrder,
    Batch
}

internal sealed record GlobalSearchResult(
    string Icon,
    string Title,
    string Subtitle,
    string Category,
    string Path,
    GlobalSearchTargetKind TargetKind,
    string? AreaKey = null,
    int? EntityId = null,
    int? SettingsTab = null,
    string SearchKeywords = "");

internal static class GlobalSearchService
{
    private sealed record CatalogEntry(GlobalSearchResult Result, bool PlannerOnly = false, bool AdminOnly = false);

    private static readonly CatalogEntry[] Catalog =
    {
        Entry("🏠", "Dashboard", "Übersicht und aktuelle Hinweise", "Navigation", "Übersicht & Planung › Dashboard", "dashboard", keywords: "start übersicht home"),
        Entry("📅", "Planung", "Tag, Woche und Monat planen", "Navigation", "Übersicht & Planung › Planung", "planning", planner: true, keywords: "kalender personalplanung wochenplanung tagesplanung"),
        Entry("🕒", "Arbeitszeit / Betrieb", "Arbeitszeiten und Betriebstage", "Navigation", "Übersicht & Planung › Arbeitszeit / Betrieb", "worktime", planner: true, keywords: "arbeitszeit betrieb kalender sollzeit"),
        Entry("📆", "Abwesenheiten", "Ferien, Krankheit und sonstige Abwesenheiten", "Navigation", "Übersicht & Planung › Abwesenheiten", "absences", planner: true, keywords: "ferien krank urlaub absence"),
        Entry("📋", "Produktionsaufträge", "Aufträge planen und verwalten", "Produktion", "Produktion › Produktionsaufträge", "orders", planner: true, keywords: "auftrag order produktion planen"),
        Entry("🏭", "Auftragscockpit", "Status, Arbeitskarten und Auftragsfortschritt", "Produktion", "Produktion › Auftragscockpit", "cockpit", planner: true, keywords: "fertigung arbeitskarte job card status"),
        Entry("📈", "Ist-Produktion / OEE", "Ist-Mengen, Ausschuss, Stillstände und OEE", "Produktion", "Produktion › Ist-Produktion / OEE", "actual", planner: true, keywords: "oee ist ausschuss stillstand downtime gutmenge"),
        Entry("📦", "Abgeschlossene Chargen", "Archiv abgeschlossener Produktionschargen", "Produktion", "Produktion › Abgeschlossene Chargen", "batches", keywords: "charge batch archiv abgeschlossen"),
        Entry("📊", "Auswertungen / KPIs", "Kennzahlen und Produktionsauswertungen", "Produktion", "Produktion › Auswertungen / KPIs", "analytics", keywords: "kpi analyse auswertung statistik kennzahl"),

        Entry("🔧", "Einstellungen & Verwaltung", "Stammdaten und Administration zentral öffnen", "Einstellungen", "Einstellungen & Verwaltung", null, planner: true, targetKind: GlobalSearchTargetKind.SettingsDashboard, keywords: "einstellungen verwaltung setup konfiguration"),
        Entry("🏷️", "Artikelstamm", "Artikel und Standardwerte verwalten", "Stammdaten", "Einstellungen › Stammdaten › Artikel", "articles", planner: true, keywords: "artikel produkt stammdaten standardmenge"),
        Entry("👥", "Mitarbeitende & Skills", "Personal, Pensum und Qualifikationen verwalten", "Stammdaten", "Einstellungen › Stammdaten › Mitarbeitende", "employees", planner: true, keywords: "mitarbeiter personal person skills qualifikation pensum"),
        Entry("⚙️", "Arbeitsplätze & Schichten", "Arbeitsplätze und Schichtmodelle verwalten", "Stammdaten", "Einstellungen › Stammdaten › Arbeitsplätze", "workstations", planner: true, keywords: "arbeitsplatz schicht workstation shift"),
        Entry("👤", "Benutzer & Audit", "Benutzerkonten, Rollen und Audit-Protokoll", "Administration", "Einstellungen › Benutzer & Organisation › Benutzer", "users", admin: true, keywords: "benutzer user rolle audit protokoll konto"),

        Setting("🏢", "Unternehmen / Werk", "Firmenname, Firmen-Code und Standort", "Einstellungen › Benutzer & Organisation › Unternehmen", 0, "firma firmenname company standort werk code registrierung"),
        Setting("💾", "Backup & Wiederherstellung", "Sicherungen erstellen und zurückspielen", "Einstellungen › Daten & Sicherheit › Backup", 1, "backup sicherung wiederherstellen restore datenordner automatisch"),
        Setting("🗑️", "Papierkorb", "Gelöschte Datensätze prüfen und wiederherstellen", "Einstellungen › Daten & Sicherheit › Papierkorb", 2, "papierkorb gelöscht löschen restore wiederherstellen recycle"),
        Setting("🔐", "Sicherheit", "Recovery-Code und automatische Sitzungssperre", "Einstellungen › Daten & Sicherheit › Sicherheit", 3, "sicherheit recovery code sitzung sperre autolock passwort"),
        Setting("📤", "Export", "CSV-Export und Exportverzeichnis", "Einstellungen › System › Export", 4, "export csv excel trennzeichen utf8 ordner daten"),
        Setting("🎨", "Benutzeroberfläche", "Persönliche Ansicht und Navigationsoptionen", "Einstellungen › System › Benutzeroberfläche", 5, "oberfläche ui sidebar navigation ansicht zurücksetzen benutzeroberfläche")
    };

    public static IReadOnlyList<GlobalSearchResult> Search(string? query, int maxResults = 10)
    {
        var term = query?.Trim() ?? string.Empty;
        if (term.Length == 0)
            return Array.Empty<GlobalSearchResult>();

        var results = new List<(GlobalSearchResult Result, int Score)>();
        foreach (var item in Catalog)
        {
            if (item.AdminOnly && !SessionService.IsAdministrator)
                continue;
            if (item.PlannerOnly && !SessionService.IsPlannerOrAdmin)
                continue;

            var score = MatchScore(item.Result, term);
            if (score < int.MaxValue)
                results.Add((item.Result, score));
        }

        try
        {
            AddDataResults(results, term);
        }
        catch
        {
            // Navigation/settings search remains available even if local data is temporarily unavailable.
        }

        return results
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Result.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Result.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.Result)
            .DistinctBy(x => (x.TargetKind, x.AreaKey, x.EntityId, x.SettingsTab, x.Title))
            .Take(Math.Max(1, maxResults))
            .ToArray();
    }

    private static void AddDataResults(List<(GlobalSearchResult Result, int Score)> results, string term)
    {
        using var db = new AppDbContext();
        var pattern = $"%{term}%";

        if (SessionService.IsPlannerOrAdmin)
        {
            var employees = db.Employees.AsNoTracking()
                .Where(x => EF.Functions.Like(x.PersonnelNumber, pattern) ||
                            EF.Functions.Like(x.FirstName, pattern) ||
                            EF.Functions.Like(x.LastName, pattern) ||
                            EF.Functions.Like(x.Role, pattern) ||
                            EF.Functions.Like(x.Department, pattern))
                .OrderBy(x => x.PersonnelNumber)
                .Take(5)
                .ToList();

            foreach (var employee in employees)
            {
                var title = $"{employee.FirstName} {employee.LastName}".Trim();
                var subtitle = string.Join(" · ", new[] { employee.PersonnelNumber, employee.Role, employee.Department }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
                AddEntity(results, new GlobalSearchResult(
                    "👥", title, subtitle, "Mitarbeitende",
                    "Einstellungen › Stammdaten › Mitarbeitende & Skills",
                    GlobalSearchTargetKind.Employee, EntityId: employee.Id,
                    SearchKeywords: subtitle), term);
            }

            var articles = db.ArticleMasters.AsNoTracking()
                .Where(x => EF.Functions.Like(x.ArticleNumber, pattern) ||
                            EF.Functions.Like(x.Name, pattern) ||
                            (x.Notes != null && EF.Functions.Like(x.Notes, pattern)))
                .OrderBy(x => x.ArticleNumber)
                .Take(5)
                .ToList();

            foreach (var article in articles)
            {
                var title = string.IsNullOrWhiteSpace(article.ArticleNumber)
                    ? article.Name
                    : $"{article.ArticleNumber} · {article.Name}";
                AddEntity(results, new GlobalSearchResult(
                    "🏷️", title, $"Einheit: {article.Unit}", "Artikel",
                    "Einstellungen › Stammdaten › Artikel",
                    GlobalSearchTargetKind.Area, AreaKey: "articles",
                    SearchKeywords: $"{article.ArticleNumber} {article.Name} {article.Notes}"), term);
            }

            var workstations = db.Workstations.AsNoTracking()
                .Where(x => EF.Functions.Like(x.Name, pattern) || EF.Functions.Like(x.Area, pattern))
                .OrderBy(x => x.Name)
                .Take(5)
                .ToList();

            foreach (var workstation in workstations)
            {
                AddEntity(results, new GlobalSearchResult(
                    "⚙️", workstation.Name,
                    string.IsNullOrWhiteSpace(workstation.Area) ? "Arbeitsplatz" : workstation.Area,
                    "Arbeitsplätze", "Einstellungen › Stammdaten › Arbeitsplätze & Schichten",
                    GlobalSearchTargetKind.Area, AreaKey: "workstations",
                    SearchKeywords: $"{workstation.Name} {workstation.Area}"), term);
            }

            var qualifications = db.Qualifications.AsNoTracking()
                .Where(x => EF.Functions.Like(x.Name, pattern))
                .OrderBy(x => x.Name)
                .Take(4)
                .ToList();

            foreach (var qualification in qualifications)
            {
                AddEntity(results, new GlobalSearchResult(
                    "🎓", qualification.Name, "Qualifikation / Skill", "Skills",
                    "Einstellungen › Stammdaten › Mitarbeitende & Skills",
                    GlobalSearchTargetKind.Area, AreaKey: "employees",
                    SearchKeywords: $"{qualification.Name} skill qualifikation"), term);
            }

            var orders = db.ProductionOrders.AsNoTracking()
                .Where(x => EF.Functions.Like(x.OrderNumber, pattern) ||
                            EF.Functions.Like(x.Product, pattern) ||
                            EF.Functions.Like(x.ArticleNumber, pattern) ||
                            EF.Functions.Like(x.BatchNumber, pattern) ||
                            (x.Description != null && EF.Functions.Like(x.Description, pattern)))
                .OrderByDescending(x => x.PlannedDate)
                .Take(6)
                .ToList();

            foreach (var order in orders)
            {
                var details = new List<string>();
                if (!string.IsNullOrWhiteSpace(order.Product)) details.Add(order.Product);
                if (!string.IsNullOrWhiteSpace(order.BatchNumber)) details.Add($"Charge {order.BatchNumber}");
                if (!string.IsNullOrWhiteSpace(order.Status)) details.Add(order.Status);
                AddEntity(results, new GlobalSearchResult(
                    "📋", order.OrderNumber,
                    string.Join(" · ", details), "Produktionsauftrag",
                    "Produktion › Produktionsaufträge",
                    GlobalSearchTargetKind.ProductionOrder, EntityId: order.Id,
                    SearchKeywords: $"{order.OrderNumber} {order.Product} {order.ArticleNumber} {order.BatchNumber} {order.Description}"), term);
            }
        }
        else
        {
            var completedBatches = db.ProductionOrders.AsNoTracking()
                .Where(x => x.Status == "Abgeschlossen" &&
                            (EF.Functions.Like(x.BatchNumber, pattern) ||
                             EF.Functions.Like(x.OrderNumber, pattern) ||
                             EF.Functions.Like(x.Product, pattern)))
                .OrderByDescending(x => x.CompletedAtUtc)
                .Take(5)
                .ToList();

            foreach (var order in completedBatches)
            {
                AddEntity(results, new GlobalSearchResult(
                    "📦", string.IsNullOrWhiteSpace(order.BatchNumber) ? order.OrderNumber : $"Charge {order.BatchNumber}",
                    $"{order.OrderNumber} · {order.Product}", "Abgeschlossene Charge",
                    "Produktion › Abgeschlossene Chargen",
                    GlobalSearchTargetKind.Batch, EntityId: order.Id,
                    SearchKeywords: $"{order.BatchNumber} {order.OrderNumber} {order.Product}"), term);
            }
        }
    }

    private static void AddEntity(List<(GlobalSearchResult Result, int Score)> results, GlobalSearchResult result, string term)
    {
        var score = MatchScore(result, term);
        if (score < int.MaxValue)
            results.Add((result, score + 1));
    }

    private static int MatchScore(GlobalSearchResult result, string term)
    {
        if (result.Title.Equals(term, StringComparison.CurrentCultureIgnoreCase)) return 0;
        if (result.Title.StartsWith(term, StringComparison.CurrentCultureIgnoreCase)) return 1;
        if (result.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase)) return 2;
        if (result.Subtitle.Contains(term, StringComparison.CurrentCultureIgnoreCase)) return 3;
        if (result.Path.Contains(term, StringComparison.CurrentCultureIgnoreCase)) return 4;
        if (result.SearchKeywords.Contains(term, StringComparison.CurrentCultureIgnoreCase)) return 5;
        return int.MaxValue;
    }

    private static CatalogEntry Entry(
        string icon,
        string title,
        string subtitle,
        string category,
        string path,
        string? areaKey,
        bool planner = false,
        bool admin = false,
        GlobalSearchTargetKind targetKind = GlobalSearchTargetKind.Area,
        string keywords = "") =>
        new(new GlobalSearchResult(icon, title, subtitle, category, path, targetKind, AreaKey: areaKey, SearchKeywords: keywords), planner, admin);

    private static CatalogEntry Setting(string icon, string title, string subtitle, string path, int tab, string keywords) =>
        new(new GlobalSearchResult(icon, title, subtitle, "Einstellungen", path, GlobalSearchTargetKind.SettingsTab, SettingsTab: tab, SearchKeywords: keywords), AdminOnly: true);
}

namespace Produktionsplanung.App.Services;

public enum TourAudience
{
    Everyone,
    PlannerOrAdmin,
    Administrator
}

public sealed record AppTourStep(
    string Key,
    string Icon,
    string Title,
    string Summary,
    string[] Tips,
    string? AreaKey = null,
    string? NavigationButtonName = null,
    TourAudience Audience = TourAudience.Everyone);

public static class AppTourCatalog
{
    public const int CurrentVersion = 1;

    private static readonly IReadOnlyList<AppTourStep> Steps = new[]
    {
        new AppTourStep(
            "welcome", "👋", "Willkommen bei SolutionCompakt",
            "Dieser Rundgang zeigt dir einmal die wichtigsten Bereiche und erklärt, wofür du sie verwendest.",
            new[]
            {
                "Links wechselst du zwischen Planung, Produktion, Stammdaten und Verwaltung.",
                "Oben findest du Zurück, Systemhinweise, persönliche Nachrichten und die Hilfe.",
                "Deine persönliche Ansicht wird pro Benutzer gespeichert."
            }),
        new AppTourStep(
            "navigation", "🧭", "Navigation & Orientierung",
            "Die farbigen Symbole links haben pro Modul immer dieselbe Farbe. Der aktuelle Bereich ist zusätzlich hervorgehoben.",
            new[]
            {
                "Mit ☰ kannst du die Navigation einklappen.",
                "Mit „Zurück“ kommst du zum vorherigen Arbeitsbereich zurück.",
                "Fahre mit der Maus über Symbole und Felder, um zusätzliche Tooltips zu sehen."
            }),
        new AppTourStep(
            "dashboard", "🏠", "Dashboard",
            "Deine Startseite für den schnellen Überblick über Produktion, Planung und offene Themen.",
            new[]
            {
                "Hier erkennst du wichtige Kennzahlen und Warnungen.",
                "Nutze das Dashboard als Einstieg und springe von dort in die Detailbereiche.",
                "Die Glocke oben rechts zeigt automatische Systemhinweise."
            },
            AreaKey: "dashboard", NavigationButtonName: "DashboardButton"),
        new AppTourStep(
            "planning", "📅", "Planung",
            "Hier planst du Mitarbeitende und Produktion in Tages-, Wochen- und Monatsansichten.",
            new[]
            {
                "Mitarbeitende lassen sich in den Planungsansichten per Drag & Drop zuweisen.",
                "Filter helfen dir, nur relevante Mitarbeitende, Aufträge oder Abwesenheiten zu sehen.",
                "Der Wochenplan kann als PDF exportiert und für den Online-Wochenplan vorbereitet werden."
            },
            AreaKey: "planning", NavigationButtonName: "PlanningCalendarButton", Audience: TourAudience.PlannerOrAdmin),
        new AppTourStep(
            "worktime", "🕒", "Arbeitszeit & Betriebskalender",
            "Hier pflegst du Arbeitszeiten und Ausnahmen des Betriebskalenders.",
            new[]
            {
                "Arbeitszeiten bilden die Grundlage für Personal- und Kapazitätsplanung.",
                "Betriebstage und Ausnahmen beeinflussen die Terminierung von Produktionsschichten.",
                "Kontrolliere Feiertage oder Sondertage, bevor grössere Planungen erstellt werden."
            },
            AreaKey: "worktime", NavigationButtonName: "WorkTimeCalendarButton", Audience: TourAudience.PlannerOrAdmin),
        new AppTourStep(
            "orders", "📋", "Produktionsaufträge & Chargen",
            "Produktionsaufträge verbinden Artikel, Charge, Menge, Arbeitsplatz, Schicht und Termin.",
            new[]
            {
                "Neue Chargen werden aus einem Artikel angelegt.",
                "Filter und Suche helfen bei vielen laufenden Aufträgen.",
                "Abgeschlossene Chargen bleiben als nachvollziehbare Historie erhalten."
            },
            AreaKey: "orders", NavigationButtonName: "ProductionOrdersButton", Audience: TourAudience.PlannerOrAdmin),
        new AppTourStep(
            "cockpit", "🏭", "Auftragscockpit",
            "Das Auftragscockpit begleitet einen Auftrag durch Arbeitsgänge, Arbeitskarten und Status.",
            new[]
            {
                "Hier siehst du die operative Bearbeitung eines Produktionsauftrags.",
                "Qualifikationen können bei der Mitarbeiterwahl berücksichtigt werden.",
                "Arbeitskarten und Übergaben unterstützen die Schicht- und Produktionsabwicklung."
            },
            AreaKey: "cockpit", NavigationButtonName: "ManufacturingControlButton", Audience: TourAudience.PlannerOrAdmin),
        new AppTourStep(
            "actual", "📈", "Ist-Produktion / OEE",
            "Hier werden reale Produktionswerte, Gutmenge, Ausschuss und Stillstände erfasst.",
            new[]
            {
                "Soll und Ist bleiben dadurch klar vergleichbar.",
                "Stillstände separat erfassen, damit OEE-Auswertungen nachvollziehbar bleiben.",
                "Bestehende Einträge können über Suche schnell gefunden werden."
            },
            AreaKey: "actual", NavigationButtonName: "ProductionActualButton", Audience: TourAudience.PlannerOrAdmin),
        new AppTourStep(
            "analytics", "📊", "Auswertungen / KPIs",
            "Dieser Bereich verdichtet Produktions- und Planungsdaten zu Kennzahlen.",
            new[]
            {
                "Zeitraum wechseln, um Entwicklungen zu vergleichen.",
                "KPIs sind nur so gut wie die erfassten Ist-Daten.",
                "Nutze den Bereich für Ursachenanalyse und Besprechungen."
            },
            AreaKey: "analytics", NavigationButtonName: "AnalyticsButton"),
        new AppTourStep(
            "articles", "🏷️", "Artikel & Chargen",
            "Artikel sind die Stammbasis für Chargen und Produktionsaufträge.",
            new[]
            {
                "Artikelnummer, Einheit, Standardmenge und Sollrate sauber pflegen.",
                "Excel-Import erleichtert grössere Stammdatenübernahmen.",
                "Die Chargenhistorie zeigt, was mit einem Artikel bereits produziert wurde."
            },
            AreaKey: "articles", NavigationButtonName: "ArticlesButton", Audience: TourAudience.PlannerOrAdmin),
        new AppTourStep(
            "employees", "👥", "Mitarbeitende & Skills",
            "Hier verwaltest du Mitarbeitende, Pensum und Qualifikationen.",
            new[]
            {
                "Skill-Level reichen von 0 bis 5; Level 5 ist Admin.",
                "Qualifikationen beeinflussen passende Mitarbeitervorschläge in der Planung.",
                "Deaktivieren bewahrt historische Bezüge besser als Löschen."
            },
            AreaKey: "employees", NavigationButtonName: "EmployeesButton", Audience: TourAudience.PlannerOrAdmin),
        new AppTourStep(
            "workstations", "⚙️", "Arbeitsplätze & Schichten",
            "Arbeitsplätze, Besetzung und Schichtmodelle definieren die Produktionskapazität.",
            new[]
            {
                "Minimum, Optimal und Maximum der Besetzung plausibel halten.",
                "Pflichtqualifikationen können pro Arbeitsplatz hinterlegt werden.",
                "Arbeitsplatz-Schicht-Freigaben steuern, wann Produktion terminierbar ist."
            },
            AreaKey: "workstations", NavigationButtonName: "WorkstationsButton", Audience: TourAudience.PlannerOrAdmin),
        new AppTourStep(
            "absences", "📆", "Abwesenheiten",
            "Ferien, Krankheit und andere Abwesenheiten werden hier gepflegt und in der Planung berücksichtigt.",
            new[]
            {
                "Zeiträume möglichst früh erfassen.",
                "Abwesenheiten wirken sich auf Verfügbarkeit und Personalvorschläge aus.",
                "Vertrauliche Abwesenheitsdetails werden nicht in den Online-Wochenplan exportiert."
            },
            AreaKey: "absences", NavigationButtonName: "AbsencesButton", Audience: TourAudience.PlannerOrAdmin),
        new AppTourStep(
            "messages", "✉️", "Persönliche Hinweise",
            "Über den Briefumschlag oben rechts sendest du Hinweise direkt an Kolleginnen und Kollegen.",
            new[]
            {
                "Der Empfänger erhält ein Popup und bestätigt den Hinweis ausdrücklich als gelesen.",
                "Posteingang und Gesendet bleiben als Historie gespeichert.",
                "Der Absender sieht, ob und wann der Hinweis bestätigt wurde."
            },
            AreaKey: "messages"),
        new AppTourStep(
            "notifications", "🔔", "Systemhinweise",
            "Die Glocke zeigt automatische Hinweise aus dem System, getrennt von persönlichen Nachrichten.",
            new[]
            {
                "Der Zähler zeigt offene Systemhinweise.",
                "Nutze die Hinweise als schnelle Kontrolle für offene oder auffällige Situationen.",
                "Persönliche Nachrichten findest du daneben beim Briefumschlag."
            },
            AreaKey: "notifications"),
        new AppTourStep(
            "users", "👤", "Benutzer, Rollen & Audit",
            "Administratoren verwalten hier Benutzerkonten, Rollen und die Nachvollziehbarkeit wichtiger Aktionen.",
            new[]
            {
                "Rollen nur so weit vergeben, wie sie wirklich benötigt werden.",
                "Das Audit-Log hilft bei der Nachvollziehbarkeit von Änderungen.",
                "Firmenbenutzer gehören zum aktuellen Firmenmandanten."
            },
            AreaKey: "users", NavigationButtonName: "UserAdminButton", Audience: TourAudience.Administrator),
        new AppTourStep(
            "settings", "🔧", "Einstellungen, Backup & Sicherheit",
            "Hier verwaltest du Firma, Sicherungen, Export, Sicherheit und die persönliche Benutzeroberfläche.",
            new[]
            {
                "Regelmässige Backups sind für die lokale SQLite-Datenbank wichtig.",
                "Online-Wochenplan und Firebase werden bewusst separat aktiviert.",
                "Die App-Einführung kannst du hier jederzeit erneut starten."
            },
            AreaKey: "settings", NavigationButtonName: "SettingsButton", Audience: TourAudience.Administrator),
        new AppTourStep(
            "finish", "✅", "Du kennst jetzt die wichtigsten Bereiche",
            "Nutze das ? oben rechts jederzeit, wenn du die Einführung erneut öffnen möchtest.",
            new[]
            {
                "Kontext-Hinweise unter dem Seitentitel erklären dir den aktuellen Bereich.",
                "Tooltips geben zusätzliche Hinweise direkt an Feldern und Schaltflächen.",
                "Du kannst die Einführung später jederzeit erneut starten."
            })
    };

    public static IReadOnlyList<AppTourStep> GetAvailableSteps() =>
        Steps.Where(IsAvailable).ToArray();

    public static string GetContextHint(string? navigationButtonName)
    {
        if (string.IsNullOrWhiteSpace(navigationButtonName))
            return "SolutionCompakt · Produktionsplanung";

        return Steps.FirstOrDefault(x =>
                   string.Equals(x.NavigationButtonName, navigationButtonName, StringComparison.Ordinal))?.Summary
               ?? "SolutionCompakt · Produktionsplanung";
    }

    public static int FindStepIndex(string? navigationButtonName)
    {
        var available = GetAvailableSteps();
        if (string.IsNullOrWhiteSpace(navigationButtonName))
            return 0;

        var index = available.ToList().FindIndex(x =>
            string.Equals(x.NavigationButtonName, navigationButtonName, StringComparison.Ordinal));
        return index >= 0 ? index : 0;
    }

    private static bool IsAvailable(AppTourStep step) => step.Audience switch
    {
        TourAudience.Administrator => SessionService.IsAdministrator,
        TourAudience.PlannerOrAdmin => SessionService.IsPlannerOrAdmin,
        _ => true
    };
}

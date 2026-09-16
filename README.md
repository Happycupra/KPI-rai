# KPI-rai – Produktionsplanung für Windows

Native Windows-Anwendung für Personal-, Arbeits- und Produktionsplanung auf Basis des definierten Windows-Master-Prompts.

## Ziel

Die Anwendung unterstützt Produktionsleiter und Teamleiter bei:

- Tages- und Wochenplanung
- Mitarbeiter- und Qualifikationsverwaltung
- Arbeitsplätzen / Produktionslinien
- Abwesenheiten und Schichten
- Warnungen bei Unterbesetzung, Doppelbelegung und fehlenden Qualifikationen
- späteren Produktionsaufträgen, KPIs, Exporten und Mehrbenutzerbetrieb

## Technologie

- C# / .NET 8
- WPF
- MVVM
- Entity Framework Core
- SQLite
- CommunityToolkit.Mvvm

## Projektstatus

Dies ist das MVP-Grundgerüst. Enthalten sind bereits:

- native WPF-Anwendung
- linke Navigation
- Dashboard
- SQLite-Datenbank
- Mitarbeiter-, Arbeitsplatz-, Schicht-, Qualifikations-, Abwesenheits- und Planungsmodelle
- Demo-Daten beim ersten Start
- MVVM-/Datenstruktur
- GitHub Actions Build für Windows

## Start lokal

Voraussetzungen:

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 mit Workload **.NET-Desktopentwicklung** oder `dotnet` CLI

```powershell
dotnet restore
dotnet build Produktionsplanung.sln
dotnet run --project src/Produktionsplanung.App/Produktionsplanung.App.csproj
```

## Datenbank

Die lokale SQLite-Datenbank wird unter `%LOCALAPPDATA%\Produktionsplanung\Data\produktionsplanung.db` angelegt.

## MVP-Roadmap

1. Mitarbeiterverwaltung vollständig editierbar machen
2. Skill-Matrix ergänzen
3. Arbeitsplätze und Schichten pflegbar machen
4. Abwesenheiten erfassen
5. Tagesplanung mit Drag & Drop
6. Wochenplanung
7. Warnsystem
8. Backup / Restore
9. Produktionsaufträge
10. PDF-/CSV-Export und KPIs

# OpsCompact – Planen · Produzieren · Verbessern

OpsCompact ist eine native Windows-Anwendung für Personal-, Arbeits- und Produktionsplanung mit integrierten KPIs, OEE, Kalender, Skills und Betriebsdaten.

## Kernfunktionen

- Dashboard mit live berechneten Produktions- und Personal-KPIs
- Outlook-ähnlicher Planungs-Kalender mit Tag-, Woche- und Monatsansicht
- Tages- und Wochenplanung
- automatische Mitarbeitervorschläge und Auto-Besetzung für Produktionsaufträge
- Pflichtqualifikationen je Arbeitsplatz und Skill-Matrix mit Level 0–3
- Mitarbeiter-, Schicht-, Arbeitsplatz- und Abwesenheitsverwaltung
- Betriebskalender mit Feiertagen, Betriebsferien, Sonderarbeitstagen und Sollstunden-Faktoren
- Arbeitszeitkonto mit Soll-, Plan-, Ist- und Saldo-Stunden
- Produktionsaufträge und Personalbedarfs-Abdeckung
- Ist-Produktion, Stillstände, Ausschuss und OEE
- Wochen- und Monatsauswertungen
- Benutzerrollen, Login und Audit-Log
- Backup / Restore und CSV-Komplettexport
- Installer, self-contained Single-EXE und echter USB-/Portable-Modus

## Technologie

- C# / .NET 8
- WPF
- MVVM mit CommunityToolkit.Mvvm
- Entity Framework Core
- SQLite
- GitHub Actions für Build, Tests, Portable-Pakete, Installer und Releases

## Start lokal

Voraussetzungen:

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 mit Workload **.NET-Desktopentwicklung** oder `dotnet` CLI

Das Windows-Icon wird beim Build automatisch aus der versionierten Icon-Quelle erzeugt.

```powershell
dotnet restore
dotnet build Produktionsplanung.sln
dotnet run --project src/Produktionsplanung.App/Produktionsplanung.App.csproj
```

Die erzeugte Anwendung heißt `OpsCompact.exe`.

## Daten und Kompatibilität

Für bestehende Installationen bleibt der bisherige lokale Datenpfad bewusst erhalten:

`%LOCALAPPDATA%\Produktionsplanung\Data\produktionsplanung.db`

Dadurch bleiben vorhandene Daten beim Wechsel auf den Namen OpsCompact erhalten. Im USB-Modus liegen Datenbank, Einstellungen, Backups und Exporte beim Programmordner.

Die Backup-Endung `.kpibackup` bleibt ebenfalls erhalten, damit ältere Backups weiter eingelesen werden können. Neue Backups, Exportordner, Setup-Dateien und Release-Artefakte tragen den Namen **OpsCompact**.

## Release-Artefakte

Ein Windows-Build erzeugt:

- `OpsCompact.exe` als self-contained Single-EXE
- `OpsCompact-USB-Portable-<Version>-win-x64`
- `OpsCompact-Windows-<Version>-win-x64`
- `OpsCompact-Setup-<Version>-win-x64.exe`

Tags nach dem Muster `v0.1.0` erzeugen automatisch ein GitHub Release mit Installer, portablem ZIP, Single-EXE und USB-Paket.

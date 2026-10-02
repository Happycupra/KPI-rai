# SolutionCompakt – Planen · Organisieren · Voranbringen

**SolutionCompakt** ist eine native Windows-Anwendung für Personal-, Arbeits- und Produktionsplanung. Sie verbindet Einsatzplanung, Qualifikationen, Produktionsaufträge, Fertigungssteuerung, Chargen, Ist-Produktion, OEE, Auswertungen und betriebliche Stammdaten.

> **Aktueller Entwicklungsstand: 02.10.2026**
>
> Plattform: **Windows 10/11 · .NET 8 · WPF**
> Datenbetrieb: **lokal/offline mit SQLite** oder optional **zentraler Mehrbenutzerbetrieb mit PostgreSQL + SignalR**

## Zentraler Mehrbenutzerbetrieb

SolutionCompakt besitzt zusätzlich zum bisherigen lokalen SQLite-Modus einen optionalen zentralen Betriebsmodus:

- gemeinsame PostgreSQL-Datenbank für mehrere PCs derselben Firma
- automatische einmalige Übernahme einer bestehenden lokalen SQLite-Installation
- firmenbezogenes PostgreSQL-Schema anhand der stabilen `CompanyId`
- bestehende Benutzer, Planung, Aufträge, Chargen, Ist-Daten, OEE, Audit, Schichtübergaben und persönliche Nachrichten werden gemeinsam genutzt
- Echtzeit-Aktualisierung über `SolutionCompakt.Server` und SignalR
- Serverzugang nur mit aktiver SolutionCompakt-Lizenz und bestätigtem zentralen Benutzer
- Optimistic Concurrency verhindert stilles Überschreiben paralleler Änderungen
- weitere PCs können einer bestehenden zentralen Firma über dieselbe `CompanyId`/`CompanyCode` beitreten
- lokaler SQLite-Betrieb bleibt vollständig erhalten und ist weiterhin der Standard

Die vollständige Einrichtung und Testmatrix stehen in [`docs/CENTRAL-SERVER.md`](docs/CENTRAL-SERVER.md).

## Funktionsumfang

### Dashboard & Hinweise
- Dashboard mit live berechneten Produktions- und Personal-KPIs
- zentrale Hinweise für relevante Planungs- und Betriebsprobleme
- Navigation aus Hinweisen in die betroffenen Bereiche
- Anzeige des letzten erfolgreichen lokalen Backups im SQLite-Betrieb

### Interne Hinweise & Lesebestätigung
- persönliche Hinweise zwischen SolutionCompakt-Benutzern
- Briefumschlag-Symbol oben rechts mit Zähler für ungelesene Hinweise
- Popup bei neuen Hinweisen
- explizite Aktion **Gelesen bestätigen**
- Posteingang und Gesendet-Historie
- Absender sieht, ob und wann der Empfänger den Hinweis bestätigt hat
- Priorität Normal / Wichtig
- vollständige Speicherung mit Absender-/Empfänger-Snapshot, Betreff, Inhalt und Zeitstempeln
- Audit-Einbindung für Senden und Lesebestätigung
- im Zentralbetrieb PC-übergreifend über die gemeinsame PostgreSQL-Datenbank und SignalR-Aktualisierung

### Firmenregistrierung & Mandantenfähigkeit
- stabile technische `CompanyId` pro Firma
- lesbarer `CompanyCode`
- bestehende Installationen werden rückwärtskompatibel migriert
- Firebase-Lizenz- und Online-Wochenplan-Daten bleiben unter `companies/{companyId}/...` getrennt
- zentraler Datenbetrieb verwendet ein separates PostgreSQL-Schema `company_<CompanyId>`

### Online-Wochenplan
- veröffentlichbarer, versionierter Wochenplan-Snapshot als JSON
- Personaleinsätze und Produktionsschichten
- direkte Veröffentlichung aus der Wochenplanung
- Firebase-basierte Online-Anmeldung und rollenabhängiger Zugriff
- Online-Korrekturen bleiben vom Desktop-Snapshot getrennt

### Personal- & Einsatzplanung
- Outlook-ähnlicher Planungskalender mit Tag-, Woche- und Monatsansicht
- Tages- und Wochenplanung
- automatische Mitarbeitervorschläge
- Auto-Besetzung für fehlende Personalpositionen
- Prüfung von Abwesenheiten, Überschneidungen, Schichten und Qualifikationen
- Mindest-, Optimal- und Maximalbesetzung je Arbeitsplatz
- Soll-, Plan-, Ist- und Saldo-Stunden
- Betriebskalender
- PDF-Export

### Mitarbeiter & Qualifikationen
- Mitarbeiterverwaltung
- frei definierbare Qualifikationen
- Skill-Matrix mit Level 0–5
- Pflichtqualifikationen je Arbeitsplatz
- qualifikationsbasierte Mitarbeitervorschläge

### Arbeitsplätze & Schichten
- Arbeitsplätze und Produktionslinien
- Mindest-, optimale und maximale Personalstärke
- Schichtverwaltung
- zulässige Schichten je Arbeitsplatz und Datum
- Schicht- und Besetzungskonflikte

### Produktionsaufträge & Fertigungssteuerung
- Produktionsaufträge mit Produkt, Menge, Termin, Priorität, Status und Personalbedarf
- Auftragscockpit
- Arbeitsgänge und Arbeitskarten
- Fertigungsrouten und Arbeitsfolgen
- Mitarbeiterzuweisung
- Funktion **Beste Wahl**
- Start-, Pause- und Abschlussinformationen

### Artikel & Chargen
- Artikelverwaltung
- Chargenerstellung
- Chargendetails und Chargenhistorie
- Archiv abgeschlossener Chargen
- Arbeitsgänge, Ist-Erfassungen und Stillstände
- Chargenvergleich
- PDF-Chargenbericht

### Ist-Produktion & OEE
- Gesamt-, Gut- und Ausschussmenge
- geplante Produktionszeit und Laufzeit
- Stillstandsgründe und Stillstandsdauer
- Verfügbarkeit, Leistung, Qualität und OEE
- Wochen- und Monatsauswertungen

### What-if-Planung
Nachvollziehbare Planungssimulationen sind als eigener Bereich in der Anwendung verfügbar.

- read-only Simulation von Personalengpässen
- Vergleich geplanter Besetzung mit Zielbesetzung
- Ermittlung qualifizierter und konfliktfreier Alternativen
- verständliche Begründung und Auswirkung je Vorschlag
- Simulation verändert keine Produktions- oder Planungsdaten
- einzelne Übernahme eines Vorschlags erst nach erneuter Prüfung von Abwesenheit, Überschneidung, Qualifikation, Schichtfreigabe und vorhandener Zuweisung
- semantischer Audit-Eintrag für jede übernommene Alternative

**Status:** Service und Bedienoberfläche sind integriert.

### Digitale Schichtübergabe
Die Daten- und Servicebasis für eine strukturierte Schichtübergabe ist implementiert.

- Übergabe von Schicht zu Schicht
- optionale Zuordnung zu Arbeitsplatz und Produktionsauftrag
- Prioritäten
- Betreff und Detailinformation
- Status Offen / Bestätigt / Erledigt
- Bestätigung mit Benutzer und Zeitstempel
- Abschluss mit Lösung und Zeitstempel
- Einbindung in das bestehende Audit-System
- Filter nach offen/heute/kritisch sowie Arbeitsplatz und Schicht
- offene kritische Übergaben in der Dashboard-Konfliktzentrale

**Status:** Datenmodell, Workflow-Service und Bedienoberfläche sind integriert.

## Sicherheit & Nachvollziehbarkeit
- Benutzeranmeldung
- Rollen und Berechtigungen
- Administrator-, Planer- und eingeschränkte Zugriffe
- Audit-Log
- automatische Sitzungssperre
- Recovery-Code
- Warnung bei ungespeicherten Änderungen
- Startup-Health-Check
- SQLite-Integritätsprüfung im lokalen Modus
- zentrale Lizenz- und Benutzerprüfung für SignalR-Zugriff
- Optimistic Concurrency im PostgreSQL-Modus

## Datensicherung & Export

### Lokaler SQLite-Modus
- manuelles Backup und Restore
- automatische Backups
- `.kpibackup`
- konfigurierbare Aufbewahrung

### Zentraler PostgreSQL-Modus
- PostgreSQL ist die führende Datenbank
- lokale `.kpibackup`-Erstellung und -Wiederherstellung sind bewusst gesperrt, damit keine veraltete lokale Sicherung fälschlich als vollständiges Backup gilt
- PostgreSQL muss serverseitig gesichert werden, z. B. per `pg_dump`

## Technologie
- **C# / .NET 8**
- **WPF**
- **MVVM** mit CommunityToolkit.Mvvm
- **Entity Framework Core 8**
- **SQLite** für lokalen Betrieb
- **PostgreSQL / Npgsql** für zentralen Betrieb
- **ASP.NET Core + SignalR** für Authentifizierung und Echtzeit
- **Docker Compose** für PostgreSQL und `SolutionCompakt.Server`
- **PDFsharp-WPF**
- GitHub Actions für Build, Regressionstests, Publishing, Portable-Pakete, Installer und Releases

## Architektur

### Lokal

```text
SolutionCompakt WPF
        |
      SQLite
```

### Zentral

```text
PC A ----\
          +---- PostgreSQL
PC B ----/       gemeinsame operative Daten
  |                  ^
  +---- SignalR -----+
       SolutionCompakt.Server
```

Der Zentralmodus ist optional. Ohne aktivierte `central-mode.json` arbeitet SolutionCompakt weiterhin wie bisher lokal mit SQLite.

## Installation & Entwicklung

```powershell
dotnet restore Produktionsplanung.sln
dotnet build Produktionsplanung.sln
dotnet run --project src/Produktionsplanung.App/Produktionsplanung.App.csproj
```

Für den Zentralbetrieb siehe `docs/CENTRAL-SERVER.md`.

Die erzeugte Anwendung heißt `SolutionCompakt.exe`.

### Datenpfad
Für bestehende Installationen bleibt der bisherige lokale Datenpfad erhalten:

```text
%LOCALAPPDATA%\Produktionsplanung\Data\produktionsplanung.db
```

Dadurch bleiben vorhandene Daten auch nach der Umbenennung von OpsCompact auf SolutionCompakt erhalten.

Im USB-/Portable-Modus befinden sich Datenbank, Einstellungen, Backups und Exporte beim Programmordner. Die Backup-Endung `.kpibackup` bleibt aus Kompatibilitätsgründen erhalten.

## Build & Release
Der Windows-Build prüft den Quellcode und führt die vorhandenen Regressionstests aus. Erfolgreiche Builds können folgende Artefakte erzeugen:

- `SolutionCompakt.exe` als self-contained Single-EXE
- `SolutionCompakt-USB-Portable-<Version>-win-x64`
- `SolutionCompakt-Windows-<Version>-win-x64`
- `SolutionCompakt-Setup-<Version>-win-x64.exe`

Tags nach dem Muster `v0.1.0` erzeugen automatisch ein GitHub Release mit Installer, portablem ZIP, Single-EXE und USB-Paket.

## Aktueller Entwicklungsstand

**Produktiv bzw. in der Bedienoberfläche integriert:** Personal- und Schichtplanung, Mitarbeiter/Skills, Abwesenheiten, Betriebskalender, Produktionsaufträge, Fertigungssteuerung, Artikel/Chargen, Ist-Produktion/OEE, KPI-Auswertungen, interne Hinweise mit Popup und Lesebestätigung, Benutzer/Audit, Backup/Restore, CSV- und PDF-Export sowie Portable-Betrieb.

**In der Bedienoberfläche integriert:** What-if-/Neuplanung und digitale Schichtübergabe einschließlich Dashboard-Hinweisen.

**Optional verfügbar:** zentraler Mehrbenutzerbetrieb mit PostgreSQL, SignalR und PC-übergreifenden Nachrichten.

**Noch geplant:** QR-/Barcode-Shopfloor, Qualitätsprüfungen, Wartung/Maschinenzustände, ERP/API-Anbindung und weitergehende automatische Neuplanung.

## Entwicklungsprinzipien
- automatische Vorschläge verändern keine Produktionsdaten ohne explizite Benutzeraktion
- Qualifikationen, Abwesenheiten und Planungskonflikte werden geprüft
- bestehende lokale Daten und Backups bleiben über Updates kompatibel
- wichtige Änderungen sind auditierbar
- Offline-Fähigkeit des lokalen Modus bleibt erhalten
- zentrale Änderungen dürfen parallele Benutzeränderungen nicht still überschreiben

---

**SolutionCompakt**  
*Planen · Organisieren · Voranbringen – Einfach effizienter.*

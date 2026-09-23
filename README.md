# SolutionCompakt – Planen · Organisieren · Voranbringen

**SolutionCompakt** ist eine native Windows-Anwendung für Personal-, Arbeits- und Produktionsplanung. Sie verbindet Einsatzplanung, Qualifikationen, Produktionsaufträge, Fertigungssteuerung, Chargen, Ist-Produktion, OEE, Auswertungen und betriebliche Stammdaten in einer lokal nutzbaren Desktop-Anwendung.

> **Aktueller Entwicklungsstand: 22.09.2026**  
> Produktversion im Projekt: **0.1.0** · Plattform: **Windows 10/11 · .NET 8 · WPF · SQLite**

## Funktionsumfang

### Dashboard & Hinweise
- Dashboard mit live berechneten Produktions- und Personal-KPIs
- zentrale Hinweise für relevante Planungs- und Betriebsprobleme
- Navigation aus Hinweisen in die betroffenen Bereiche
- Anzeige des letzten erfolgreichen Backups

### Personal- & Einsatzplanung
- Outlook-ähnlicher Planungskalender mit Tag-, Woche- und Monatsansicht
- Tages- und Wochenplanung
- automatische Mitarbeitervorschläge
- Auto-Besetzung für fehlende Personalpositionen bei Produktionsaufträgen
- Prüfung von Abwesenheiten, Überschneidungen, Schichten und Qualifikationen
- Mindest-, Optimal- und Maximalbesetzung je Arbeitsplatz
- Warnungen bei Unter- und Überbesetzung
- Soll-, Plan-, Ist- und Saldo-Stunden
- Mitarbeiter-Schnellansicht aus der Planung
- Betriebskalender mit Feiertagen, Betriebsferien, Sonderarbeitstagen und Sollstunden-Faktoren
- PDF-Export des Planungskalenders und Wochenplans

### Mitarbeiter & Qualifikationen
- Mitarbeiterverwaltung
- frei definierbare Qualifikationen
- Skill-Matrix mit Level 0–5 (Level 5 = Admin)
- Pflichtqualifikationen je Arbeitsplatz
- qualifikationsbasierte Mitarbeitervorschläge
- Berücksichtigung der Qualifikation bei der Fertigungssteuerung

### Arbeitsplätze & Schichten
- Arbeitsplätze und Produktionslinien
- Mindest-, optimale und maximale Personalstärke
- Schichtverwaltung
- zulässige Schichten je Arbeitsplatz und Datum
- Schicht- und Besetzungskonflikte in der Planung

### Produktionsaufträge & Fertigungssteuerung
- Produktionsaufträge mit Produkt, Menge, Termin, Priorität, Status und Personalbedarf
- Auftragscockpit für die operative Fertigungssteuerung
- Arbeitsgänge und Arbeitskarten
- Fertigungsrouten und Arbeitsfolgen
- Mitarbeiterzuweisung zu Arbeitskarten
- Funktion **Beste Wahl** zur Auswahl geeigneter verfügbarer Mitarbeiter
- Start-, Pause- und Abschlussinformationen der Fertigung
- Plan-/Ist-Verknüpfung zwischen Auftrag, Personal und Produktion

### Artikel & Chargen
- Artikelverwaltung
- Chargenerstellung aus Artikeln
- Chargendetails und Chargenhistorie
- Archiv abgeschlossener Chargen
- Arbeitsgänge, Ist-Erfassungen und Stillstände je Charge
- Chargenvergleich
- PDF-Chargenbericht mit Produktionszeitraum, Mengen, Kennzahlen, Arbeitsgängen, Stillständen und Bemerkungen

### Ist-Produktion & OEE
- Erfassung von Gesamt-, Gut- und Ausschussmenge
- geplante Produktionszeit und Laufzeit
- Stillstandsgründe und Stillstandsdauer
- Berechnung von Verfügbarkeit, Leistung und Qualität
- OEE-Berechnung
- Zuordnung zu Produktionsauftrag und Produktionsschicht
- Wochen- und Monatsauswertungen

### What-if-Planung
Die technische Grundlage für nachvollziehbare Planungssimulationen ist vorhanden.

- read-only Simulation von Personalengpässen
- Vergleich geplanter Besetzung mit Zielbesetzung
- Ermittlung qualifizierter und konfliktfreier Alternativen
- verständliche Begründung und Auswirkung je Vorschlag
- Simulation verändert keine Produktions- oder Planungsdaten

**Status:** Backend/Service vorhanden. Eine vollständige Bedienoberfläche für What-if-Szenarien ist noch in Entwicklung.

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

**Status:** Datenmodell und Workflow-Service vorhanden. Die sichtbare Schichtübergabe-Oberfläche ist noch in Entwicklung.

## Sicherheit & Nachvollziehbarkeit
- Benutzeranmeldung
- Rollen und Berechtigungen
- Administrator-, Planer- und eingeschränkte Zugriffe
- Audit-Log für relevante Datenänderungen
- automatische Sitzungssperre nach konfigurierbarer Inaktivität
- Recovery-Code für die lokale Passwortwiederherstellung
- Warnung bei ungespeicherten Änderungen mit Speichern / Verwerfen / Abbrechen
- Startup-Health-Check für Speicher, Schreibzugriff, Datenbankintegrität und Einzelinstanz
- SQLite-Integritätsprüfung beim Start

## Datensicherung & Export
- manuelles Backup und Restore
- automatische Backups
- konfigurierbare Aufbewahrung automatischer Backups
- Datenbank-Snapshot für konsistente Sicherungen
- Backup-Format `.kpibackup`
- CSV-Komplettexport der betrieblichen Daten
- PDF-Exporte für Planung und Chargenberichte
- lokaler und USB-/Portable-Speichermodus

## Benutzeroberfläche
- rollenabhängige Navigation
- einklappbare Seitenleiste und Navigationsgruppen
- benutzerspezifische UI-Einstellungen
- Navigationshistorie mit Zurück-Funktion
- Hinweis-/Benachrichtigungsbereich
- Deep Links zwischen Aufträgen, Fertigungssteuerung, Ist-Produktion, Mitarbeitern und Chargen

## Technologie
- **C# / .NET 8**
- **WPF**
- **MVVM** mit CommunityToolkit.Mvvm
- **Entity Framework Core 8**
- **SQLite**
- **PDFsharp-WPF**
- GitHub Actions für Build, Regressionstests, Publishing, Portable-Pakete, Installer und Releases

## Architektur
SolutionCompakt ist aktuell als lokale, offline-fähige Windows-Anwendung ausgelegt. Die Produktionsdaten werden in SQLite gespeichert. Das Datenbankschema wird beim Start rückwärtskompatibel ergänzt, sodass bestehende Installationen weiterverwendet werden können.

Die bestehende Architektur eignet sich für Einzelplatz-, Notebook- und USB-/Portable-Betrieb. Zentraler Netzwerk-/Mehrbenutzerbetrieb und eine Server-Datenbank sind noch keine Bestandteile des aktuellen Produktstands.

## Installation & Betrieb

### Lokale Entwicklung
Voraussetzungen:
- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 mit Workload **.NET-Desktopentwicklung** oder `dotnet` CLI

```powershell
dotnet restore Produktionsplanung.sln
dotnet build Produktionsplanung.sln
dotnet run --project src/Produktionsplanung.App/Produktionsplanung.App.csproj
```

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

**Produktiv bzw. in der Bedienoberfläche integriert:** Personal- und Schichtplanung, Mitarbeiter/Skills, Abwesenheiten, Betriebskalender, Produktionsaufträge, Fertigungssteuerung, Artikel/Chargen, Ist-Produktion/OEE, KPI-Auswertungen, Benutzer/Audit, Backup/Restore, CSV- und PDF-Export sowie Portable-Betrieb.

**Technische Grundlage vorhanden, UI noch ausstehend:** What-if-/Neuplanung und digitale Schichtübergabe.

**Noch geplant:** QR-/Barcode-Shopfloor, Qualitätsprüfungen, Wartung/Maschinenzustände, ERP/API-Anbindung, zentraler Mehrbenutzerbetrieb und weitergehende automatische Neuplanung.

## Entwicklungsprinzipien
- automatische Vorschläge verändern keine Produktionsdaten ohne explizite Benutzeraktion
- Qualifikationen, Abwesenheiten und Planungskonflikte werden vor Zuweisungen geprüft
- bestehende Daten und Backups sollen über Updates hinweg kompatibel bleiben
- wichtige Änderungen sind nachvollziehbar und auditierbar
- Offline-Fähigkeit bleibt ein Kernziel

---

**SolutionCompakt**  
*Planen · Organisieren · Voranbringen – Einfach effizienter.*

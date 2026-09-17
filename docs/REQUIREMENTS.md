# Anforderungen – SolutionCompakt Produktionsplanung

**Produktname:** SolutionCompakt  
**Branding:** SolutionCompakt-Logo und Windows-App-Icon werden als eingebettete, für die Windows-Anwendung validierte Ressourcen ausgeliefert.

Diese Datei fasst die Zielarchitektur und den geplanten Funktionsumfang der Windows-Anwendung zusammen.

## Zielplattform

- Windows 10/11
- native Desktop-Anwendung
- C# / .NET 8 / WPF
- MVVM
- Entity Framework Core
- SQLite im MVP
- spätere Migration auf SQL Server/PostgreSQL möglich
- Offline-Kernfunktionen

## Kernbereiche

1. Dashboard
2. Tagesplanung
3. Wochenplanung
4. Mitarbeiter
5. Arbeitsplätze / Produktionslinien
6. Produktionsaufträge
7. Abwesenheiten
8. Qualifikationen / Skill-Matrix
9. Arbeitszeiten
10. Auswertungen / KPIs
11. Backup
12. Einstellungen

## Mitarbeiter

Geplante Stammdaten:

- Personalnummer
- Vorname / Nachname
- Funktion
- Abteilung
- Pensum
- Eintrittsdatum
- Standardschicht
- Sollstunden
- Aktiv/Inaktiv
- optionale Kontakt- und Notizfelder

## Qualifikationen

Qualifikationen sind frei konfigurierbar. Vorgesehenes Level-System:

- 0 = keine Qualifikation
- 1 = in Ausbildung
- 2 = qualifiziert
- 3 = Experte / Trainer

Die Skill-Matrix soll Mitarbeiter und Qualifikationen tabellarisch und filterbar darstellen.

## Arbeitsplätze

Pro Arbeitsplatz / Produktionslinie:

- Name und Bereich
- Mindestbesetzung
- optimale Besetzung
- maximale Besetzung
- benötigte Qualifikationen
- Aktiv/Inaktiv
- Standardschicht

## Tagesplanung

Zentrale Funktionen:

- Datum wählen
- Mitarbeiter einem Arbeitsplatz zuweisen
- Schicht und Arbeitszeit berücksichtigen
- Drag & Drop, sofern stabil umsetzbar
- Mitarbeiter-Pool mit verfügbar / eingeplant / abwesend
- Planung kopieren
- Drucken / PDF-Export später

## Wochenplanung

- Montag–Freitag standardmäßig
- optional Montag–Sonntag
- Ansicht nach Mitarbeitern
- alternative Ansicht nach Arbeitsplätzen
- Vorwoche kopieren
- einzelne Tage kopieren

## Warnsystem

Automatisch erkennen:

- Doppelbelegung
- Abwesenheit
- fehlende Qualifikation
- Unterbesetzung
- Überbesetzung
- Schichtkonflikte
- Überstunden

## Abwesenheiten

Unterstützte Typen:

- Ferien
- Krankheit
- Unfall
- Weiterbildung
- Militär / Zivildienst
- unbezahlter Urlaub
- sonstige Abwesenheit

## Produktionsaufträge

Spätere MVP-Erweiterung mit:

- Auftragsnummer
- Produkt
- Menge / Einheit
- Priorität
- geplante und tatsächliche Zeiten
- Produktionslinie
- Personalbedarf
- Status
- Kommentar

Statuswerte: geplant, bereit, läuft, pausiert, abgeschlossen, Problem.

## KPIs

Geplant sind unter anderem:

- verfügbare / abwesende Mitarbeiter
- Ferien- und Krankheitsquote
- geplante Stunden / Überstunden
- Personalabdeckung
- offene / laufende / abgeschlossene Aufträge
- verspätete Aufträge
- Linienauslastung
- unterbesetzte Arbeitsplätze
- Qualifikationskonflikte
- Doppelbelegungen

## Daten und Sicherheit

- lokale SQLite-Datenbank unter `%LOCALAPPDATA%\Produktionsplanung\Data\`
- eindeutige Personalnummern
- Backups und Restore
- später Benutzerrollen und Audit Trail
- Passwörter nie im Klartext
- technische Logs ohne unnötige vertrauliche Daten

## Roadmap

### Version 1

- Windows-Grundlayout
- SQLite
- Mitarbeiterverwaltung
- Arbeitsplätze
- Qualifikationen / Skill-Matrix
- Schichten
- Abwesenheiten
- Tagesplanung
- Wochenplanung
- Speicherung
- Backup

### Version 1.1

- Produktionsaufträge
- Personalbedarf
- Warnsystem
- Qualifikationskontrolle
- Soll-/Ist-Stunden
- PDF-Export
- CSV/XLSX-Export

### Version 1.2

- KPI-Dashboard
- Benutzerrollen
- Audit Trail
- Netzwerkbetrieb
- zentrale SQL-Datenbank

## Spätere Erweiterungen

- Maschinenplanung
- Wartung und Störungen
- OEE
- ERP-Anbindung
- Barcode / QR-Code
- automatische Personalplanung
- Schichtübergabe

## Definition of Done für MVP

Version 1 gilt als nutzbar, wenn:

- Anwendung startet stabil
- Mitarbeiter gespeichert werden
- Arbeitsplätze gespeichert werden
- Qualifikationen funktionieren
- Abwesenheiten funktionieren
- Tages- und Wochenplanung funktionieren
- Daten nach Neustart erhalten bleiben
- Warnungen funktionieren
- Backup funktioniert
- keine kritischen Fehler vorhanden sind

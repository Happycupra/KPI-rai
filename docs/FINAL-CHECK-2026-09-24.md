# KPI-rai / SolutionCompakt – Abschlussprüfung 24.09.2026

**Ergebnis: noch keine vollständige Produktionsfreigabe.** Der geprüfte Windows-Build ist grün; reale Windows-Bedienung, Installation und produktiver Firebase-Betrieb sind hier nicht abschliessend bestätigt.

## Prüfbasis und Grenzen

- Repository-Basis: `642dee8718f4ce23a09da9f18f1f4f8c11eacd79` (main, inklusive automatisch aktualisierter Downloads).
- Letzter Windows-Lauf für den Anwendungscode `871bfd778ed74a6b6606509bc2ff68713981ba0b`: [Build 36004309162](https://github.com/Happycupra/KPI-rai/actions/runs/36004309162). Build, 50/50 Regressionstests, Publishing und Installer-Erstellung erfolgreich; beide Signaturschritte übersprungen.
- Lokale Umgebung: Linux ohne .NET/WPF-Runtime. Kein interaktiver Windows-Test, keine vollständige Pixel-/DPI-Abnahme, keine echte Anmeldung an Firebase und kein Test mit produktiven Kundendaten.
- Quellcodeprüfung von Navigation, gemeinsamen Steuerelementen, Web-Wochenplan, Online-Publishing, Authentifizierung, Backup/Restore, OEE und CI/Installer. Diese Prüfung beweist nicht die Fehlerfreiheit sämtlicher Geschäftsabläufe.

## Konkrete Korrekturen in diesem PR

| Bereich | Befund | Änderung / Nachweis |
|---|---|---|
| Release | `.github/workflows/release.yml` enthielt abgebrochene PowerShell-Signaturbefehle, doppelte YAML-Schlüssel und mehrfach eingefügte Release-Blöcke. | Workflow aus intakten Schritten rekonstruiert; gemeinsames Signaturskript; Secrets über `env` geprüft; YAML lokal inklusive doppelter Schlüssel geprüft. Tag-Release selbst noch nicht ausgeführt. |
| Build | Alle PRs und main teilten eine einzige Concurrency-Gruppe mit Abbruch älterer Läufe. | Gruppierung nach PR bzw. Ref verhindert, dass ein fremder PR den main-Build abbricht. |
| Online-Woche | Leere historische Wochen orientierten ihre Tagesüberschriften am heutigen Datum. | Datum kommt aus `weekStart` des gewählten Snapshots; Regressionstest. |
| Online-Produktion | Produktionsschichten wurden exportiert und gespeichert, aber vom Browser weder geladen noch angezeigt. | Produktionskarten mit Auftrag, Produkt, Arbeitsplatz, Schicht, Zeit und Personalbedarf; Regressionstest. |
| Aktualisieren | Ohne bereits vorhandene Woche konnte Aktualisieren keinen später veröffentlichten Erstplan finden. | Wochenliste wird neu geladen, bisherige Auswahl nach Möglichkeit beibehalten; Regressionstest. |
| Fehler / Sitzung | Laden und Korrekturspeichern hatten unbehandelte Fehler; alte Anzeigen/Sitzungswerte blieben erhalten. | Sichtbare Fehlermeldungen, Erhalt der Korrektureingaben, Bereinigung beim Abmelden und Schutz vor verspäteten Ladeantworten; Regressionstests. |
| Design | Destruktive WPF-Buttons erbten einen hellblauen Hover-Hintergrund bei weiterhin weisser Schrift. | Eigenes rotes Hover-/Pressed-Template erhält den Kontrast. Windows-Sichtprüfung noch erforderlich. |
| Bedienbarkeit | Online-Karten und Upload-Bereich konnten bei langen Texten/kleinen Breiten überlaufen. | Textumbruch, flexible Upload-Breite, sichtbarer Tastaturfokus und scrollbarer Dialog. Kein vollständiger Browser-Sichttest. |
| Wartbarkeit | Ungenutztes `bypassUnsavedChangesPrompt` erzeugte Compilerwarnungen. | Feld entfernt; vorhandene Speichern/Verwerfen/Abbrechen-Prüfung bleibt aktiv. |

## Noch offene Punkte – vor Freigabe bearbeiten

### Hoch: Firebase-Veröffentlichung blockiert

[Deploy-Lauf 36004309595](https://github.com/Happycupra/KPI-rai/actions/runs/36004309595), Schritt „Deploy Hosting, Firestore Rules and Functions“:

`HTTP Error: 403, Permission denied to get service [firestore.googleapis.com]`

Die Credential-Datei wurde laut vorherigem Schritt erstellt. Der verwendete Dienstzugang konnte den Firestore-Dienststatus im Projekt `solution-compact` nicht abfragen. Google-Cloud-Projektzuordnung und IAM-Berechtigungen dieses Dienstkontos müssen geprüft werden; aus dem Log lässt sich keine vollständige Liste aller darüber hinaus benötigten Rechte ableiten. Keine Rechte oder Secrets wurden geändert.

### Hoch: Online-Publishing ist nicht atomar

Quelle: `online-weekplan/functions/index.js`, `publishWeekPlan` / `replaceCollection`.

Metadaten werden zuerst gespeichert, danach Personaleinsätze und Produktionsschichten separat gelöscht/ersetzt, bei Bedarf in mehreren Batches. Fehler im zweiten Teil oder parallele Veröffentlichungen können einen unvollständigen bzw. gemischten Wochenstand erzeugen. Einzelne IDs werden zudem erst nach dem Schreiben der Metadaten geprüft. Vor produktiver Nutzung: Paket vollständig validieren, neuen Snapshot als separate Version schreiben und erst nach erfolgreichem Abschluss den aktiven Versionszeiger umschalten. Dazu Emulator-Tests für Fehler mitten im Vorgang und parallele Publikationen ergänzen.

### Hoch: Wiederherstellung ohne vollständigen Rollback

Quelle: `src/Produktionsplanung.App/Services/BackupService.cs`, `RestoreBackup` / `ValidateDatabase`.

Die Zieldatenbank wird direkt per `File.Copy(..., true)` überschrieben; Einstellungen folgen separat. Bei I/O-Fehlern ist kein gemeinsamer Rollback implementiert. Die Validierung prüft zwei Tabellennamen, jedoch nicht `PRAGMA integrity_check` und vollständige Schema-/Versionsverträglichkeit. Vor Freigabe: temporär validieren, Vorzustand sichern, Austausch/Fehlerbehandlung für DB und Einstellungen gemeinsam absichern; Tests mit beschädigtem Backup und simuliertem Schreibfehler.

Zusätzlich löscht `CreateBackup` eine vorhandene Zieldatei vor dem Erstellen des neuen ZIPs. Bei Fehlschlag kann das bisherige Backup verloren gehen. Erst temporär schreiben, danach ersetzen.

### Mittel: Schutz ungespeicherter Änderungen unvollständig

Quelle: `MainWindow.xaml.cs`, `CanLeaveCurrentContent`; `Views/*View.xaml.cs`.

Die Navigationsabfrage greift nur für Ansichten mit `IUnsavedChangesAware`. Implementiert ist dies für Mitarbeiter, Produktionsaufträge und Einstellungen. Andere editierbare Bereiche, etwa Abwesenheiten und Ist-Produktion, müssen hinsichtlich Eingabeverlust beim Wechseln geprüft und bei Bedarf eingebunden werden. Die allgemeine README-Aussage ist deshalb weiter gefasst als die nachgewiesene Abdeckung.

### Mittel: Online-Authentifizierung benötigt zusätzliche Abnahme

Quelle: `online-weekplan/functions/index.js`, `login`; `online-weekplan/firestore.rules`.

Im Login-Handler ist keine eigene Begrenzung wiederholter Fehlanmeldungen implementiert. Ob externe Schutzmassnahmen aktiv sind, wurde nicht verifiziert. Rollen/Firmen-Claims sowie deaktivierte Benutzer, Passwortänderungen und erneuerte Tokens müssen im echten Firebase-/Emulatorbetrieb getestet werden. Aus der lokalen Passwortänderung lässt sich kein automatischer Cloud-Sync nachweisen. Die README beschreibt den Erst-Sync ausdrücklich als noch ausstehend.

### Mittel: Funktionsumfang noch nicht vollständig sichtbar

README und Quellstruktur kennzeichnen What-if-Planung und digitale Schichtübergabe als Service-/Datenbasis ohne vollständige Bedienoberfläche. Diese Funktionen dürfen bei einer Abnahme nicht als vollständig bedienbar gelten.

### Offen: Windows- und Designabnahme

- Alle Hauptbereiche mit Administrator, Planer und Beobachter öffnen; Navigation, Zurück, Suche/Filter und kontextbezogene Hilfe prüfen.
- Seitenleiste offen/geschlossen; Fenstergrössen und Skalierung 100 %, 125 %, 150 %, 200 % prüfen. Mindestfenster derzeit 1120 × 720 WPF-Einheiten (`MainWindow.xaml`).
- Lange Namen, grosse Datenmengen, leere Listen, Validierungsfehler und Tastaturbedienung testen; keine abgeschnittenen Labels, Dialogaktionen oder Eingabefelder.
- Vollständigen Ablauf auf Wegwerfdaten ausführen: Mitarbeiter/Skills → Auftrag → Personalplanung → Fertigung → Ist-Produktion/OEE → PDF/CSV → Backup/Restore → Neustart.
- Installer auf sauberem Windows-System ohne vorinstallierte .NET-Runtime sowie Update bestehender Installation testen; Portable-Modus auf beschreibbarem USB-Pfad testen.
- Aktuelle Build-Artefakte sind laut CI nicht signiert; Signaturstatus vor externer Auslieferung bewusst festlegen.

## Ausgeführte lokale Prüfungen

- `node --test tests/online-weekplan.test.cjs`: **8/8 erfolgreich**. Dies sind isolierte JS-Verhaltenstests mit simuliertem DOM/Firestore, keine echten Browser-/Firebase-End-to-End-Tests.
- `node --check` für Browser- und Functions-JavaScript.
- Alle **36 XAML-Dateien** als XML gelesen; **128 statisch erkannte Ereignisverweise** mit zugehörigem Code-behind abgeglichen, keine fehlenden Handler gefunden. Dies prüft weder alle Bindings noch visuelle Positionen.
- Alle **3 Workflow-YAML-Dateien** geparst, auch auf doppelte Schlüssel geprüft. Kein ausgeführter Tag-Release.
- `git diff --check` ohne Fehler.

Der neue Windows-PR-Lauf ist separat auf dem aktuellen PR-Commit zu kontrollieren. Die oben genannten 50/50 beziehen sich ausdrücklich auf den geprüften main-Lauf vor diesen Änderungen.

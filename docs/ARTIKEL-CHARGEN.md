# Artikel, Chargen und Startseite

Ein Artikel (zum Beispiel **0001 – Leviaprost**) bündelt seine Produktionschargen. Jede Charge ist ein eigener Produktionsauftrag mit eigenständiger Menge, Planung, Arbeitskarten und Ist-Erfassungen.

## Bedienung

1. **Artikel & Chargen → Artikel anlegen**: Artikelnummer als Text, Name und Einheit erfassen. Standardmenge, Sollrate und ein Standard-Arbeitsplan erleichtern die Wiederverwendung.
2. Artikel auswählen und **Neue Charge** öffnen. Auftragsnummer und Chargennummer manuell eingeben, Menge und Planung prüfen. Jede neue Charge beginnt ohne Ist-Daten.
3. Auf der Startseite nach **Heute**, **Alle offenen**, **Laufend**, **Probleme** oder **Abgeschlossen** filtern. Ein Klick auf die Chargennummer oder Enter auf der ausgewählten Zeile öffnet die Chargendetails.
4. **Fertigungssteuerung** und **Ist-Produktion** in den Details öffnen den zugehörigen Auftrag. Arbeitskarten werden über die vorhandene Funktion aus dem ausdrücklich zugeordneten Arbeitsplan erzeugt.
5. Der letzte fertige Arbeitsgang schliesst die Charge automatisch ab. Ohne Arbeitskarten kann ein Planer in den Details manuell abschliessen. Der Abschluss ist keine zusätzliche Qualitätsfreigabe.
6. **Abgeschlossene Chargen** ist die zentrale Sammelstelle, mit Artikel-, Text- und Abschlussdatumsfilter. Die Artikelübersicht zeigt ebenfalls alle zugehörigen Chargen, neueste zuerst.
7. Korrekturen an abgeschlossenen Chargen erfordern **Wieder öffnen** und eine Begründung. Der bisherige Abschluss wird im Audit protokolliert. Anschliessend erneut abschliessen. Bereits fertige Arbeitskarten werden dabei nicht zurückgesetzt.

## Regeln und historische Daten

- Artikelnummern behalten führende Nullen. Eine Chargennummer ist pro Artikel eindeutig; die Auftragsnummer bleibt global eindeutig.
- Neue Chargen benötigen einen aktiven Artikel. Verwendete Artikelnummern sind gesperrt; Artikel können deaktiviert werden.
- Artikelname, Einheit und Sollrate werden bei Erstellung im Auftrag festgehalten. Artikeländerungen verändern bestehende Aufträge nicht; Änderungen am Arbeitsplan verändern bereits erzeugte Arbeitskarten nicht.
- Beginn und Abschluss werden in UTC gespeichert und in der Oberfläche lokal angezeigt. Der erste Start setzt den Beginn. Ein manueller Abschluss erfindet keinen Produktionsbeginn.
- Neue Ist-Erfassungen übernehmen Artikel und Charge vom Auftrag. Historische Artikel-/Chargenangaben bleiben erhalten; Abweichungen werden in den Chargendetails ausgewiesen.
- Arbeitskartenmengen und Ist-Mengen sind getrennte Datenquellen und werden in den Details nicht addiert.
- Beobachter können Listen und Details lesen. Schreibaktionen erfordern Planer- oder Administratorrechte, auch in den Services und bisherigen Bearbeitungswegen.

## Upgrade und Betrieb

Beim ersten Start dieser Version erstellt die Schemaaktualisierung vor Änderungen eine Sicherung im vorhandenen Backup-Verzeichnis: `before-batches-<Zeit>-<ID>.kpibackup`. Sie verwendet das bestehende SQLite-Snapshot- und Wiederherstellungsformat. Schlägt die Sicherung fehl, wird die Aktualisierung nicht begonnen.

Die neue Schemaaktualisierung läuft transaktional und nur einmal pro Datenbank. Bestehende Aufträge werden ausschliesslich bei eindeutiger Artikelnummer, passendem Namen/Einheit, eindeutiger Chargennummer und widerspruchsfreien Ist-Chargen verknüpft. Es werden keine Artikel aus uneindeutigen Altdaten erfunden. Nicht verknüpfte Aufträge bleiben sichtbar mit **Artikelzuordnung offen**.

Historische Abschlussdaten werden nur aus vollständig fertigen Arbeitskarten mit erfassten Abschlusszeiten übernommen. Ohne belastbare Daten steht **Abschlussdatum unbekannt**. Solche Datensätze sind ohne Datumsfilter sichtbar, fallen aber nicht in einen ausgewählten Abschlusszeitraum.

Der CSV-Komplettexport enthält zusätzlich `artikel.csv`, `chargen.csv` und `chargen_ist_historie.csv`. Für Excel ist die Artikelnummer als Text zu importieren, damit Excel führende Nullen nicht selbständig entfernt. Das Datenbankbackup enthält alle neuen Felder und Tabellen.

Die Anwendung bleibt eine lokale WPF-/SQLite-Anwendung. Eine gemeinsame Netzwerkdatenbank oder ein HTTP-Server sind nicht Bestandteil dieses Ausbaus. `ArticleService` und `BatchService` bündeln die Fachlogik für eine spätere Trennung.

## Prüfung

```powershell
dotnet build Produktionsplanung.sln -c Release
dotnet run --project tests/Produktionsplanung.RegressionTests -c Release --no-build
```

Die Regressionstests decken Erstellung, Dubletten, unveränderte historische Daten, Arbeitsplanzuordnung, automatischen/manuellen Abschluss, Wiederöffnung/Audit, Rollenrechte, Datumsfilter, eindeutige Dashboard-Zählung, Navigation, Migration alter Tabellen und Backup/Restore ab. Mit `KPI_BATCH_PREVIEW_DIRECTORY` kann der Testlauf zusätzlich WPF-Ansichten in PNG-Dateien rendern; die Tests arbeiten ausschliesslich mit temporären Datenbanken.

# Windows-, DPI- und Mehrbenutzer-Abnahme

Ein grüner Build ist keine praktische Abnahme. Das Manifest aktiviert PerMonitorV2 mit PerMonitor-Fallback. Jede Zeile unten muss auf echten Windows-Geräten durchgeführt und im versionsgebundenen Protokoll belegt werden. Das Beispiel ist absichtlich **nicht bestanden**; es darf nicht als Prüfnachweis verwendet werden.

| Test-ID | Anzeige / Fall |
| --- | --- |
| 1366x768-100 | 1366 × 768, 100 % |
| 1920x1080-100 | 1920 × 1080, 100 % |
| 1920x1080-125 | 1920 × 1080, 125 % |
| 2560x1440-125 | 2560 × 1440, 125 % |
| 2560x1440-150 | 2560 × 1440, 150 % |
| 3840x2160-150 | 3840 × 2160, 150 % |
| 3840x2160-200 | 3840 × 2160, 200 % |
| mixed-dpi-monitors | Fenster zwischen Monitoren mit unterschiedlicher DPI verschieben |
| large-synthetic-data | 500 Mitarbeiter, fünf Jahre / 91.300 Zuweisungen, 3.000 Aufträge mit Chargennummern |
| two-pc-concurrency | Zwei PCs ändern denselben Datensatz; Konflikt wird sichtbar, keine stille Überschreibung |
| two-pc-messages | Versand, Popup, ungelesen/gelesen, Wichtig und Lesebestätigung zwischen zwei PCs |
| tenant-isolation | Zweite Firma kann weder Daten noch Nachrichten der ersten Firma lesen |
| server-restart | Server-Neustart, SignalR verbindet erneut, aktuelle Daten werden geladen |
| offline-recovery | Verbindungsabbruch erhält offene Eingaben, Fehler ist sichtbar, Wiederanlauf funktioniert |

Für **jede Anzeigezeile** prüfen: Login, Navigation, Dashboard, Kalender, Planung, Simulation, Schichtübergabe, DataGrid, Dropdowns, Chargen- und Bearbeitungsdialoge, PDF-Vorschau, Admin-Konsole und Message Center. Kriterien: alle Aktionen erreichbar, keine abgeschnittenen Inhalte ohne Scrollmöglichkeit, lesbare Texte, korrekte Popups und unveränderte Eingaben beim Monitorwechsel. Windows-Version, Monitor-/Skalierungseinstellungen, Screenshots und Fehlerprotokolle im Beleg dokumentieren.

Die automatisierte Windows-Regression erzeugt große Testdaten ausschließlich aus synthetischen Konstanten und prüft Wochenabfragen, What-if-Besetzung, Konfliktfreiheit und Schreibfreiheit. Sie ersetzt keine Lastmessung aller Module oder Bedienprüfung. Echte Kundendaten gehören nicht in CI; Namen zu ersetzen genügt nicht, weil Freitexte, Audit, Backups, Nachrichten und Zugangsdaten weiterhin Personenbezüge enthalten können. Eine kundenspezifische, vollständig geprüfte Anonymisierung bleibt separat erforderlich, falls solche Daten genutzt werden sollen.

Protokoll vorbereiten:

```sh
node -e 'const fs=require("fs");const r=require("./docs/release-acceptance/template.json");r.version="1.2.3";fs.writeFileSync("docs/release-acceptance/1.2.3.json",JSON.stringify(r,null,2)+"\n")'
```

`sourceCommit` auf den vollständigen geprüften Commit setzen; pro Test `status: "passed"`, Prüfer, UTC-Zeit und konkrete Belegreferenz eintragen. Danach lokal prüfen:

```sh
node scripts/validate-release-acceptance.cjs --tag v1.2.3
```

Nicht durchgeführte oder fehlgeschlagene Prüfungen bleiben `pending` bzw. `failed` und blockieren den offiziellen Release. Nicht nur das Protokoll, sondern auch die verlinkten Belege sind menschlich zu prüfen. Die Pipeline validiert Vollständigkeit und Quellenbindung, kann die Wahrheit eines manuellen Eintrags aber nicht bestätigen.

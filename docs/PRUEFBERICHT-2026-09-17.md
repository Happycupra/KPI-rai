# KPI-rai – Prüfung und Branch-Bereinigung
Stand: 17.09.2026 · Repository: Happycupra/KPI-rai

## 1. Ergebnis
Annahme zum Umfang: Prüfung der acht genannten Funktionen und Konsolidierung bestehender Änderungen. Neue Produktfunktionen werden in diesem Abschnitt nicht implementiert. „Teilweise“ bedeutet ausdrücklich nicht freigabefertig.

| Nr. | Funktion | Status nach Zusammenführung | Vorhanden | Fehlende Teile |
|---|---|---|---|---|
| 1 | Inaktivitätssperre | Teilweise | AutoLockEnabled, AutoLockMinutes; Auswahl 15/30/60 Minuten in Einstellungen | Keine Aktivitätsmessung, kein Sperrmechanismus, kein erzwungener erneuter Login |
| 2 | UI je Benutzer | Teilweise | Kalenderansicht und Filter in UserUiPreferences je normalisiertem Benutzernamen; Übernahme alter Einstellungen | MainWindow lädt und schreibt Menü-/Sidebarzustände weiterhin installationsweit |
| 3 | Ungespeicherte Änderungen | Teilweise | IUnsavedChangesAware in Mitarbeiter-, Auftrags- und Einstellungsansicht | Navigation/Zurück/Fensterschließen rufen Vertrag nicht auf; Dialog Speichern/Verwerfen/Abbrechen fehlt; Rücksetzung der Vergleichsbasis muss geprüft werden |
| 4 | Backup-Warnung | Fehlt | LastSuccessfulBackupAtLocal und LastSuccessfulBackupPath werden nach erfolgreichem Backup gespeichert | Dashboard zeigt weder Backup-Alter noch gelbe/rote Warnung |
| 5 | Einrichtungsassistent | Fehlt | Einzelne Verwaltungsansichten vorhanden; Start führt DemoDataSeeder aus | Kein geführter Ablauf Firma → Werk → Schichten → Arbeitsplätze → Mitarbeiter → Qualifikationen |
| 6 | Benachrichtigungszentrum | Teilweise | Dashboard.Issues zeigt Unterbesetzung, Abwesenheitskonflikte, überfällige Aufträge und Auftragsprobleme | Keine Glocke, kein zentraler Bereich, keine vollständige Bündelung von Skills, Backups und Konflikten |
| 7 | Live-Konfliktprüfung | Teilweise | Speichern prüft Schichtfreigabe, Qualifikation, Abwesenheit und Doppelbelegung; Hinweise und Vorschläge vorhanden | Keine vollständige Liveprüfung des aktuellen Entwurfs; MaximumStaff wird beim manuellen Speichern nicht als Grenze geprüft |
| 8 | Papierkorb/Archiv | Teilweise | Mitarbeiter aktivier-/deaktivierbar; Löschschutz für referenzierte Daten und Produktionshistorie | Endgültiges Löschen bleibt möglich; kein einheitlicher Archiv-/Wiederherstellungsablauf für Mitarbeiter, Aufträge und Stammdaten |

Technische Belege:
- 1–2: Services/AppSettingsService.cs, ViewModels/SettingsViewModel.cs, Views/SettingsView.xaml, Views/PlanningCalendarView.xaml.cs, MainWindow.xaml.cs.
- 3: Services/IUnsavedChangesAware.cs, Views/{Employees,ProductionOrders,Settings}View.xaml.cs und MainWindow.xaml.cs.
- 4–6: Services/BackupService.cs, ViewModels/DashboardViewModel.cs, Views/DashboardView.xaml, App.xaml.cs und MainWindow.xaml.
- 7: ViewModels/DayPlanningViewModel.cs, DayPlanningViewModel.Skills.cs und Views/DayPlanningView.xaml.
- 8: Models/Entities.cs und die Löschbefehle der Stammdaten-/Auftragsverwaltung.

Branch-Prüfung:
- Sieben Nebenbranches waren bereits Vorfahren von main: feat/login-recovery-nav-persistence-20260917, feat/recovery-code-management-20260917, feat/solutioncompakt-branding, fix/app-review-20260917, fix/final-stabilization-2026-09-17, fix/pruefbericht-2026-09-17, fix/review-runtime-data-integrity.
- feat/security-userprefs-dirty-backup-20260917 enthält acht zusätzliche Commits (7383fbf bis 3fc6337). Diese werden mit beiden Eltern in main zusammengeführt, damit die Arbeit vor einer Branch-Löschung erhalten bleibt.
- Zusammenführung lokal ohne Konflikte; Build Release: 0 Fehler, 0 Warnungen. Alle 16 vorhandenen Regressionstests erfolgreich.
- Bestehende Tests decken die acht neuen Anforderungen nicht vollständig ab. Keine Aussage über einen manuellen End-to-End-Test der fehlenden Funktionen.
- Das Ziel „nur main“ ist erst erreicht, wenn die entfernten Branches tatsächlich gelöscht und erneut abgefragt wurden.

## 2. Offene Punkte
Keine der acht Anforderungen ist vollständig erfüllt. Ein erfolgreicher Build ersetzt diese fachliche Prüfung nicht.
Für den Firmennetzwerkbetrieb fehlen weiterhin zentrale Datenhaltung und ein abgestimmtes Mehrbenutzerkonzept; das MVP verwendet eine lokale SQLite-Datenbank und lokale JSON-Einstellungen.
Der Connector kann Commits und Referenzänderungen schreiben, bietet aber keine Branch-Löschung. Die lokale CLI ist als tejari49 ohne Schreibrecht angemeldet; Chrome benötigt die Anmeldung mit einem berechtigten Konto.

## 3. Konkrete nächste Schritte
1. Nach erfolgreicher Veröffentlichung des Konsolidierungscommits alle acht Nebenbranches löschen; vorher jeden Branch-Tipp gegen main auf Erreichbarkeit prüfen.
2. In einem nächsten Implementierungsabschnitt zuerst die bereits begonnenen Punkte 1–3 vervollständigen.
3. Danach Backup-Warnung und Live-Konfliktprüfung; anschließend Benachrichtigungszentrum, Einrichtungsassistent und Archiv.
4. Je Funktion die folgenden Akzeptanzkriterien gezielt testen.

## 4. Akzeptanzkriterien
| Nr. | Für die spätere Umsetzung verbindlich zu prüfen |
|---|---|
| 1 | Aktivität setzt Frist zurück; nach 15/30/60 Minuten werden Haupt- und Nebenfenster gesperrt; Zugriff erst nach gültigem Login; Entwürfe bleiben geschützt |
| 2 | Benutzer A und B behalten unabhängig Kalenderfilter und Menüzustände; erneuter Login stellt die jeweils eigene Ansicht wieder her; alte Werte werden einmal übernommen |
| 3 | Bei Mitarbeiter-/Auftragsänderung und Navigation erscheinen Speichern/Verwerfen/Abbrechen; Abbrechen bleibt auf der Seite; ungültiges Speichern verhindert Navigation; erfolgreiches Speichern erzeugt keine erneute Warnung |
| 4 | Kein erfolgreiches Backup: rot; vorgeschlagene MVP-Grenzen: ab 7 Tagen gelb, ab 14 Tagen rot; Alter in Tagen sichtbar; fehlgeschlagene Backups aktualisieren den Erfolg nicht |
| 5 | Neue Installation wird in der genannten Reihenfolge eingerichtet; Pflichtfelder validiert; Abbruch/Fortsetzen ohne Datenverlust; abgeschlossene Einrichtung erscheint nicht erneut automatisch |
| 6 | Glocke mit Anzahl und Liste bündelt alle genannten Problemtypen; Eintrag führt zum betroffenen Bereich; behobene Probleme verschwinden nach Aktualisierung |
| 7 | Die fünf genannten Konflikte werden vor dem Klick auf Speichern live angezeigt; bei Bearbeitung wird die eigene Zuweisung ausgeschlossen; Nacht-/Nachbarschichten werden berücksichtigt; Speichern validiert nochmals |
| 8 | Archivieren erhält Historie und Referenzen; archivierte Datensätze werden aus neuen Planungen ausgeschlossen; Wiederherstellung möglich; Stammdaten und Aufträge folgen demselben Prinzip |

Bereinigung gilt als abgeschlossen, wenn der Commit auf main liegt, alle acht bisherigen Branch-Tipps über main erhalten bleiben, keine Nebenbranches mehr existieren und der Arbeitsbaum sauber ist.

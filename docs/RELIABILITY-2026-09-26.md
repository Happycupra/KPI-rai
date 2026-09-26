# Fehlerbehebung und Absicherung – 26.09.2026

Dieses Paket korrigiert bestehende Abläufe. What-if-Planung, Schichtübergabe und der separate Arbeitszeit-PR sind nicht enthalten.

## Verhalten nach dem Update

- Eine fehlgeschlagene laufende Lizenzprüfung pausiert alle offenen Anwendungsfenster, statt die Anwendung zu beenden und Eingaben zu verwerfen. Der Prüftimer bleibt aktiv; nach erfolgreicher Prüfung werden die vorher aktivierten Fenster wieder freigegeben. Eine echte Sperre bleibt wirksam. Beim bewussten Beenden während der Pause wird vor Eingabeverlust gewarnt.
- Bestehende Online-Sitzungen dürfen bei gesperrter/abgelaufener Lizenz, deaktiviertem Benutzer oder veralteter Rolle keine weiteren Datenzugriffe ausführen. Nach synchronisierter Passwortänderung wird ebenfalls eine erneute Anmeldung erforderlich. Bereits angezeigte oder zuvor heruntergeladene Daten können nicht zurückgerufen werden.
- Der Publish-Endpunkt prüft aktuelle Berechtigungen vor und nach dem Upload. Eintrags-IDs werden vor dem Schreiben validiert. Jede Veröffentlichung schreibt in eine eigene Version; erst der abschliessende Zeigerwechsel macht sie sichtbar. Der Web-Client liest weiterhin auch ältere, unversionierte Pläne.
- Backups werden zunächst in eine temporäre Archivdatei geschrieben und danach ersetzt. Vorhandene Archive werden bei fehlgeschlagener Erstellung nicht vorzeitig gelöscht.
- Restore prüft SQLite-Integrität und Fremdschlüssel, berücksichtigt WAL und stellt bei Fehlern beim Dateiaustausch den vorherigen Datenbank-/Einstellungsstand wieder her. Bei nicht möglichem Rollback bleiben Sicherheitsdateien erhalten und ein Neustart wird verlangt. Kein vollständiger Schutz gegen Betriebssystem-/Stromausfall zwischen mehreren Dateiaustauschen.
- Einstellungen werden ebenfalls über eine temporäre Datei ausgetauscht. Restore hält währenddessen die Einstellungssperre.
- Updates benötigen HTTPS und eine gültige SHA-256-Prüfsumme. Mehrdeutige Installer-Pakete und beliebige EXE-Fallbacks werden abgelehnt.
- Bereits vorhandene Lizenzidentitäten werden bei wiederholter Prüfung nicht erneut auf die Platte geschrieben; HTTP-Antworten werden freigegeben. Eine bestehende Compilerwarnung beim Start der Einführung ist bereinigt.

## Nachweis

- Web-/Backend-Verhaltenstests einschliesslich unterbrochener und paralleler Veröffentlichungen.
- Firestore-Emulatortests für Mandantentrennung, Lizenzsperre/-ablauf, Benutzerdeaktivierung, Rollen-/Passwortänderung und nicht veröffentlichte Versionen.
- Windows-Regressionstests für Backup-Überschreibfehler, Restore-Rollback, beschädigte Backups, fehlgeschlagene Einstellungsschreibvorgänge, Updatevalidierung und Wiederaufnahme nach Lizenzpause.
- Der Windows-Build hängt vom erfolgreichen Firestore-Regeltest ab.

## Verbleibende Grenzen

- Code-Signing bleibt von einem bereitgestellten gültigen Zertifikat abhängig; dieses Paket erzeugt kein Zertifikat.
- Vollständige Windows-/DPI-Bedienungsabnahme und Tests mit echten Kundendaten sind separat erforderlich.
- Alte/unvollständige Online-Snapshot-Versionen werden aufbewahrt; eine automatisierte Aufbewahrungsregel ist noch nicht enthalten.
- Die vollständige Benutzerlisten-Synchronisation ist weiterhin nicht atomar; Benutzerzugriffe während dieses kurzen Austauschs können vorübergehend abgelehnt werden.
- Ein serverseitiges Login-Rate-Limit und eine dauerhafte Sync-Wiederholungswarteschlange sind separate Folgearbeiten.

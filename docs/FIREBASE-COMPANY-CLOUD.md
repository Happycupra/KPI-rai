# Firmenkonto und Homeoffice über Firebase

Der Firebase-Modus speichert gemeinsame Betriebsdaten in Firestore. SQLite ist die lokale Arbeitskopie, einschließlich noch nicht bestätigter Änderungen. PostgreSQL/SignalR bleibt ein separater, optionaler Betriebsmodus; beide Modi werden nicht gleichzeitig aktiviert.

## Erste Übernahme einer bestehenden lokalen Firma

1. Alle beteiligten PCs auf die neue App-Version aktualisieren. Die Firma und ihre Benutzer müssen bereits über den vorhandenen Online-Zugang mit aktiver Firmenlizenz registriert sein.
2. Eine Datensicherung der bisherigen Installation erstellen.
3. Im Login „Bei bestehender Firma über Firebase anmelden“ wählen und Firmen-Code, Benutzername und Passwort des Firmenadministrators eingeben.
4. Die angezeigte erste Übernahme bestätigen. Die App überträgt die vorhandenen lokalen Datensätze einschließlich Historie und Nachrichten portionsweise. Firebase veröffentlicht den Datensatz erst nach vollständiger Prüfung.
5. Nach dem Download arbeitet dieser PC auf einer neuen Firmen-Arbeitskopie; die ursprüngliche SQLite-Datei bleibt erhalten.

Eine bereits aktiv genutzte PostgreSQL-Installation benötigt eine gesonderte Übernahme ihres aktuellen Datenbestands. Die App verhindert einen unbemerkten Wechsel auf eine möglicherweise ältere lokale SQLite-Kopie.

## Neuer PC / Homeoffice

App installieren, „Bei bestehender Firma über Firebase anmelden“ wählen und dieselben Firmen-Zugangsdaten eingeben. Der Benutzer meldet sich mit **seinem eigenen Firmenkonto** an, nicht mit dem Administratorpasswort. Ein neues Gerät wird automatisch der aktiven Firmenlizenz zugeordnet. Eine zweite Firma oder ein zweites Administratorkonto wird dabei nicht angelegt.

Der erste Login und Download benötigen Internet. Bei späteren Anmeldungen bleibt die vorhandene Arbeitskopie mit ausstehenden Änderungen erhalten. Pro Benutzer und Rolle werden getrennte Arbeitskopien verwendet. Andere Benutzerpasswörter, lokale PINs, Gerätegeheimnisse und Anbieter-Zugangsdaten sind nicht Bestandteil der Firmendatensynchronisation.

## Gemeinsame Daten und Sicherheit

Synchronisiert werden Mitarbeiter, Qualifikationen, Arbeitsplätze, Schichten, Regeln, Abwesenheiten, Betriebskalender, Arbeitszeiten, Artikel, Planung, Produktionsaufträge, Lauf-/Fertigungsdaten, Arbeitsgänge, Ist-Produktion, Stillstände, Schichtübergaben und Papierkorb. Audit-Historie ist Administratoren vorbehalten. Persönliche Nachrichten sind auf ihre jeweiligen Absender/Empfänger beschränkt.

Jeder Datensatz besitzt eine globale ID, einen Versionsstand und einen serverseitigen Änderungszeitpunkt. Lokale Integer-IDs bleiben erhalten und werden an Beziehungen eindeutig übersetzt. Qualifikationszuordnungen haben eine deterministische Kennung aus Mitarbeiter und Qualifikation. Der Backend-Zugriff erfolgt über Firebase-ID-Tokens mit Prüfung der aktuellen Benutzer-Version, Rolle, Aktivierung und Firmenlizenz; operative Datensynchronisation prüft zusätzlich die Gerätebindung. Direkte Firestore-Zugriffe auf Daten, Nachrichten, Geräte und Kennwortdaten sind gesperrt.

Benutzerverwaltung und Passwortänderungen laufen über Cloud-Endpunkte. Alte Installationen dürfen nach Aktivierung des Firmenkontos keine vollständigen Benutzerlisten mehr über den bisherigen Desktop-Sync ersetzen. Lokale PINs bleiben gerätespezifisch. Tokens werden unter Windows mit DPAPI geschützt gespeichert.

## Offline und Konflikte

Die App bestätigt Änderungen zuerst lokal und speichert den Übertragungsauftrag in derselben SQLite-Transaktion. Änderungen werden in der Regel alle 15 Sekunden übertragen bzw. abgefragt. Verbindungsfehler erhalten die Warteschlange; identische Wiederholungen erzeugen keine zusätzlichen Datensätze oder Nachrichten. Bei aktueller, bestätigter Berechtigung ist Offline-Arbeit für bis zu 24 Stunden möglich, niemals über das Lizenzende hinaus. Neue Online-Prüfungen verweigern deaktivierten Benutzern oder gesperrten Firmen den Zugriff.

Die Versionsprüfung schützt vor stiller Überschreibung. Eingaben werden bei eingehenden Daten nicht automatisch verworfen. Unter „Firmenkonto / Synchronisation“ können ausstehende Änderungen, Fehler und Konflikte geprüft werden. Ein erneutes Übernehmen lokaler Änderungen verwendet eine neue Auftragskennung und den aktuellen Firmen-Versionsstand. Ein vollständiges Neuladen mit Verwerfen lokaler Änderungen verlangt eine ausdrückliche Entscheidung und erhält zusätzlich die vorherige Arbeitskopie.

Ein lokales `.kpibackup` darf die Firmencloud nicht zurücksetzen. Wiederherstellung des zentralen Cloud-Bestands benötigt einen kontrollierten Administratorablauf. Vor Produktivbetrieb sind Firestore-Backups und die Wiederherstellung zu prüfen; eine lokale Arbeitskopie ersetzt kein Cloud-Backup.

## Bereitstellung und Abnahme

Die bestehende Firebase-Pipeline veröffentlicht die zusätzlichen HTTPS-Funktionen, Regeln und Indizes im Projekt `solution-compact`. Kundendaten werden dadurch nicht automatisch hochgeladen; die erstmalige Datenübernahme wird in der App vom Firmenadministrator gestartet.

Automatisierte Prüfungen laufen gegen den Firestore-Emulator und isolierte Windows-/SQLite-Fixtures. Zusätzlich mit zwei echten PCs prüfen: erstmaliger Homeoffice-Login, gemeinsamer Datenstand, Nachrichten und Lesebestätigung, Offline-Wiederanlauf, gleichzeitige Bearbeitung, Rollenänderung, Lizenzsperre, große Datenbestände und PDF-/Windows-/DPI-Verhalten. Code-Signing und die dokumentierte manuelle Release-Abnahme bleiben Voraussetzungen offizieller Releases.

# Zentraler Echtzeit-Mehrbenutzerbetrieb

SolutionCompakt unterstützt neben dem bisherigen lokalen SQLite-Modus einen optionalen zentralen Mehrbenutzerbetrieb mit PostgreSQL und SignalR.

## Architektur

```text
PC A SolutionCompakt ----\
                          +---- PostgreSQL 16 (gemeinsame operative Daten)
PC B SolutionCompakt ----/
          |                         ^
          +---- HTTPS/SignalR ------+
                    |
             SolutionCompakt.Server
```

Die vorhandenen WPF-Services verwenden im Zentralmodus denselben `AppDbContext`, aber mit Npgsql/PostgreSQL statt SQLite. Dadurch sind Mitarbeiter, Qualifikationen, Planung, Produktionsaufträge, Fertigungssteuerung, Chargen, Ist-Produktion/OEE, Benutzer, Audit, Schichtübergaben und persönliche Nachrichten gemeinsam verfügbar, ohne jedes Modul separat als REST-Client neu zu implementieren.

Der lokale SQLite-Betrieb bleibt Standard und funktioniert unverändert, solange `central-mode.json` nicht aktiviert ist.

## Mandantentrennung

Jede SolutionCompakt-Firma erhält in PostgreSQL ein eigenes Schema:

```text
company_<CompanyId als GUID-N>
```

Die Tabellen des bestehenden operativen Datenmodells liegen ausschliesslich in diesem Firmenschema. Die Server-/Realtime-Metadaten liegen getrennt davon.

## Erster PC – bestehende lokale Daten übernehmen

Voraussetzung: Die vorhandene lokale Installation besitzt bereits Firma, CompanyId/CompanyCode und mindestens einen Benutzer.

1. `.env.central.example` nach `.env.central` kopieren.
2. Sichere Werte für `POSTGRES_PASSWORD` und `SOLUTIONCOMPAKT_JWT_KEY` setzen.
3. Server starten:

```powershell
docker compose --env-file .env.central -f docker-compose.central.yml up -d --build
```

4. Health prüfen:

```powershell
Invoke-RestMethod http://127.0.0.1:8088/health
```

5. Zentralmodus für die Desktop-App aktivieren:

```powershell
.\scripts\Enable-CentralMode.ps1 `
  -PostgresPassword '<gleiches-passwort-wie-in-.env.central>' `
  -ServerHost '127.0.0.1' `
  -ServerUrl 'http://127.0.0.1:8088'
```

6. SolutionCompakt neu starten.

Beim ersten Zentralstart wird das Firmenschema erstellt. Ist es leer, werden die lokalen SQLite-Daten einmalig in einer Transaktion übernommen. Die vorhandenen Primärschlüssel bleiben erhalten und PostgreSQL-Sequenzen werden anschliessend auf die importierten Maximalwerte gesetzt.

Nach erfolgreicher Übernahme ergänzt SolutionCompakt `CompanyId`, `CompanyCode` und `CompanyName` automatisch in `central-mode.json`.

## Zweiter PC derselben Firma

Am einfachsten wird das bereits vervollständigte `central-mode.json` des ersten PCs sicher auf den zweiten PC kopiert. Die Datei enthält die Datenbank-Verbindungsinformation und ist deshalb wie ein Kennwort zu behandeln.

Alternativ:

```powershell
.\scripts\Enable-CentralMode.ps1 `
  -PostgresPassword '<passwort>' `
  -ServerHost '192.168.1.20' `
  -ServerUrl 'http://192.168.1.20:8088' `
  -CompanyId '<CompanyId des ersten PCs>' `
  -CompanyCode '<CompanyCode>' `
  -CompanyName '<Firmenname>' `
  -NoAutoMigrate
```

Beim Start übernimmt die frische Installation diese Firmenidentität und verwendet sofort die bereits zentral vorhandenen Benutzer. Es wird keine zweite Firmen-ID angelegt.

Jede Windows-Installation behält weiterhin ihre eigene Lizenzidentität. Für die SignalR-Anmeldung muss die Installation online als aktive Lizenz bestätigt werden können.

## LAN-Zugriff

PostgreSQL wird standardmässig nur an `127.0.0.1:5432` gebunden. Für einen zweiten Rechner muss in `.env.central` z. B. gesetzt werden:

```text
POSTGRES_BIND_IP=0.0.0.0
SERVER_BIND_IP=0.0.0.0
```

Danach den Stack neu starten. Port 5432 darf **nicht offen ins Internet** gestellt werden. Zugriff nur aus einem vertrauenswürdigen LAN/VPN zulassen und die Host-Firewall entsprechend einschränken. Für produktive externe Verbindungen PostgreSQL-TLS bzw. VPN verwenden und in `central-mode.json` `SSL Mode=Require` setzen.

## Echtzeit

Nach dem normalen SolutionCompakt-Login fordert der Desktop beim Server ein kurzlebiges JWT an. Der Server:

1. prüft die Installationslizenz über den bestehenden `licenseStatus`-Dienst,
2. prüft `source_user_id`, Benutzername, Rolle und Aktivstatus direkt gegen `company_<id>.UserAccounts`,
3. signiert erst danach das JWT,
4. ordnet die SignalR-Verbindung ausschliesslich der Firmen-Gruppe aus dem signierten `company_id`-Claim zu.

Jeder erfolgreiche zentrale Schreibvorgang meldet die betroffenen Entitätstypen an SignalR. Andere PCs derselben Firma aktualisieren Hinweise, Nachrichten und die aktuell sichtbare Ansicht. Bei ungespeicherten Eingaben wird nicht automatisch neu geladen; stattdessen bleibt die Eingabe erhalten.

## Gleichzeitige Änderungen

Im Zentralmodus besitzt jede EF-Entität zusätzlich einen Shadow-`ConcurrencyToken`. Bei `UPDATE` wird der ursprünglich geladene Token mitgeprüft. Hat ein anderer Benutzer denselben Datensatz inzwischen geändert, wird die zweite Änderung abgelehnt und der Benutzer zum Aktualisieren aufgefordert. Dadurch gilt nicht einfach unbemerkt „last writer wins“.

## Backup

`.kpibackup` ist ein SQLite-Backupformat und deshalb im Zentralmodus bewusst deaktiviert. PostgreSQL muss serverseitig gesichert werden, z. B. mit `pg_dump` und einer externen/zweiten Aufbewahrung.

Der lokale SQLite-Datenbestand wird bei der Erstübernahme nicht gelöscht und kann als Rückfallkopie erhalten bleiben. Mit `scripts/Disable-CentralMode.ps1` kann die Anwendung wieder im lokalen Modus gestartet werden; Änderungen, die zwischenzeitlich nur zentral erfolgt sind, werden dabei nicht automatisch zurück in die alte SQLite-Datei synchronisiert.

## Konfiguration ohne Klartext-Verbindungsstring in der Datei

Für verwaltete Installationen können Umgebungsvariablen die Datei überschreiben:

```text
SOLUTIONCOMPAKT_CENTRAL_ENABLED=true
SOLUTIONCOMPAKT_CENTRAL_DB=Host=...;Database=...;Username=...;Password=...;SSL Mode=Require
SOLUTIONCOMPAKT_CENTRAL_SERVER=https://server.example.ch
```

## Testmatrix

Für den Funktionstest mindestens:

1. PC A: Mitarbeiter ändern -> PC B sieht Aktualisierung.
2. PC A: persönliche Nachricht an Benutzer auf PC B -> PC B erhält Nachricht/Popup.
3. PC B: Lesebestätigung -> PC A sieht den bestätigten Status.
4. Beide PCs öffnen denselben Datensatz, PC A speichert zuerst, PC B versucht danach zu speichern -> Concurrency-Konflikt statt stiller Überschreibung.
5. Planung/Produktionsauftrag auf PC A anlegen -> PC B sieht denselben Datensatz.
6. App auf PC B schliessen/öffnen -> Daten bleiben vollständig zentral vorhanden.
7. Benutzer zentral deaktivieren -> erneute Anmeldung/Realtime-Token für diesen Benutzer wird abgelehnt.
8. Server/SignalR kurz stoppen -> zentrale PostgreSQL-Daten bleiben nutzbar, Realtime-Warnung erscheint; nach Serverwiederherstellung beim nächsten Start wieder verbinden.

## Technische Grenzen dieser Betriebsart

- PostgreSQL ist im Zentralmodus die führende operative Datenbank; es gibt noch keinen Offline-Schreibcache für Arbeiten ohne Datenbankverbindung.
- Für mehrere parallel laufende `SolutionCompakt.Server`-Instanzen ist eine SignalR-Backplane/Managed-SignalR-Lösung erforderlich. Eine einzelne Serverinstanz ist vollständig unterstützt.
- Schemaänderungen am operativen Datenmodell benötigen für spätere Releases einen versionierten PostgreSQL-Migrationspfad. Die Erstbereitstellung des aktuellen Schemas ist automatisiert.

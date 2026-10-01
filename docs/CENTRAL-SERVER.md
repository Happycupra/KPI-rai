# Zentraler Mehrbenutzerbetrieb – Serverbasis

Diese Ausbaustufe ergänzt SolutionCompakt um eine optionale zentrale Serverarchitektur. Der bestehende lokale SQLite-Betrieb bleibt unverändert und ist weiterhin der Standard, bis einzelne Desktop-Module bewusst auf den Server-Datenprovider umgestellt werden.

## Zielarchitektur

```text
SolutionCompakt WPF
       |
       | HTTPS / SignalR
       v
SolutionCompakt.Server (.NET 8)
       |
       +-- PostgreSQL 16
       +-- SignalR Echtzeitgruppen pro CompanyId
```

Firebase bleibt vorerst für Lizenzierung und den bestehenden Online-Wochenplan bestehen. Die zentrale relationale Produktionsdatenbank liegt bewusst in PostgreSQL.

## Enthalten

- neues ASP.NET-Core-Projekt `src/SolutionCompakt.Server`
- PostgreSQL über EF Core/Npgsql
- JWT-Authentifizierungsgrenze
- verpflichtende Mandantenkennung `company_id`
- Benutzerkennung `source_user_id`
- SignalR-Hub `/hubs/company`
- serverseitige SignalR-Gruppen strikt nach Firma
- EF-Queryfilter für mandantenbezogene Tabellen
- Schutz vor firmenübergreifenden Schreibvorgängen
- optimistische Concurrency-Grundlage über `ConcurrencyToken`
- `ChangeEvents` als Grundlage für inkrementelle Synchronisation
- Dockerfile und lokale PostgreSQL-Compose-Konfiguration
- `/health`, `/api/v1/me` und geschützter Echtzeit-Ping zum Integrationstest

## Token-Claims

Ein gültiger Desktop-Token muss mindestens enthalten:

```text
company_id      = bestehende SolutionCompakt CompanyId (GUID; N-Format ist gültig)
source_user_id  = lokale UserAccount.Id
role            = Administrator | Planer | Beobachter
```

Der Server erzeugt in dieser Stufe absichtlich noch keine Tokens. Die nächste Integrationsstufe verbindet die vorhandene SolutionCompakt-Anmeldung/Lizenzidentität mit einer serverseitigen Token-Ausgabe. Dadurch entsteht kein zweites unkoordiniertes Passwortsystem.

## Lokal starten

Benötigt werden zwei Geheimnisse als Umgebungsvariablen. Keine Produktionsgeheimnisse in Git einchecken.

PowerShell:

```powershell
$env:POSTGRES_PASSWORD = '<starkes-lokales-passwort>'
$env:SOLUTIONCOMPAKT_JWT_KEY = '<mindestens-32-zufällige-zeichen-besser-64>'
docker compose -f docker-compose.central.yml up --build
```

Danach:

```text
GET http://localhost:8088/health
```

liefert bei erreichbarer PostgreSQL-Datenbank HTTP 200.

## Datenbankbereitstellung

`Database__EnsureCreatedOnStartup=true` ist nur für die aktuelle Entwicklungs-/Pilotstufe gedacht. Vor dem produktiven Rollout wird auf versionierte EF-Core-Migrationen umgestellt und `EnsureCreatedOnStartup` deaktiviert.

## Mandantentrennung

Mandantenbezogene Entitäten implementieren `ITenantEntity`. Der `CentralDbContext` blendet Datensätze anderer Firmen über globale Queryfilter aus. Bei Schreibvorgängen wird zusätzlich geprüft, dass `CompanyId` des Datensatzes exakt dem authentifizierten Claim entspricht. Ein Client darf seine Firma daher nicht über Request-Payloads frei wählen.

## Echtzeit

Authentifizierte Clients verbinden sich mit:

```text
/hubs/company
```

Der Server ordnet die Verbindung anhand des signierten `company_id`-Claims einer Gruppe zu. Die Desktop-App soll Änderungen später über das Ereignis `change` empfangen und anschließend nur die betroffenen Datensätze nachladen.

Für mehrere gleichzeitig laufende Serverinstanzen ist später ein SignalR-Backplane/Managed-SignalR-Dienst erforderlich. Die aktuelle Stufe ist für eine Serverinstanz ausgelegt.

## Nächste Migration

Die Desktop-Migration sollte nicht als Big Bang erfolgen. Empfohlene Reihenfolge:

1. zentrale Authentifizierung/Token-Ausgabe mit bestehender Firmen- und Lizenzidentität verbinden
2. Desktop-`CentralServerClient` + SignalR-Verbindung ergänzen
3. Benutzer und persönliche Nachrichten zentralisieren
4. Schichtübergabe zentralisieren
5. Wochen-/Personalplanung zentralisieren
6. Produktionsaufträge, Chargen und Ist-Produktion umstellen
7. Offline-Outbox/Retry und Konfliktauflösung ergänzen
8. SQLite als Offline-Cache statt alleinige Datenquelle verwenden

Damit bleibt die bestehende Desktop-App während der Migration funktionsfähig.

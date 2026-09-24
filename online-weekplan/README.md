# SolutionCompakt Online-Wochenplan

Dieses Verzeichnis ist die vorbereitete Firebase-Webanwendung für den SolutionCompakt-Wochenplan. **Firebase-Projektdaten sind für den Quellcode noch nicht nötig.** Ohne `public/config.json` zeigt die Web-App nur den Einrichtungsstatus.

## Zielbild

- Anmeldung mit **Firmen-Code + Benutzername + Passwort**
- angemeldete Nicht-Administratoren: Wochenplan **nur ansehen**
- Administratoren: ansehen, JSON-Wochenplan veröffentlichen und minimale **Online-Korrekturen**
- lokale SolutionCompakt-Planung bleibt führend
- Online-Korrekturen liegen separat in `weekPlans/{weekId}/overrides` und verändern die Desktop-Daten nicht
- Benutzername und Passwort können später identisch zur Desktop-App bleiben: der Cloud-Function-Endpunkt prüft die bestehenden PBKDF2-SHA256-Hashes (150000 Iterationen) und erzeugt ein Firebase Custom Token mit Firma und Rolle als Claims

## Wenn das Firebase-Projekt später vorhanden ist

1. Firebase-Projekt anlegen und Firestore, Authentication, Hosting und Functions aktivieren.
2. Firebase CLI installieren und im Ordner `online-weekplan` initialisieren/zuordnen.
3. `public/config.example.json` nach `public/config.json` kopieren und Project ID, Web API Key, App ID sowie die beiden Function-URLs eintragen.
4. `firebase deploy` ausführen.
5. In SolutionCompakt unter Wochenplanung → **Firebase konfigurieren** dieselben öffentlichen Projektwerte/URLs eintragen.
6. Die Firma unter `companies/{companyId}` und mindestens einen Administrator unter `companies/{companyId}/authUsers` bereitstellen.
7. In SolutionCompakt als Administrator **Online-Paket vorbereiten** und die erzeugte JSON-Datei im Web-Adminbereich veröffentlichen.

## Firmen- und Benutzerstruktur

Jede Firma besitzt eine stabile, von SolutionCompakt erzeugte `companyId` und einen lesbaren `companyCode`. Dadurch können verschiedene Firmen dieselben Benutzernamen verwenden, ohne Daten zu vermischen.

`companies/{companyId}` enthält mindestens:

```json
{
  "companyCode": "MUSTER-AG",
  "companyName": "Muster AG",
  "isActive": true
}
```

Die Unter-Collection `companies/{companyId}/authUsers` ist durch Firestore-Regeln vollständig vor Browserzugriff geschützt. Ein Benutzerdokument benötigt:

```json
{
  "sourceUserId": 1,
  "username": "admin",
  "usernameNormalized": "admin",
  "displayName": "Administrator",
  "role": "Administrator",
  "isActive": true,
  "passwordHash": "<Base64 aus SolutionCompakt>",
  "passwordSalt": "<Base64 aus SolutionCompakt>"
}
```

Passwort-Hashes dürfen nur über einen administrativen Bootstrap-/Sync-Prozess übertragen werden. Der Online-Login sucht zuerst die Firma über den Firmen-Code und danach den Benutzer innerhalb genau dieser Firma. Niemals Service-Account-Schlüssel oder Klartext-Passwörter in `public/`, GitHub oder die Desktop-Konfiguration legen.

## Datenmodell

`companies/{companyId}/weekPlans/{YYYY-Www}` enthält Metadaten. Darunter:

- `entries/{assignment-id}`: veröffentlichte Personaleinsätze aus SolutionCompakt
- `productionSlots/{run-id}`: Produktionsschichten
- `overrides/{assignment-id}`: ausschliesslich Online-Korrekturen von Administratoren

Normale Benutzer erhalten über Firestore Security Rules nur Leserechte. Schreibrechte für Overrides erfordern den Custom Claim `role == "Administrator"`. Die eigentliche Veröffentlichung läuft über die privilegierte Cloud Function und nicht direkt über Client-Schreibrechte.

## Sicherheit vor Produktivbetrieb

Vor echtem Betrieb zusätzlich konfigurieren: enge CORS-Origin statt `*`, Firebase App Check, Monitoring/Rate-Limit für den Login-Endpunkt, Backup/Retention sowie einen kontrollierten Benutzer-Sync aus SolutionCompakt.


## Verkäufer-Provisionierung und Offline-Betrieb

Die lokale App ist bereits auf einen stabilen Firmenmandanten vorbereitet. Aktuell kann eine vollständig neue Offline-Installation Firma + ersten Admin einmalig lokal registrieren. Für den späteren Verkaufsbetrieb ist die empfohlene strengere Variante:

1. Verkäufer legt Firma und ersten Admin im zentralen Provisioning an.
2. Online-Installation aktiviert sich einmalig über einen Aktivierungscode.
3. Offline-Installation importiert alternativ eine **digital signierte Aktivierungsdatei**.
4. Danach funktionieren Desktop-Login, Planung und lokale Benutzerverwaltung vollständig ohne Internet.
5. Sobald Internet vorhanden ist, werden freigegebene Benutzer-/Wochenplanänderungen synchronisiert.

Eine Offline-Aktivierungsdatei sollte erst produktiv aktiviert werden, wenn der Verkäufer-Signaturschlüssel eingerichtet ist. Ein im Programm eingebetteter gemeinsamer Geheimcode wäre kein ausreichender Manipulationsschutz.

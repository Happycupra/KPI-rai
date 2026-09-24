# SolutionCompakt Online-Wochenplan

Dieses Verzeichnis enthält die Firebase-Webanwendung für den SolutionCompakt-Wochenplan. Das produktive Web-Projekt `solution-compact` ist in `public/config.json` hinterlegt. Firebase-Webkonfigurationen sind Client-Konfiguration und keine Service-Account-Geheimnisse; Zugriffsschutz erfolgt über Authentication, Security Rules und später App Check.

## Zielbild

- Anmeldung mit **Firmen-Code + Benutzername + Passwort**
- angemeldete Nicht-Administratoren: Wochenplan **nur ansehen**
- Administratoren: ansehen, JSON-Wochenplan veröffentlichen und minimale **Online-Korrekturen**
- lokale SolutionCompakt-Planung bleibt führend
- Online-Korrekturen liegen separat in `weekPlans/{weekId}/overrides` und verändern die Desktop-Daten nicht
- Benutzername und Passwort können später identisch zur Desktop-App bleiben: der Cloud-Function-Endpunkt prüft die bestehenden PBKDF2-SHA256-Hashes (150000 Iterationen) und erzeugt ein Firebase Custom Token mit Firma und Rolle als Claims

## Produktiver Firebase-Stand

Das Firebase-Webprojekt ist bereits konfiguriert:

- Project ID: `solution-compact`
- Auth Domain: `solution-compact.firebaseapp.com`
- Hosting-Ziel: `https://solution-compact.web.app`
- Functions-Region: `europe-west1`
- Login-Function: `login`
- Publish-Function: `publishWeekPlan`

Für den tatsächlichen Deploy fehlt nur noch eine berechtigte Firebase/Google-Cloud-Anmeldung. Im Repository ist dafür `.github/workflows/firebase-deploy.yml` vorbereitet. Der Workflow erwartet das GitHub Secret `FIREBASE_SERVICE_ACCOUNT_SOLUTION_COMPACT`. Dieses Secret darf niemals in Quellcode, Chat oder öffentliche Dateien kopiert werden.

Nach dem ersten Deploy:

1. Firestore, Authentication, Hosting und Functions im Projekt müssen aktiviert sein.
2. Die Firma wird unter `companies/{companyId}` provisioniert.
3. Mindestens der erste Firmen-Administrator wird unter `companies/{companyId}/authUsers` synchronisiert.
4. In SolutionCompakt wird **Online-Wochenplan aktivieren** eingeschaltet.
5. Ein Wochenplan-Paket kann veröffentlicht werden.

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


## GitHub Deployment

Der Workflow `.github/workflows/firebase-deploy.yml` validiert Web-Dateien und Functions, installiert Firebase CLI und deployt Hosting, Firestore Rules und Functions in das Projekt `solution-compact`.

Für CI/CD wird Application Default Credentials über ein GitHub Secret verwendet. Empfohlen ist ein dediziertes Deployment-Servicekonto mit nur den notwendigen Rollen. Nach dem Deploy wird die temporäre Credentials-Datei auf dem Runner entfernt.

# Firmenregistrierung, Mandanten und Offline-Betrieb

## Zielbild

SolutionCompakt verwendet pro Installation genau **eine Firma / einen Mandanten**.

Jede Firma besitzt dauerhaft:

- `CompanyId`: technische, unveränderliche Mandanten-ID
- `CompanyCode`: lesbarer Firmen-Code für den Online-Login, z. B. `MUSTER-AG`
- `CompanyName`: anzeigbarer Firmenname
- Registrierungsmodus und Registrierungszeitpunkt

Benutzer werden nicht global, sondern innerhalb dieser Firma betrachtet. Lokal ist die SQLite-Datenbank bereits genau einer Firma zugeordnet. Online werden Daten unter `companies/{companyId}/...` getrennt.

## Empfohlener Verkaufsprozess

Für den Produktivvertrieb ist **Seller Managed Provisioning** die empfohlene Variante:

1. Verkäufer legt die Firma in einem zentralen Provisioning-System an.
2. Verkäufer erzeugt den ersten Firmen-Administrator.
3. Die Firma erhält einen eindeutigen Firmen-Code.
4. Die neue Installation wird einmalig aktiviert.
5. Danach verwaltet der Firmen-Administrator seine Benutzer selbst.
6. Neue lokale Benutzer werden bei bestehender Internetverbindung in den Firmenmandanten synchronisiert.
7. Die lokale Desktop-App bleibt auch nach erfolgreicher Aktivierung vollständig offline nutzbar.

Eine freie öffentliche Selbstregistrierung von Firmen ist für den Verkaufsbetrieb nicht vorgesehen.

## Online-Aktivierung

Bevorzugter Ablauf bei Internetzugang:

- Kunde erhält Aktivierungscode / Einladungslink.
- SolutionCompakt sendet den Code an den späteren Provisioning-Endpunkt.
- Der Endpunkt liefert Firmen-ID, Firmen-Code, Firmenname und den vorbereiteten ersten Admin zurück.
- Die Installation speichert diese Identität lokal.
- Weitere Logins laufen lokal gegen SQLite; Firebase wird nur für Online-Funktionen und Synchronisation benötigt.

Vorteile:

- einfache Sperrung / Lizenzsteuerung
- eindeutige Firma
- kein manueller Dateitransfer
- erster Admin wird wirklich vom Verkäufer vorgegeben

## Offline-Aktivierung

Wenn die Installation beim ersten Start kein Internet hat, sollte der Verkäufer eine Datei wie
`Muster-AG.sccompany` bereitstellen.

Die Datei soll später enthalten:

- Schema-Version
- CompanyId
- CompanyCode
- CompanyName
- erster Admin / Rolle
- Aktivierungs-/Lizenz-Metadaten
- Ablaufdatum der Aktivierungsdatei
- eindeutige Paket-ID
- digitale Signatur

**Wichtig:** Die Offline-Datei muss asymmetrisch digital signiert werden. Der private Verkäufer-Schlüssel bleibt ausschliesslich beim Verkäufer / Provisioning-Backend. In der ausgelieferten App befindet sich nur der öffentliche Prüfschlüssel. Ein fest im Programm eingebauter gemeinsamer Geheimcode wäre kein ausreichender Schutz.

Nach erfolgreichem Import arbeitet die App genauso wie bei einer Online-Aktivierung vollständig lokal.

## Bestehende Installationen

Bestehende SolutionCompakt-Datenbanken werden nicht gesperrt. Beim Upgrade wird einmalig:

- eine stabile CompanyId erzeugt,
- ein eindeutiger CompanyCode erzeugt,
- der Modus `LegacyMigration` gesetzt.

Benutzer, Passwörter und Produktionsdaten bleiben unverändert.

## Offline nach der Aktivierung

### Desktop-App

Die Desktop-App bleibt **offline-first**:

- Benutzeranmeldung lokal
- Rollen lokal
- Planung lokal
- Produktion / OEE lokal
- Nachrichten innerhalb derselben lokalen Datenbank lokal
- Backup / Export lokal

Die Cloud ist keine Voraussetzung zum Starten oder Arbeiten.

### Web-Wochenplan

Der Web-Wochenplan ist naturgemäss cloudbasiert. Für einen Offline-Lesemodus kann Firestore-Persistenz plus PWA-Service-Worker verwendet werden. Dabei soll:

- nur bereits zuvor geladener Wochenplan offline angezeigt werden,
- ein klarer Status `Offline · letzter synchronisierter Stand` sichtbar sein,
- Admin-Bearbeitung offline deaktiviert bleiben, um Konflikte zu vermeiden,
- persistenter Browser-Cache nur auf ausdrücklich als vertrauenswürdig bestätigten Geräten aktiviert werden.

Ein **erster** Web-Login auf einem neuen Gerät benötigt Internet, weil das Custom-Auth-Token vom Server erzeugt wird.

## Firmenbenutzer

Für den Produktivbetrieb wird keine öffentliche Benutzerregistrierung benötigt.

Empfohlen:

- Verkäufer erstellt Firma + ersten Admin.
- Firmen-Admin legt weitere Benutzer in SolutionCompakt an.
- Bei Onlineverbindung werden diese Benutzer kontrolliert in
  `companies/{companyId}/authUsers` synchronisiert.
- Firmen-Code + Benutzername + Passwort bilden den sichtbaren Online-Login.
- Derselbe Benutzername kann dadurch in mehreren Firmen existieren.
- Rollen werden als Firebase Custom Claims übernommen.

## Aktueller Implementierungsstand

Bereits umgesetzt:

- stabile CompanyId
- stabiler CompanyCode
- Firmenname bei lokaler Ersteinrichtung erforderlich
- bestehende Installationen werden automatisch migriert
- Firmen-Code wird im lokalen Login angezeigt
- Online-Wochenplan-Paket enthält CompanyId + CompanyCode
- Firebase Login verlangt Firmen-Code + Benutzername + Passwort
- Firestore-Struktur ist auf `companies/{companyId}/...` umgestellt
- Firestore Security Rules erzwingen Mandantentrennung
- veröffentlichte Wochenpläne können nicht zwischen Firmen vermischt werden

Noch für den echten Verkaufsbetrieb erforderlich:

- Verkäufer-/Provisioning-Backend
- produktiver Signaturschlüssel für Offline-Aktivierungsdateien
- Benutzer-Sync-Endpunkt
- Lizenz-/Abo-Regeln, falls gewünscht
- optionaler PWA-Offline-Lesemodus

# KPI-rai – Versionen und Releases

## Versionsschema

KPI-rai verwendet Semantic Versioning im Format `MAJOR.MINOR.PATCH`.

- `MAJOR`: inkompatible Änderungen an Datenmodell/Arbeitsweise
- `MINOR`: neue Funktionen bei grundsätzlich kompatibler Nutzung
- `PATCH`: Fehlerbehebungen und kleine Verbesserungen

Die aktuelle Basisversion ist `0.1.0`.

## Normaler Build

Jeder Push auf `main` baut automatisch:

1. die WPF-Anwendung,
2. einen self-contained Windows-x64-Publish inklusive .NET Runtime,
3. den Windows-Installer `KPI-rai-Setup-<Version>-win-x64.exe`.

Die Ergebnisse werden als GitHub-Actions-Artefakte bereitgestellt.

## Release erstellen

Ein Git-Tag im Format `vMAJOR.MINOR.PATCH` startet den Release-Workflow, z. B.:

```text
v0.1.0
```

Der Workflow erzeugt automatisch:

- den Windows-Installer,
- ein portables Windows-x64-ZIP,
- eine GitHub Release mit automatisch generierten Release Notes.

## Versionsanzeige

Die installierte Anwendung zeigt ihre Assembly-Version unten in der Hauptnavigation an.

## Hinweis zur Codesignatur

Der Installer ist aktuell technisch installierbar, aber noch nicht mit einem kommerziellen Windows-Code-Signing-Zertifikat signiert. Windows SmartScreen kann deshalb auf einzelnen PCs beim ersten Start eine Warnung anzeigen. Für eine unternehmensweite Verteilung sollte später eine Codesignatur ergänzt werden.

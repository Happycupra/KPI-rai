# SolutionCompakt – Versionen und Releases

## Versionsschema

SolutionCompakt verwendet Semantic Versioning im Format `MAJOR.MINOR.PATCH`.

- `MAJOR`: inkompatible Änderungen an Datenmodell/Arbeitsweise
- `MINOR`: neue Funktionen bei grundsätzlich kompatibler Nutzung
- `PATCH`: Fehlerbehebungen und kleine Verbesserungen

Die aktuelle Basisversion ist `0.1.0`.

## Normaler Build

Jeder Push auf `main` baut automatisch:

1. die WPF-Anwendung,
2. einen self-contained Windows-x64-Publish inklusive .NET Runtime,
3. eine portable Single-EXE und ein USB-Paket,
4. den Windows-Installer `SolutionCompakt-Setup-<Version>-win-x64.exe`.

Die Ergebnisse werden als GitHub-Actions-Artefakte bereitgestellt.

## Release erstellen

Ein Git-Tag im Format `vMAJOR.MINOR.PATCH` startet den Release-Workflow, z. B.:

```text
v0.1.0
```

Der Workflow erzeugt automatisch:

- den Windows-Installer,
- ein portables Windows-x64-ZIP,
- eine portable Single-EXE,
- ein USB-/Portable-Paket,
- eine GitHub Release mit automatisch generierten Release Notes.

## Versionsanzeige

Die installierte Anwendung zeigt ihre Assembly-Version unten in der Hauptnavigation an.

## Codesignatur

Der Build kann die Windows-EXE-Dateien und den Installer mit einem Authenticode-Zertifikat signieren. Dafür werden in GitHub Actions diese Repository-Secrets benötigt:

- `WINDOWS_SIGNING_PFX_BASE64`: PFX-Datei als Base64-Text
- `WINDOWS_SIGNING_PFX_PASSWORD`: Passwort der PFX-Datei

Wenn `WINDOWS_SIGNING_PFX_BASE64` nicht gesetzt ist, laufen Build und Release weiterhin ohne Signatur. Sobald das Secret vorhanden ist, werden signiert:

- `artifacts/win-x64/SolutionCompakt.exe`
- `artifacts/single-file/SolutionCompakt.exe`
- versionierte portable EXE im Release
- `artifacts/installer/SolutionCompakt-Setup-<Version>-win-x64.exe`

Ein PFX darf nicht in Git eingecheckt werden. Lokale Zertifikatsdateien mit `.pfx` oder `.p12` werden durch `.gitignore` ausgeschlossen.

PFX lokal in Base64 umwandeln:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\Pfad\codesigning.pfx")) | Set-Clipboard
```

Lokale Artefakte können mit demselben Skript signiert werden:

```powershell
.\scripts\Sign-WindowsArtifacts.ps1 `
  -CertificatePath "C:\Pfad\codesigning.pfx" `
  -CertificatePassword "<Passwort>" `
  -Paths @(
    "artifacts\win-x64\SolutionCompakt.exe",
    "artifacts\single-file\SolutionCompakt.exe",
    "artifacts\installer\*.exe"
  )
```

Für weniger SmartScreen-Warnungen im Firmennetzwerk sollte ein kommerzielles OV- oder EV-Code-Signing-Zertifikat verwendet werden. Eine selbstsignierte Testsignatur eignet sich nur für interne Tests auf Rechnern, auf denen das Zertifikat ausdrücklich vertraut wird.

# SolutionCompakt – Downloads

Im Ordner `downloads/` liegen die direkt abrufbaren Windows-Downloads.

Aktuelle Dateien:

- `SolutionCompakt-Setup-latest.zip` – jeweils aktueller Windows-Installer
- `SolutionCompakt-Portable-latest.zip` – jeweils aktuelle portable Single-EXE-Version

Archivierte/ältere Pakete bleiben zusätzlich im Ordner erhalten, z. B. die bisherigen `0.1.0`-Builds.

Der Workflow `.github/workflows/windows-build.yml` baut und testet SolutionCompakt auf `main`. Nach einem erfolgreichen Main-Build veröffentlicht `.github/workflows/publish-portable-download.yml` automatisch die aktuelle Portable-Version als `SolutionCompakt-Portable-latest.zip` in diesem Ordner.

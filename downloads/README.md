# SolutionCompakt – Downloads

Im Hauptordner `downloads/` liegen die vier aktuellen Windows-Build-Artefakte als ZIP-Dateien:

- `SolutionCompakt-Setup-0.1.0-win-x64.zip` – Windows-Installer
- `SolutionCompakt-Portable-0.1.0-win-x64.zip` – portable Single-EXE-Version
- `SolutionCompakt-USB-Portable-0.1.0-win-x64.zip` – USB-/Portable-Paket mit `portable.mode`
- `SolutionCompakt-Windows-0.1.0-win-x64.zip` – vollständiger Windows-x64-Build

Der Workflow `.github/workflows/windows-build.yml` erzeugt diese vier Pakete bei einem erfolgreichen Build auf `main` und aktualisiert die Dateien in diesem Ordner.

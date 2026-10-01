param(
    [Parameter(Mandatory = $false)]
    [string] $DataDirectory = (Join-Path $env:LOCALAPPDATA "Produktionsplanung")
)

$ErrorActionPreference = "Stop"
$path = Join-Path $DataDirectory "central-mode.json"
if (-not (Test-Path $path)) {
    Write-Host "Keine Zentralmodus-Konfiguration gefunden: $path"
    exit 0
}

$config = Get-Content $path -Raw | ConvertFrom-Json
$config.Enabled = $false
$config | ConvertTo-Json | Set-Content -Path $path -Encoding UTF8
Write-Host "Zentralbetrieb deaktiviert. SolutionCompakt nach dem Neustart wieder im lokalen SQLite-Modus: $path"

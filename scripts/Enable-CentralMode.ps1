param(
    [Parameter(Mandatory = $true)]
    [string] $PostgresPassword,

    [Parameter(Mandatory = $false)]
    [string] $ServerHost = "127.0.0.1",

    [Parameter(Mandatory = $false)]
    [int] $PostgresPort = 5432,

    [Parameter(Mandatory = $false)]
    [string] $ServerUrl = "http://127.0.0.1:8088",

    [Parameter(Mandatory = $false)]
    [string] $Database = "solutioncompakt",

    [Parameter(Mandatory = $false)]
    [string] $DatabaseUser = "solutioncompakt",

    [Parameter(Mandatory = $false)]
    [string] $DataDirectory = (Join-Path $env:LOCALAPPDATA "Produktionsplanung"),

    [Parameter(Mandatory = $false)]
    [switch] $RequireSsl,

    [Parameter(Mandatory = $false)]
    [switch] $NoAutoMigrate
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($PostgresPassword)) {
    throw "PostgresPassword darf nicht leer sein."
}
if ([string]::IsNullOrWhiteSpace($ServerHost)) {
    throw "ServerHost darf nicht leer sein."
}

New-Item -ItemType Directory -Path $DataDirectory -Force | Out-Null
$sslMode = if ($RequireSsl) { "Require" } else { "Disable" }
$connection = "Host=$ServerHost;Port=$PostgresPort;Database=$Database;Username=$DatabaseUser;Password=$PostgresPassword;SSL Mode=$sslMode;Timeout=10;Command Timeout=30"

$config = [ordered]@{
    Enabled = $true
    DatabaseConnectionString = $connection
    ServerUrl = $ServerUrl.TrimEnd('/')
    AutoMigrateLocalData = -not $NoAutoMigrate
}

$path = Join-Path $DataDirectory "central-mode.json"
$config | ConvertTo-Json | Set-Content -Path $path -Encoding UTF8

Write-Host "Zentralbetrieb aktiviert: $path"
Write-Host "PostgreSQL: $ServerHost`:$PostgresPort / $Database"
Write-Host "Realtime-Server: $($config.ServerUrl)"
Write-Host "SolutionCompakt jetzt neu starten. Beim ersten Zentralstart werden lokale Daten automatisch übernommen, falls das Firmenschema auf dem Server noch leer ist."

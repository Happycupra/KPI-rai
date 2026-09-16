$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'src/Produktionsplanung.App/Assets/OpsCompact.ico.b64'
$target = Join-Path $repoRoot 'src/Produktionsplanung.App/Assets/OpsCompact.ico'

if (-not (Test-Path $source)) {
    throw "OpsCompact icon source not found: $source"
}

$base64 = ((Get-Content $source -Raw) -replace '\s', '').Trim()
$padding = (4 - ($base64.Length % 4)) % 4
if ($padding -gt 0) {
    $base64 += ('=' * $padding)
}

$bytes = [Convert]::FromBase64String($base64)
if ($bytes.Length -lt 4 -or $bytes[0] -ne 0 -or $bytes[1] -ne 0 -or $bytes[2] -ne 1 -or $bytes[3] -ne 0) {
    throw 'Decoded OpsCompact icon is not a valid ICO file.'
}

$directory = Split-Path -Parent $target
New-Item -ItemType Directory -Path $directory -Force | Out-Null
[IO.File]::WriteAllBytes($target, $bytes)
Write-Host "Generated $target ($($bytes.Length) bytes)"

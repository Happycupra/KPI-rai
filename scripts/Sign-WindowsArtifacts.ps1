param(
    [Parameter(Mandatory = $true)]
    [string] $CertificatePath,

    [Parameter(Mandatory = $false)]
    [string] $CertificatePassword,

    [Parameter(Mandatory = $true)]
    [string[]] $Paths,

    [Parameter(Mandatory = $false)]
    [string] $TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

function Resolve-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $windowsKits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path $windowsKits) {
        $candidate = Get-ChildItem $windowsKits -Recurse -Filter signtool.exe |
            Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($candidate) {
            return $candidate.FullName
        }
    }

    throw "signtool.exe wurde nicht gefunden. Installiere das Windows SDK oder nutze einen GitHub Windows Runner."
}

if (-not (Test-Path $CertificatePath)) {
    throw "Signaturzertifikat wurde nicht gefunden: $CertificatePath"
}

$signTool = Resolve-SignTool
$existingPaths = @()

foreach ($path in $Paths) {
    $matches = Get-ChildItem -Path $path -File -ErrorAction SilentlyContinue
    foreach ($match in $matches) {
        $existingPaths += $match.FullName
    }
}

if ($existingPaths.Count -eq 0) {
    throw "Keine signierbaren Artefakte gefunden."
}

foreach ($artifact in $existingPaths | Sort-Object -Unique) {
    Write-Host "Signiere $artifact"

    $arguments = @(
        "sign",
        "/fd", "SHA256",
        "/tr", $TimestampUrl,
        "/td", "SHA256",
        "/f", $CertificatePath
    )

    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $arguments += @("/p", $CertificatePassword)
    }

    $arguments += $artifact

    & $signTool @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Codesignatur fehlgeschlagen: $artifact"
    }
}

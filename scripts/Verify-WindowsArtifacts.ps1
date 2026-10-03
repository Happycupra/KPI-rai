param([Parameter(Mandatory = $true)][string[]] $Paths)
$ErrorActionPreference = "Stop"

$command = Get-Command signtool.exe -ErrorAction SilentlyContinue
$signTool = if ($command) { $command.Source } else {
    $windowsKits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    Get-ChildItem $windowsKits -Recurse -Filter signtool.exe |
        Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $signTool) { throw "signtool.exe wurde nicht gefunden." }
if ($Paths.Count -eq 0) { throw "Keine Artefakte zur Prüfung angegeben." }
foreach ($path in $Paths) {
    $matches = @(Get-ChildItem -Path $path -File -ErrorAction SilentlyContinue)
    if ($matches.Count -eq 0) { throw "Erwartetes Artefakt fehlt: $path" }
    foreach ($artifact in $matches) {
        & $signTool verify /pa /all $artifact.FullName
        if ($LASTEXITCODE -ne 0) { throw "Ungültige oder nicht vertrauenswürdige Signatur: $($artifact.FullName)" }
        $signature = Get-AuthenticodeSignature -LiteralPath $artifact.FullName
        if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate) {
            throw "Gültige Authenticode-Signatur mit Zeitstempel erforderlich: $($artifact.FullName)"
        }
    }
}

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
function Expect-Failure([scriptblock] $Run, [string] $Message) {
    $failed = $false
    try { & $Run } catch { $failed = $true; Write-Host "PASS: $Message" }
    if (-not $failed) { throw "Expected rejection: $Message" }
}
$temp = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    Expect-Failure { & "$root/scripts/Sign-WindowsArtifacts.ps1" -CertificatePath "$temp/missing.pfx" -Paths @("$temp/missing.exe") } 'Missing certificate'
    Set-Content "$temp/test.pfx" 'Not a certificate'
    Set-Content "$temp/present.exe" 'Not an executable'
    Expect-Failure { & "$root/scripts/Sign-WindowsArtifacts.ps1" -CertificatePath "$temp/test.pfx" -Paths @("$temp/present.exe", "$temp/missing.exe") } 'One missing artifact must abort the whole signing request'
    Expect-Failure { & "$root/scripts/Verify-WindowsArtifacts.ps1" -Paths @("$temp/missing.exe") } 'Missing verification target'
    # The regression-test apphost is a real, unsigned PE executable.
    $unsignedExe = Join-Path $temp 'unsigned.exe'
    Copy-Item "$root/tests/Produktionsplanung.RegressionTests/bin/Release/net8.0-windows/Produktionsplanung.RegressionTests.exe" $unsignedExe
    Expect-Failure { & "$root/scripts/Verify-WindowsArtifacts.ps1" -Paths @($unsignedExe) } 'Unsigned executable cannot be published'
} finally { Remove-Item $temp -Recurse -Force }

# Expected native verification failures must not leak into the Actions shell exit.
$global:LASTEXITCODE = 0

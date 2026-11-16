$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
foreach ($name in 'build.ps1','tools/release-windows.ps1','tools/publish-local-release.ps1','tools/package-windows.ps1','tools/setup-accept.ps1','tools/write-install-report.ps1') {
    $tokens = $null; $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $root $name), [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw ($name + ': ' + ($errors.Message -join '; ')) }
}
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('deskbox-release-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$fixtureState = @{ Requests = (New-Object 'Collections.Generic.List[object]'); FailUpload = $false }
# Inherited by the publisher's child scope. No actual gh process or network request is used.
$ghMock = {
    $fixtureState.Requests.Add(@($args))
    $global:LASTEXITCODE = 0
    if ($fixtureState.FailUpload -and $args[0] -eq 'release') { $global:LASTEXITCODE = 1 }
}.GetNewClosure()
Set-Item Function:\gh -Value $ghMock
function Expect-Blocked([scriptblock]$Action) {
    $blocked = $false
    try { & $Action } catch { $blocked = $true }
    if (-not $blocked) { throw 'Expected the publisher to reject an invalid fixture.' }
}
try {
    $payload = Join-Path $fixture 'candidate.zip'
    'isolated fixture, not an application' | Set-Content -LiteralPath $payload
    'Fixture notes' | Set-Content -LiteralPath (Join-Path $fixture 'RELEASE-NOTES.md')
    $manifest = @{ Product='DeskBox'; Version='2.1.0-fixture'; SourceSHA=('a'*40); Dirty=$false; NativeAndInstallerVerified=$false }
    $manifestPath = Join-Path $fixture 'BUILD-MANIFEST.json'
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath
    function Write-Checksums {
        $lines = foreach ($name in 'candidate.zip','RELEASE-NOTES.md','BUILD-MANIFEST.json') {
            (Get-FileHash -LiteralPath (Join-Path $fixture $name) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name
        }
        $lines | Set-Content -LiteralPath (Join-Path $fixture 'SHA256SUMS.txt')
    }
    Write-Checksums
    if ((Get-Command gh).CommandType -ne 'Function') { throw 'Fixture gh stub is not active.' }
    & (Join-Path $root 'tools/publish-local-release.ps1') -Directory $fixture -Repo fixture-only-do-not-publish/fixture-only
    if ($fixtureState.Requests.Count -ne 2 -or $fixtureState.Requests[1] -notcontains '--draft' -or $fixtureState.Requests[1] -contains '--clobber') { throw 'The candidate must remain a draft and must not overwrite published assets.' }
    $fixtureState.Requests.Clear()
    'tampered bytes' | Add-Content -LiteralPath $payload
    Expect-Blocked { & (Join-Path $root 'tools/publish-local-release.ps1') -Directory $fixture }
    if ($fixtureState.Requests.Count) { throw 'Tampered packages reached gh.' }
    Write-Checksums; $manifest.Dirty=$true; $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath
    Expect-Blocked { & (Join-Path $root 'tools/publish-local-release.ps1') -Directory $fixture }
    if ($fixtureState.Requests.Count) { throw 'A dirty source checkout reached gh.' }
    $manifest.Dirty=$false; $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath; Write-Checksums
    $fixtureState.FailUpload=$true
    Expect-Blocked { & (Join-Path $root 'tools/publish-local-release.ps1') -Directory $fixture -Repo fixture-only-do-not-publish/fixture-only }
    if (-not (Test-Path -LiteralPath $payload)) { throw 'Upload failure deleted the local package.' }
    $packageRoot = Join-Path $fixture 'packaging-fixture'
    foreach ($name in 'tools','dist','packaging') { New-Item -ItemType Directory -Path (Join-Path $packageRoot $name) | Out-Null }
    Copy-Item -LiteralPath (Join-Path $root 'tools/package-windows.ps1') -Destination (Join-Path $packageRoot 'tools/package-windows.ps1')
    'fixture' | Set-Content -LiteralPath (Join-Path $packageRoot 'packaging/DeskBox.iss')
    Add-Type -TypeDefinition '[assembly:System.Reflection.AssemblyFileVersion("99.3.7.4")][assembly:System.Reflection.AssemblyInformationalVersion("99.3.7-fixture")] public class DeskBoxVersionFixture {}' -OutputAssembly (Join-Path $packageRoot 'dist/DeskBox.exe')
    @'
param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Values)
$Values | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'arguments.json')
$release = ($Values | Where-Object { $_ -like '/DReleaseVersion=*' }).Substring(17)
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'dist/installer') | Out-Null
'fixture installer, not executable' | Set-Content -LiteralPath (Join-Path $PSScriptRoot ('dist/installer/DeskBox-'+$release+'-Setup.exe'))
$global:LASTEXITCODE=0
'@ | Set-Content -LiteralPath (Join-Path $packageRoot 'iscc-fixture.ps1')
    & (Join-Path $packageRoot 'tools/package-windows.ps1') -Iscc (Join-Path $packageRoot 'iscc-fixture.ps1')
    $options = Get-Content -LiteralPath (Join-Path $packageRoot 'arguments.json') -Raw | ConvertFrom-Json
    if ($options -notcontains '/DFileVersion=99.3.7.4' -or $options -notcontains '/DReleaseVersion=99.3.7-fixture') { throw 'Installer versions did not follow the compiled fixture metadata.' }
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot 'dist/installer/DeskBox-99.3.7-fixture-Setup.exe'))) { throw 'Versioned package was not produced.' }
    Write-Output 'PASS local release checks: PowerShell parsing, draft-only upload, checksum tamper rejection, dirty-source rejection, upload failure retains files, installer version follows PE metadata; fake gh/ISCC, no network.'
} finally { Remove-Item -LiteralPath $fixture -Recurse -Force }

param([Parameter(Mandatory=$true)][string]$Directory, [string]$Repo = 'zong-technology/DeskBox')
$ErrorActionPreference = 'Stop'
$folder = (Resolve-Path -LiteralPath $Directory).Path
$manifest = Get-Content -LiteralPath (Join-Path $folder 'BUILD-MANIFEST.json') -Raw | ConvertFrom-Json
if ($manifest.Product -ne 'DeskBox' -or $manifest.Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$' -or $manifest.SourceSHA -notmatch '^[0-9a-f]{40}$' -or $manifest.Dirty -ne $false) { throw 'Publish requires a clean Git checkout and a valid DeskBox build manifest.' }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Install GitHub CLI and run gh auth login on this build machine first.' }
$files = @()
foreach ($line in Get-Content -LiteralPath (Join-Path $folder 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([0-9a-f]{64})  ([^/\\]+)$') { throw 'Invalid checksum entry.' }
    $expected = $Matches[1]; $file = Join-Path $folder $Matches[2]
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw ('Checksum mismatch: ' + $file) }
    $files += $file
}
$files += Join-Path $folder 'SHA256SUMS.txt'
& gh auth status
if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI authentication is required; keep credentials on this computer.' }
$tag = 'v' + $manifest.Version
# A draft remains private to repository writers if an attachment upload fails. Never overwrite a published tag.
& gh release create $tag @files --repo $Repo --target $manifest.SourceSHA --draft --prerelease --title ('DeskBox ' + $manifest.Version) --notes-file (Join-Path $folder 'RELEASE-NOTES.md')
if ($LASTEXITCODE -ne 0) { throw 'Release upload failed. Local packages are intact; inspect any partial draft in GitHub Releases before retrying.' }
Write-Host 'Draft uploaded. Review the manifest and complete target-device testing before publishing it.' -ForegroundColor Green

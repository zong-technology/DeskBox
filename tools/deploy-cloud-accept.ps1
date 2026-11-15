$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('deskbox-deploy-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'tools'),(Join-Path $fixture 'cloud\deskbox-worker') -Force | Out-Null
$script = Join-Path $fixture 'tools\deploy.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'deploy-deskbox-cloud.ps1') -Destination $script
$global:cloudCommands = New-Object Collections.Generic.List[string]
$global:cloudPasswordLength = 0
function global:npx.cmd {
    $command = $args -join ' '; $global:cloudCommands.Add($command); $global:LASTEXITCODE = 0
    if ($command -match 'd1 list --json') { '[{"name":"deskbox-sync-data","uuid":"12345678-1234-1234-1234-123456789abc"}]' }
    if ($command -match 'secret put') { $input | Out-Null }
}
function global:Set-Clipboard { param($Value) $global:cloudPasswordLength = $Value.Length }
& $script | Out-Null
$config = Get-Content -LiteralPath (Join-Path $fixture 'cloud\deskbox-worker\.wrangler.generated.json') -Raw | ConvertFrom-Json
if ($config.d1_databases[0].database_id -ne '12345678-1234-1234-1234-123456789abc') { throw 'Existing database was not reused' }
if ($global:cloudCommands -match 'd1 create') { throw 'Existing database was recreated' }
if ($global:cloudPasswordLength -ne 64) { throw 'Random login password was not copied' }
if (-not ($global:cloudCommands -match 'd1 execute deskbox-sync-data --remote --file schema.sql')) { throw 'Schema command lost its arguments' }
if (-not ($global:cloudCommands -match 'secret put DESKBOX_OWNER_PASSWORD --config')) { throw 'Secret command missing' }
$first = @($global:cloudCommands | Where-Object { $_ -match 'secret put' }).Count
& $script | Out-Null
if (@($global:cloudCommands | Where-Object { $_ -match 'secret put' }).Count -ne $first) { throw 'An upgrade unexpectedly rotated the login password' }
& $script -ResetPassword | Out-Null
if (@($global:cloudCommands | Where-Object { $_ -match 'secret put' }).Count -ne $first + 1) { throw 'Explicit password reset failed' }
Write-Output 'Deploy orchestration: PASS 7 (mock CLI; no network, cloud resource or clipboard changes)'

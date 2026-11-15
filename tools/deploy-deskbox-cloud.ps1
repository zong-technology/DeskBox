param([ValidatePattern('^[a-z][a-z0-9-]{2,40}$')][string]$Name = 'deskbox-sync', [switch]$ResetPassword)
$ErrorActionPreference = 'Stop'
$workerRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\cloud\deskbox-worker'))
$configPath = Join-Path $workerRoot '.wrangler.generated.json'
$statePath = Join-Path $workerRoot '.deployment-state.json'
if (-not (Get-Command node -ErrorAction SilentlyContinue) -or -not (Get-Command npx.cmd -ErrorAction SilentlyContinue)) { throw 'Install Node.js 22 or later first: https://nodejs.org/' }
function Invoke-Wrangler([string[]]$Parameters) {
    & npx.cmd --yes wrangler@4.92.0 @Parameters
    if ($LASTEXITCODE -ne 0) { throw 'Cloudflare command failed. Existing task/project data was not deleted.' }
}
Push-Location $workerRoot
try {
    Write-Host 'Cloudflare will open its official login/authorization page. Use your own free account.'
    Invoke-Wrangler -Parameters @('login')
    $databaseName = $Name + '-data'
    $rawList = & npx.cmd --yes wrangler@4.92.0 d1 list --json
    if ($LASTEXITCODE -ne 0) { throw 'Cannot list databases. Complete Cloudflare login first.' }
    $databases = ($rawList -join "`n") | ConvertFrom-Json
    $database = @($databases | Where-Object { $_.name -eq $databaseName }) | Select-Object -First 1
    if ($database) { $databaseId = $database.uuid }
    else {
        $created = & npx.cmd --yes wrangler@4.92.0 d1 create $databaseName --location apac --update-config=false
        if ($LASTEXITCODE -ne 0) { throw 'Database creation failed; rerun to discover and reuse an existing database.' }
        $outputText = $created -join "`n"
        if ($outputText -notmatch 'database_id["\s:=]+([0-9a-fA-F-]{36})') { throw 'Database created, but its ID was not parsed. Rerun; the database list will reuse it.' }
        $databaseId = $Matches[1]
    }
    if ($databaseId -notmatch '^[0-9a-fA-F-]{36}$') { throw 'Unexpected D1 database ID.' }
    $config = @{ name=$Name; main='index.ts'; compatibility_date='2026-11-01'; workers_dev=$true; d1_databases=@(@{binding='DB';database_name=$databaseName;database_id=$databaseId}) }
    [IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    Invoke-Wrangler -Parameters @('d1','execute',$databaseName,'--remote','--file','schema.sql','--config',$configPath,'--yes')
    Invoke-Wrangler -Parameters @('deploy','--config',$configPath)
    $state = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else { $null }
    if ($ResetPassword -or -not $state -or $state.name -ne $Name -or -not $state.passwordConfigured) {
        $bytes = New-Object byte[] 32; $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
        $password = [BitConverter]::ToString($bytes).Replace('-','').ToLowerInvariant()
        $password | & npx.cmd --yes wrangler@4.92.0 secret put DESKBOX_OWNER_PASSWORD --config $configPath
        if ($LASTEXITCODE -ne 0) { throw 'Secret configuration failed. Rerun before using the service.' }
        Invoke-Wrangler -Parameters @('d1','execute',$databaseName,'--remote','--command','DELETE FROM admin_sessions;','--config',$configPath,'--yes')
        Set-Clipboard -Value $password
        $password = $null; [Array]::Clear($bytes,0,$bytes.Length)
        [IO.File]::WriteAllText($statePath, (@{name=$Name;passwordConfigured=$true} | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
        Write-Host 'Private cloud login: owner. The generated 64-character password is now on your clipboard.' -ForegroundColor Cyan
        Write-Host 'Save it in your password manager now. It is not stored in the project files.'
    }
    Write-Host 'Open the workers.dev URL printed above. Log in as owner; authorize Windows and iPhone separately.' -ForegroundColor Green
    Write-Host 'To rotate a lost login password later, run this script with -ResetPassword. Device revocation is separate.'
} finally { Pop-Location }

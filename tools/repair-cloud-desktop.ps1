#Requires -Version 7.0
param([string]$DataDir='D:\DeskBox\data',[string]$ServiceUrl='https://deskbox-sync.arthur98.workers.dev')
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$config=Join-Path $project 'dist\DeskBox-0.3.1-cloud-setup\cloud\deskbox-worker\.wrangler.generated.json'
$cli=Join-Path $project 'cloud\deskbox-account-sync\node_modules\wrangler\wrangler-dist\cli.js'
$credentialPath=Join-Path $DataDir 'cloud-login.enc'
if(Test-Path -LiteralPath $credentialPath){throw 'Desktop credential already exists; refusing to replace it.'}
$origin=[Uri]$ServiceUrl
if($origin.Scheme -ne 'https' -or $origin.AbsolutePath -ne '/' -or $origin.UserInfo -or $origin.Query){throw 'Expected HTTPS cloud service origin'}
$id=[Guid]::NewGuid().ToString()
$bytes=New-Object byte[] 32;$rng=[Security.Cryptography.RandomNumberGenerator]::Create()
try{$rng.GetBytes($bytes)}finally{$rng.Dispose()}
$token=[BitConverter]::ToString($bytes).Replace('-','').ToLowerInvariant()
$sha=[Security.Cryptography.SHA256]::Create()
try{$hash=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($token))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
$now=[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds();$expires=$now+365L*86400000L
$name=[Environment]::MachineName.Replace("'","''")
$sql="INSERT INTO devices (id,owner,name,role,hash,expires,revoked,created) SELECT '$id','owner','$name','desktop','$hash',$expires,0,$now WHERE NOT EXISTS (SELECT 1 FROM devices WHERE owner='owner' AND role='desktop' AND revoked=0 AND expires>$now);"
$result=& node $cli d1 execute deskbox-sync-data --remote --command $sql --config $config --json
if($LASTEXITCODE -ne 0){throw 'Cloud enrollment failed; local credential was not written'}
$response=($result -join "`n")|ConvertFrom-Json
if($response[0].meta.changes -ne 1){throw 'An active desktop already exists; no credentials changed'}
try{
    Add-Type -AssemblyName System.Security
    $json=@{BaseUrl=$origin.GetLeftPart([UriPartial]::Authority);DeviceId=$id;Token=$token;GateToken='direct-worker';Role='desktop';ExpiresAt=$expires}|ConvertTo-Json -Compress
    $protected=[Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($json),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    $encoded=ConvertTo-Json -InputObject ([Convert]::ToBase64String($protected)) -Compress
    [IO.File]::WriteAllText($credentialPath+'.tmp',$encoded,(New-Object Text.UTF8Encoding($false)))
    [IO.File]::Move($credentialPath+'.tmp',$credentialPath)
    [IO.File]::WriteAllText((Join-Path $DataDir 'cloud-service-url.json'),(ConvertTo-Json -InputObject $origin.GetLeftPart([UriPartial]::Authority) -Compress),(New-Object Text.UTF8Encoding($false)))
    Write-Output 'PASS: missing desktop enrolled; encrypted login saved for the current Windows user. Restart DeskBox to load it.'
}catch{
    if(-not(Test-Path -LiteralPath $credentialPath)){$null=& node $cli d1 execute deskbox-sync-data --remote --command "UPDATE devices SET revoked=1 WHERE id='$id';" --config $config --json}
    throw
}finally{$token=$null;$json=$null;[Array]::Clear($bytes,0,$bytes.Length)}

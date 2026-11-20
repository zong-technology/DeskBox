# Run using Windows PowerShell 5.1 on a licensed, isolated Windows test machine.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ClosedOfflineProject,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [string]$DeskBoxExe = 'dist\DeskBox.exe',
    [string]$TargetCpu,
    [switch]$Compile,
    [string]$ApprovedNewScaleBlock,
    [string]$ApprovedSclFile,
    [string]$ApprovedSclSha256,
    [string[]]$ExpectedDevice = @(),
    [string[]]$ExpectedBlock = @(),
    [string[]]$ExpectedTag = @()
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Use Windows PowerShell 5.1 and .NET Framework 4.8.' }
if (($Compile -or $ApprovedNewScaleBlock -or $ApprovedSclFile) -and -not $TargetCpu) { throw 'Set -TargetCpu to the exact PLC path returned by an inspection.' }
if (($Compile -and ($ApprovedNewScaleBlock -or $ApprovedSclFile)) -or ($ApprovedNewScaleBlock -and $ApprovedSclFile)) { throw 'Select only one compile or insertion action.' }
if ($ApprovedSclFile -and -not $ApprovedSclSha256) { throw 'Pass the SHA-256 of the exact SCL text reviewed and approved for insertion.' }
[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $DeskBoxExe).Path) | Out-Null
$folder = [DeskBox.Engineering.Tia.PlcEngineering]::FullLocalPath($EvidenceDirectory)
if (Test-Path -LiteralPath $folder) { throw 'EvidenceDirectory must be a new directory for this run.' }
[IO.Directory]::CreateDirectory($folder) | Out-Null
$envInfo = [DeskBox.Engineering.Tia.TiaEnvironmentInspector]::Inspect()
$envInfo.Describe() | Write-Output
$envInfo.Describe() | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $folder 'environment.txt')
if (-not ($envInfo.Installations | Where-Object { $_.Version -eq 20 -and $_.AssemblyPath }) -or -not $envInfo.OpennessGroup) {
    'BLOCKED: V20 PublicAPI and the active Siemens TIA Openness group are required.' | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $folder 'status.txt')
    throw 'BLOCKED: V20 PublicAPI and the active Siemens TIA Openness group are required.'
}
[DeskBox.Core.AppPaths]::DataDir = $folder
$job = New-Object DeskBox.Engineering.Tia.TiaJob
$job.ProjectFile = [DeskBox.Engineering.Tia.PlcEngineering]::FullLocalPath($ClosedOfflineProject)
$token = [Threading.CancellationToken]::None
$sourceFolder = Split-Path -LiteralPath $job.ProjectFile
$sourceBefore = [DeskBox.Engineering.Tia.TiaOpenness]::CopyTree($sourceFolder, $null, $token)
$inspection = [DeskBox.Engineering.Tia.TiaOpenness]::Run($job, $token)
$inspection.Report | Write-Output
if ($inspection.Error) { throw $inspection.Error }
if (-not $inspection.Snapshot -or -not $inspection.Cpus.Count) { throw 'The selected project has no enumerated PLC.' }
$inspection.Cpus | ForEach-Object { Write-Output ('PLC: ' + $_) }
function Test-ExpectedObject([string[]]$Names, [string[]]$Kinds) {
    foreach ($name in $Names) {
        $match = @($inspection.Snapshot.Objects | Where-Object { $_.Kind -in $Kinds -and $_.Name -ceq $name })
        if ($match.Count -ne 1) { throw ('Expected one ' + ($Kinds -join '/') + ' named ' + $name + '; got ' + $match.Count) }
        Write-Output ('Checked: ' + $match[0].Identity + ' (' + $match[0].Source + ')')
    }
}
Test-ExpectedObject $ExpectedDevice @('Device', 'DeviceItem')
Test-ExpectedObject $ExpectedBlock @('FB', 'FC', 'OB', 'GlobalDB', 'InstanceDB')
Test-ExpectedObject $ExpectedTag @('PlcTag')
$summary = [ordered]@{
    Project = $inspection.Snapshot.Name
    OpennessVersion = $inspection.OpennessVersion
    Cpus = $inspection.Cpus
    ExpectedDevices = $ExpectedDevice
    ExpectedBlocks = $ExpectedBlock
    ExpectedTags = $ExpectedTag
    Inventory = @($inspection.Snapshot.Objects | Group-Object Kind | Select-Object Name, Count)
    Compile = 'NotRun'
    SavedCopyReopened = $false
    SnapshotRestored = $false
    SourceUnchanged = $false
}
try {
if ($Compile -or $ApprovedNewScaleBlock -or $ApprovedSclFile) {
    if ($inspection.Cpus -cnotcontains $TargetCpu) { throw 'TargetCpu does not match a PLC in the inspected source.' }
    $job.Cpu = $TargetCpu
    $job.ExpectedSource = $inspection.SourceHash
    if ($ApprovedNewScaleBlock) {
        $job.Operation = 'scale'
        $job.Block = $ApprovedNewScaleBlock
        $approvedSource = [DeskBox.Engineering.Tia.PlcEngineering]::AnalogScale($ApprovedNewScaleBlock)
        $job.ApprovedHash = [DeskBox.Engineering.Tia.PlcEngineering]::Hash($approvedSource)
    } elseif ($ApprovedSclFile) {
        $draft = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $ApprovedSclFile).Path)
        $job = [DeskBox.Engineering.Tia.TiaOpenness]::PrepareSclInsertion($job.ProjectFile, $TargetCpu, $inspection.SourceHash, $draft, '')
        if ($job.ApprovedHash -cne $ApprovedSclSha256.ToLowerInvariant()) { throw 'Reviewed SCL hash does not match the supplied draft.' }
    } else { $job.Operation = 'compile' }
    $result = [DeskBox.Engineering.Tia.TiaOpenness]::Run($job, $token)
    $result.Report | Write-Output
    $summary.Compile = $result.CompileState
    if ($result.Saved -or $result.WriteStarted) {
        $operationFolder = Split-Path -LiteralPath (Split-Path -LiteralPath $result.Copy)
        $receipt = [DeskBox.Engineering.Tia.TiaOpenness]::ReadRecoveryReceipt((Join-Path $operationFolder 'result.json'))
        $restored = [DeskBox.Engineering.Tia.TiaOpenness]::RestoreSnapshot($receipt, $token)
        $restoredManifest = [DeskBox.Engineering.Tia.TiaOpenness]::CopyTree((Split-Path -LiteralPath $restored), $null, $token)
        if ((Compare-Object -ReferenceObject $sourceBefore -DifferenceObject $restoredManifest -CaseSensitive)) { throw 'Restored copy does not match the original project files.' }
        $summary.SnapshotRestored = $true
        Write-Output ('Restored copy: ' + $restored)
    }
    if ($result.Error) { throw $result.Error }
    if (-not $result.Compiled) { throw ('Native PLC compilation did not pass: ' + $result.CompileState) }
    if ($job.Block -and -not $result.Saved) { throw 'The approved working copy was not saved.' }
    if ($job.Block) {
        if (-not (Test-Path -LiteralPath (Join-Path $operationFolder 'diff.txt'))) { throw 'The approved operation did not leave a diff.' }
        $reopenJob = New-Object DeskBox.Engineering.Tia.TiaJob
        $reopenJob.ProjectFile = $result.Copy
        $reopened = [DeskBox.Engineering.Tia.TiaOpenness]::Run($reopenJob, $token)
        if ($reopened.Error) { throw $reopened.Error }
        [DeskBox.Engineering.Tia.TiaOpenness]::VerifyNewScaleBlock($inspection.Snapshot, $reopened.Snapshot, $TargetCpu, $job.Block)
        $summary.SavedCopyReopened = $true
    }
    Write-Output ('Copy: ' + $result.Copy)
}
} finally {
    $sourceAfter = [DeskBox.Engineering.Tia.TiaOpenness]::CopyTree($sourceFolder, $null, $token)
    $summary.SourceUnchanged = -not (Compare-Object -ReferenceObject $sourceBefore -DifferenceObject $sourceAfter -CaseSensitive)
    $summary | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $folder 'acceptance-summary.json')
    Write-Output ('Evidence: ' + $folder)
    if (-not $summary.SourceUnchanged) { throw 'The original project files changed.' }
}

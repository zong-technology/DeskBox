# Run using Windows PowerShell 5.1 on a licensed, isolated Windows test machine.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ClosedOfflineProject,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [string]$DeskBoxExe = 'dist\DeskBox.exe',
    [string]$TargetCpu,
    [switch]$Compile,
    [string]$ApprovedNewScaleBlock,
    [string[]]$ExpectedDevice = @(),
    [string[]]$ExpectedBlock = @(),
    [string[]]$ExpectedTag = @()
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Use Windows PowerShell 5.1 and .NET Framework 4.8.' }
if (($Compile -or $ApprovedNewScaleBlock) -and -not $TargetCpu) { throw 'Set -TargetCpu to the exact PLC path returned by an inspection.' }
if ($Compile -and $ApprovedNewScaleBlock) { throw 'Select either compile-only or an approved template insertion.' }
[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $DeskBoxExe).Path) | Out-Null
$envInfo = [DeskBox.Engineering.Tia.TiaEnvironmentInspector]::Inspect()
$envInfo.Describe() | Write-Output
if (-not ($envInfo.Installations | Where-Object { $_.Version -eq 20 -and $_.AssemblyPath }) -or -not $envInfo.OpennessGroup) {
    throw 'V20 PublicAPI and the active Siemens TIA Openness group are required.'
}
$folder = [DeskBox.Engineering.Tia.PlcEngineering]::FullLocalPath($EvidenceDirectory)
if (Test-Path -LiteralPath $folder) { throw 'EvidenceDirectory must be a new directory for this run.' }
[IO.Directory]::CreateDirectory($folder) | Out-Null
$envInfo.Describe() | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $folder 'environment.txt')
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
if ($Compile -or $ApprovedNewScaleBlock) {
    if ($inspection.Cpus -cnotcontains $TargetCpu) { throw 'TargetCpu does not match a PLC in the inspected source.' }
    $job.Cpu = $TargetCpu
    $job.ExpectedSource = $inspection.SourceHash
    if ($ApprovedNewScaleBlock) {
        $job.Operation = 'scale'
        $job.Block = $ApprovedNewScaleBlock
        $approvedSource = [DeskBox.Engineering.Tia.PlcEngineering]::AnalogScale($ApprovedNewScaleBlock)
        $job.ApprovedHash = [DeskBox.Engineering.Tia.PlcEngineering]::Hash($approvedSource)
    } else { $job.Operation = 'compile' }
    $result = [DeskBox.Engineering.Tia.TiaOpenness]::Run($job, $token)
    $result.Report | Write-Output
    if ($result.Error) { throw $result.Error }
    if (-not $result.Compiled) { throw ('Native PLC compilation did not pass: ' + $result.CompileState) }
    if ($ApprovedNewScaleBlock -and -not $result.Saved) { throw 'The approved working copy was not saved.' }
    $summary.Compile = $result.CompileState
    if ($ApprovedNewScaleBlock) {
        $operationFolder = Split-Path (Split-Path -LiteralPath $result.Copy)
        if (-not (Test-Path -LiteralPath (Join-Path $operationFolder 'diff.txt'))) { throw 'The approved operation did not leave a diff.' }
        $reopenJob = New-Object DeskBox.Engineering.Tia.TiaJob
        $reopenJob.ProjectFile = $result.Copy
        $reopened = [DeskBox.Engineering.Tia.TiaOpenness]::Run($reopenJob, $token)
        if ($reopened.Error) { throw $reopened.Error }
        [DeskBox.Engineering.Tia.TiaOpenness]::VerifyNewScaleBlock($inspection.Snapshot, $reopened.Snapshot, $TargetCpu, $ApprovedNewScaleBlock)
        $summary.SavedCopyReopened = $true
        $restored = [DeskBox.Engineering.Tia.TiaOpenness]::RestoreSnapshot($result, $token)
        $restoredManifest = [DeskBox.Engineering.Tia.TiaOpenness]::CopyTree((Split-Path -LiteralPath $restored), $null, $token)
        if ((Compare-Object -ReferenceObject $sourceBefore -DifferenceObject $restoredManifest -CaseSensitive)) { throw 'Restored copy does not match the original project files.' }
        $summary.SnapshotRestored = $true
        Write-Output ('Restored copy: ' + $restored)
    }
    Write-Output ('Copy: ' + $result.Copy)
}
$sourceAfter = [DeskBox.Engineering.Tia.TiaOpenness]::CopyTree($sourceFolder, $null, $token)
if ((Compare-Object -ReferenceObject $sourceBefore -DifferenceObject $sourceAfter -CaseSensitive)) { throw 'The original project files changed.' }
$summary.SourceUnchanged = $true
$summary | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $folder 'acceptance-summary.json')
Write-Output ('Evidence: ' + $folder)

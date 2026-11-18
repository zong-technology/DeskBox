# Run using Windows PowerShell 5.1 on a licensed, isolated Windows test machine.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ClosedOfflineProject,
    [Parameter(Mandatory=$true)][string]$EvidenceDirectory,
    [string]$DeskBoxExe = 'dist\DeskBox.exe',
    [string]$TargetCpu,
    [switch]$Compile,
    [string]$ApprovedNewScaleBlock
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
[DeskBox.Core.AppPaths]::DataDir = $folder
$job = New-Object DeskBox.Engineering.Tia.TiaJob
$job.ProjectFile = [DeskBox.Engineering.Tia.PlcEngineering]::FullLocalPath($ClosedOfflineProject)
$token = [Threading.CancellationToken]::None
$inspection = [DeskBox.Engineering.Tia.TiaOpenness]::Run($job, $token)
$inspection.Report | Write-Output
if ($inspection.Error) { throw $inspection.Error }
if (-not $inspection.Snapshot -or -not $inspection.Cpus.Count) { throw 'The selected project has no enumerated PLC.' }
$inspection.Cpus | ForEach-Object { Write-Output ('PLC: ' + $_) }
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
    Write-Output ('Copy: ' + $result.Copy)
}
Write-Output ('Evidence: ' + $folder)

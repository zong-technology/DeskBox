param(
    [Parameter(Mandatory=$true)][string]$SetupPath,
    [string]$SourceExe = 'dist\DeskBox.exe',
    [string]$OutputDir = 'dist\installer-qa'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$setup = (Resolve-Path -LiteralPath $SetupPath).Path
$source = (Resolve-Path -LiteralPath $SourceExe).Path
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDir))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$target = Join-Path $output 'test install\DeskBox'
$installed = Join-Path $target 'DeskBox.exe'
$expected = (Get-FileHash -LiteralPath $source).Hash
$checks = New-Object 'Collections.Generic.List[string]'
function Check([bool]$Value, [string]$Name) {
    if (-not $Value) { throw ('FAIL ' + $Name) }
    $checks.Add($Name)
    Write-Output ('PASS ' + $Name)
}
function Run-Setup([string]$Name, [bool]$ExpectSuccess = $true) {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/TASKS=', ('/DIR="' + $target + '"'), ('/LOG="' + (Join-Path $output ($Name + '.log')) + '"'))
    $process = Start-Process -FilePath $setup -ArgumentList $arguments -PassThru
    if (-not $process.WaitForExit(90000)) { $process.Kill(); throw ('Setup timed out: ' + $Name) }
    if ($ExpectSuccess) { Check ($process.ExitCode -eq 0) ('Setup completed: ' + $Name) }
    else { Check ($process.ExitCode -ne 0) ('Running application blocks setup: ' + $Name) }
}

Run-Setup 'fresh-install'
Check ((Get-FileHash -LiteralPath $installed).Hash -eq $expected) 'Installed payload equals the Windows build'
Check ([Diagnostics.FileVersionInfo]::GetVersionInfo($installed).ProductVersion -eq '2.1.0-hologram.3') 'Installed version identity'
Check (Test-Path -LiteralPath (Join-Path $target 'tools\rollback-install.ps1')) 'Rollback helper is installed'
Check (Test-Path -LiteralPath (Join-Path $target 'tools\install-document-skills.ps1')) 'Optional document helper is installed'
Check (-not (Test-Path -LiteralPath (Join-Path $target 'data'))) 'Fresh setup does not ship user data'
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{C6D8C809-F8BD-4D24-AB93-7F3AC1902101}_is1'
Check ((Get-ItemProperty -LiteralPath $registration).DisplayVersion -eq '2.1.0-hologram.3') 'Per-user uninstall registration'

# Launch the real installed executable with an isolated synthetic profile.
$profile = Join-Path $output 'isolated-profile'
$desktop = Join-Path $profile 'synthetic-desktop'
New-Item -ItemType Directory -Path $desktop -Force | Out-Null
$state = @{ Settings = @{ TakeoverIcons=$false; AutoStart=$false; ReferenceUi=$true; Theme='cyber'; DesktopDir=$desktop; LocalSyncEnabled=$false; NotifyTray=$false }; Categories=@(); Items=@{} }
$state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $profile 'state.json') -Encoding UTF8
$oldHome = $env:DESKBOX_HOME
$app = $null
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
public static class DeskBoxInstallCapture {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool Enumerate(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(Enumerate callback, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, Enumerate callback, IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    public static IntPtr FindMainWindow(int processId) {
        IntPtr found=IntPtr.Zero;
        Enumerate visit=delegate(IntPtr window, IntPtr state) {
            uint process; GetWindowThreadProcessId(window,out process);
            if(process==(uint)processId) {
                var title=new StringBuilder(256); GetWindowText(window,title,title.Capacity);
                Rect r; GetWindowRect(window,out r);
                if(title.ToString()=="DeskBox" && r.Right-r.Left>=640 && r.Bottom-r.Top>=480) found=window;
            }
            return true;
        };
        EnumWindows(delegate(IntPtr window,IntPtr state){visit(window,state);EnumChildWindows(window,visit,state);return true;},IntPtr.Zero);
        return found;
    }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    public static bool Save(IntPtr window, string path) {
        Rect rect; if (!GetWindowRect(window, out rect)) return false;
        using (var image = new Bitmap(rect.Right-rect.Left, rect.Bottom-rect.Top))
        using (var g = Graphics.FromImage(image)) {
            IntPtr dc = g.GetHdc(); bool copied;
            try { copied=PrintWindow(window, dc, 2); } finally { g.ReleaseHdc(dc); }
            if (copied) image.Save(path, ImageFormat.Png);
            return copied;
        }
    }
}
'@

try {
    $env:DESKBOX_HOME = $profile
    $app = Start-Process -FilePath $installed -WorkingDirectory $target -PassThru
    $until = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 150
        $app.Refresh()
        $window = [DeskBoxInstallCapture]::FindMainWindow($app.Id)
        if ($app.HasExited) { throw 'The installed application exited during startup.' }
    } while ($window -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $until)
    Check ($window -ne [IntPtr]::Zero) 'Real installed application starts to its main window'
    Check ([string]::Equals($app.MainModule.FileName, $installed, [StringComparison]::OrdinalIgnoreCase)) 'Running path is the installed candidate'
    Check ((Get-FileHash -LiteralPath $app.MainModule.FileName).Hash -eq $expected) 'Running executable hash matches the build'
    Start-Sleep -Milliseconds 300
    $dpi = [DeskBoxInstallCapture]::GetDpiForWindow($window)
    $capture = [DeskBoxInstallCapture]::Save($window, (Join-Path $output 'installed-main-window.png'))
    Check $capture 'Native capture of the real installed window'
    @{ Path=$app.MainModule.FileName; SHA256=$expected; Version='2.1.0-hologram.3'; DeviceDpi=$dpi; SyntheticProfile=$profile } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'installed-launch.json') -Encoding UTF8
    Check ([DeskBoxInstallCapture]::PostMessage($window, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) 'Installed window accepts a normal close request'
    Check ($app.WaitForExit(15000)) 'Application exits normally and releases the tray instance'
    Check ($app.ExitCode -eq 0) 'Installed application exits without a process error'
} finally {
    if ($app -and -not $app.HasExited) { $app.Kill(); $app.WaitForExit() }
    $env:DESKBOX_HOME = $oldHome
}

# Use a distinctly hashed, executable old-version fixture; no real user data is involved.
$fixture = Join-Path $output 'old-version-fixture.exe'
$fixtureCode = 'using System; class OldDeskBoxFixture { static void Main() { Console.WriteLine("isolated previous-version fixture"); } }'
Add-Type -TypeDefinition $fixtureCode -OutputAssembly $fixture -OutputType ConsoleApplication
Copy-Item -LiteralPath $fixture -Destination $installed -Force
$oldHash = (Get-FileHash -LiteralPath $installed).Hash
$userData = Join-Path $target 'data'
New-Item -ItemType Directory -Path $userData -Force | Out-Null
$markers = @{}
foreach ($name in @('state.json', 'tasks.json', 'ai-providers.json', 'chats.json', 'realtime-voice.json')) {
    $path = Join-Path $userData $name
    ('{"fixture":"preserve-' + $name + '"}') | Set-Content -LiteralPath $path -Encoding UTF8
    $markers[$path] = (Get-FileHash -LiteralPath $path).Hash
}
Run-Setup 'upgrade'
Check ((Get-FileHash -LiteralPath $installed).Hash -eq $expected) 'Upgrade installs the candidate payload'
$previous = Join-Path $target 'DeskBox.previous.exe'
Check ((Get-FileHash -LiteralPath $previous).Hash -eq $oldHash) 'Upgrade backs up the exact previous executable'
Run-Setup 'same-version-reinstall'
Check ((Get-FileHash -LiteralPath $previous).Hash -eq $oldHash) 'Same-payload reinstall preserves the rollback copy'
foreach ($path in $markers.Keys) { Check ((Get-FileHash -LiteralPath $path).Hash -eq $markers[$path]) ('Upgrade preserves ' + [IO.Path]::GetFileName($path)) }

$guard = New-Object Threading.Mutex($false, 'DeskBox.SingleInstance.7F3A')
try {
    Run-Setup 'running-app-block' $false
    $rollbackArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + (Join-Path $target 'tools\rollback-install.ps1') + '"'), '-InstallDir', ('"' + $target + '"'))
    $blocked = Start-Process powershell -ArgumentList $rollbackArguments -PassThru -RedirectStandardOutput (Join-Path $output 'blocked-rollback.log') -RedirectStandardError (Join-Path $output 'blocked-rollback-error.log')
    if (-not $blocked.WaitForExit(30000)) { $blocked.Kill(); throw 'Blocked rollback did not exit.' }
    Check ($blocked.ExitCode -ne 0) 'Rollback refuses a running DeskBox instance'
    Check ((Get-FileHash -LiteralPath $installed).Hash -eq $expected) 'Blocked operations leave the candidate intact'
} finally { $guard.Dispose() }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $target 'tools\rollback-install.ps1') -InstallDir $target
Check ($LASTEXITCODE -eq 0) 'Rollback completes'
Check ((Get-FileHash -LiteralPath $installed).Hash -eq $oldHash) 'Rollback restores the exact old fixture'
Check ((Get-FileHash -LiteralPath (Join-Path $target 'DeskBox.before-rollback.exe')).Hash -eq $expected) 'Rollback retains the candidate for recovery'
foreach ($path in $markers.Keys) { Check ((Get-FileHash -LiteralPath $path).Hash -eq $markers[$path]) ('Rollback preserves ' + [IO.Path]::GetFileName($path)) }
Run-Setup 're-upgrade'

# Re-run the existing native style acceptance host against the installed bytes.
$hologramHost = Join-Path $root 'dist\silverwing-accept.exe'
& $hologramHost $installed (Join-Path $output 'installed-hologram') | Tee-Object -FilePath (Join-Path $output 'installed-hologram.log')
Check ($LASTEXITCODE -eq 0) 'Installed immersive hologram checks pass'
$nativeResult = [regex]::Match([IO.File]::ReadAllText((Join-Path $output 'installed-hologram.log')), 'PASS (\d+) native checks')
if (-not $nativeResult.Success) { throw 'Native check count was not recorded.' }

# UI matrix uses an identical copy of the installed payload, with synthetic fixtures only.
& (Join-Path $root 'tools\prepare-ci-display.ps1')
& (Join-Path $root 'tools\ui-regression.ps1') -ExpectedDpi 96 -SourceExe $installed
Check ($?) 'Installed-payload 12-page navigation and three-layout matrix at 96 DPI'

$uninstall = Start-Process -FilePath (Join-Path $target 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -PassThru
Check ($uninstall.WaitForExit(60000)) 'Uninstaller completes'
Check ($uninstall.ExitCode -eq 0) 'Uninstaller returns success'
Check (-not (Test-Path -LiteralPath $installed)) 'Uninstall removes the program'
foreach ($path in $markers.Keys) { Check ((Get-FileHash -LiteralPath $path).Hash -eq $markers[$path]) ('Uninstall preserves ' + [IO.Path]::GetFileName($path)) }
Check (-not (Test-Path -LiteralPath $registration)) 'Uninstall removes only its registration'
@{ Result='PASS'; Version='2.1.0-hologram.3'; SourceSHA256=$expected; Checks=$checks.ToArray(); Count=$checks.Count; NativeChecks=[int]$nativeResult.Groups[1].Value; UIExpectedDpi=96; RealVoiceTested=$false } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'install-report.json') -Encoding UTF8
Write-Output ('PASS Windows setup / upgrade / rollback / uninstall: ' + $checks.Count + ' checks')

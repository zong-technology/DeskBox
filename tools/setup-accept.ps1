param(
    [Parameter(Mandatory=$true)][string]$SetupPath,
    [string]$SourceExe = 'dist\DeskBox.exe',
    [string]$OutputDir = 'dist\installer-qa',
    [ValidateSet(96,120,144)][int]$ExpectedDpi = 96,
    [string]$TiaFixtureDir = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$setup = (Resolve-Path -LiteralPath $SetupPath).Path
$source = (Resolve-Path -LiteralPath $SourceExe).Path
if ($TiaFixtureDir) { $TiaFixtureDir = (Resolve-Path -LiteralPath $TiaFixtureDir).Path }
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDir))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$target = Join-Path $output 'test install\DeskBox'
$installed = Join-Path $target 'DeskBox.exe'
$expected = (Get-FileHash -LiteralPath $source).Hash
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($source).ProductVersion
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{C6D8C809-F8BD-4D24-AB93-7F3AC1902101}_is1'
if (Test-Path -LiteralPath $registration) {
    $existing = (Get-ItemProperty -LiteralPath $registration).InstallLocation
    if (-not $existing -or -not [string]::Equals([IO.Path]::GetFullPath($existing).TrimEnd('\'), $target.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'An existing DeskBox installation is registered for this Windows user. Use a disposable test user/VM; the current installation was not changed.'
    }
}
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
Check ([Diagnostics.FileVersionInfo]::GetVersionInfo($installed).ProductVersion -eq $version) 'Installed version identity'
Check (Test-Path -LiteralPath (Join-Path $target 'tools\rollback-install.ps1')) 'Rollback helper is installed'
Check (Test-Path -LiteralPath (Join-Path $target 'tools\install-document-skills.ps1')) 'Optional document helper is installed'
Check (-not (Test-Path -LiteralPath (Join-Path $target 'data'))) 'Fresh setup does not ship user data'
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{C6D8C809-F8BD-4D24-AB93-7F3AC1902101}_is1'
Check ((Get-ItemProperty -LiteralPath $registration).DisplayVersion -eq $version) 'Per-user uninstall registration'

# Launch the real installed executable with an isolated synthetic profile.
$profile = Join-Path $output 'isolated-profile'
$desktop = Join-Path $profile 'synthetic-desktop'
New-Item -ItemType Directory -Path $desktop -Force | Out-Null
$state = @{ Settings = @{ TakeoverIcons=$false; AutoStart=$false; ReferenceUi=$true; Theme='cyber'; DesktopDir=$desktop; LocalSyncEnabled=$false; NotifyTray=$false }; Categories=@(); Items=@{} }
$state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $profile 'state.json') -Encoding UTF8
$oldHome = $env:DESKBOX_HOME
$app = $null
$initialDisplay = $null
$dpiMatrix = New-Object 'Collections.Generic.List[Object]'
    Add-Type -ReferencedAssemblies System.Drawing,System.Windows.Forms,UIAutomationClient,UIAutomationTypes,WindowsBase -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
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
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    public static bool SizeWorkbench(IntPtr window) {
        Rect bounds;
        return SetWindowPos(window, new IntPtr(-1), 0, 0, 1920, 1080, 0x0040) && GetWindowRect(window, out bounds)
            && bounds.Right - bounds.Left == 1920 && bounds.Bottom - bounds.Top == 1080;
    }
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder name,int size);
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr window,uint msg,IntPtr size,StringBuilder text);
    private static AutomationElement Find(IntPtr window, string id, string name) {
        var element = AutomationElement.FromHandle(window);
        var matches = element.FindAll(TreeScope.Descendants, new OrCondition(
            new PropertyCondition(AutomationElement.AutomationIdProperty, id),
            new PropertyCondition(AutomationElement.NameProperty, name)));
        foreach (AutomationElement match in matches)
            if (!match.Current.IsOffscreen && !match.Current.BoundingRectangle.IsEmpty) return match;
        throw new InvalidOperationException("Installed window does not expose " + id);
    }
    private static void Click(AutomationElement element) {
        var rect = element.Current.BoundingRectangle;
        Click((int)(rect.Left+rect.Width/2), (int)(rect.Top+rect.Height/2));
    }
    private static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(180);
    }
    public static string ExerciseTiaPrompt(IntPtr window) {
        SetThreadDpiAwarenessContext(new IntPtr(-4));
        // DeskBox normally sits at the bottom of the desktop. Keep this isolated
        // window above runner windows while delivering real pointer input.
        uint hitProcess = 0, appProcess;
        GetWindowThreadProcessId(window, out appProcess);
        AutomationElement navigation = null;
        for (int attempt = 0; attempt < 8 && hitProcess != appProcess; attempt++) {
            if (!SetWindowPos(window,new IntPtr(-1),0,0,0,0,0x0043)) throw new InvalidOperationException("Could not raise the installed test window.");
            SetForegroundWindow(window);
            Thread.Sleep(240);
            navigation = Find(window, "nav-tia", "PLC Studio");
            var bounds = navigation.Current.BoundingRectangle;
            IntPtr navHit = WindowFromPoint(new Point((int)(bounds.Left + bounds.Width/2), (int)(bounds.Top + bounds.Height/2)));
            GetWindowThreadProcessId(navHit, out hitProcess);
        }
        if (hitProcess != appProcess) throw new InvalidOperationException("The installed navigation point is obscured by another process.");
        Click(navigation);
        Click(Find(window, "tia-focus-requirement", "\u8f93\u5165\u5de5\u7a0b\u9700\u6c42"));
        IntPtr editor = new IntPtr(AutomationElement.FocusedElement.Current.NativeWindowHandle);
        var name = new StringBuilder(128);
        GetClassName(editor, name, name.Capacity);
        Rect edit;
        if (editor == IntPtr.Zero || !IsWindowVisible(editor) || name.ToString().IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) < 0 ||
            (GetWindowLong(editor,-16) & 0x0800) != 0 || !GetWindowRect(editor, out edit))
            throw new InvalidOperationException("The installed requirement shortcut did not focus an editable Win32 Rich Edit window.");
        Click((edit.Left+edit.Right)/2, (edit.Top+edit.Bottom)/2);
        if (AutomationElement.FocusedElement.Current.NativeWindowHandle != editor.ToInt32())
            throw new InvalidOperationException("The installed requirement editor did not receive mouse focus.");
        SendKeys.SendWait("S71500");
        SendKeys.SendWait("+{ENTER}");
        Clipboard.SetText("\u5de5\u7a0b\u9a8c\u6536");
        SendKeys.SendWait("^v");
        var text = new StringBuilder(1024);
        SendMessage(editor, 0x000D, new IntPtr(text.Capacity), text);
        SendKeys.SendWait("^a");
        Clipboard.SetText("DeskBox clipboard fixture");
        SendKeys.SendWait("^c");
        if (Normalize(Clipboard.GetText()) != Normalize(text.ToString()))
            throw new InvalidOperationException("Ctrl+A and Ctrl+C did not copy the actual requirement.");
        SendKeys.SendWait("{END}");
        return text.ToString();
    }
    private static string Normalize(string value) { return value.Replace("\r\n", "\n").TrimEnd('\r','\n'); }
    private static void SelectTab(IntPtr window, string id, string label) {
        var tab = Find(window, id, label);
        object pattern;
        if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern)) ((SelectionItemPattern)pattern).Select();
        else Click(tab);
        Thread.Sleep(160);
    }
    private static void EnterFileNames(IntPtr dialog, string input) {
        var fileName = AutomationElement.FromHandle(dialog).FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new OrCondition(new PropertyCondition(AutomationElement.AutomationIdProperty, "1148"),
                new PropertyCondition(AutomationElement.NameProperty, "File name:"))));
        if (fileName == null) throw new InvalidOperationException("The installed Windows file picker did not expose its file name field.");
        object value;
        if (fileName.TryGetCurrentPattern(ValuePattern.Pattern, out value)) ((ValuePattern)value).SetValue(input);
        else {
            Click(fileName);
            SendKeys.SendWait("^a");
            Clipboard.SetText(input);
            SendKeys.SendWait("^v");
        }
        var openButton = Find(dialog, "file-picker-open-button", "Open");
        object invoke;
        if (openButton.TryGetCurrentPattern(InvokePattern.Pattern, out invoke)) ((InvokePattern)invoke).Invoke();
        else Click(openButton);
    }
    private static void ImportFixtures(IntPtr window, string directory, string destination) {
        string[] files = Directory.GetFiles(directory, "fixture-*");
        if (files.Length != 5) throw new InvalidOperationException("Expected the five labeled TIA export fixtures.");
        Click(Find(window, "tia-open-exports", "\u6253\u5f00\u5bfc\u51fa\u8d44\u6599"));
        uint process;
        GetWindowThreadProcessId(window, out process);
        var end = DateTime.UtcNow.AddSeconds(6);
        IntPtr dialog = IntPtr.Zero;
        do {
            EnumWindows(delegate(IntPtr handle, IntPtr state) {
                uint owner;
                GetWindowThreadProcessId(handle, out owner);
                var className = new StringBuilder(48);
                GetClassName(handle, className, className.Capacity);
                if (owner == process && IsWindowVisible(handle) && className.ToString() == "#32770") dialog = handle;
                return dialog == IntPtr.Zero;
            }, IntPtr.Zero);
            if (dialog == IntPtr.Zero) Thread.Sleep(100);
        } while (dialog == IntPtr.Zero && DateTime.UtcNow < end);
        if (dialog == IntPtr.Zero) throw new InvalidOperationException("The installed file picker did not open.");
        SetWindowPos(dialog, new IntPtr(-1), 0, 0, 0, 0, 0x0043);
        SetForegroundWindow(dialog);
        try {
            EnterFileNames(dialog, directory);
            Thread.Sleep(400);
            var selected = string.Join(" ", Array.ConvertAll(files, path => "\"" + Path.GetFileName(path) + "\""));
            EnterFileNames(dialog, selected);
            end = DateTime.UtcNow.AddSeconds(10);
            const string heading = "\u5de5\u7a0b\u5bfc\u51fa\u8d44\u6599\uff085 \u4e2a\u6587\u4ef6\uff09";
            const string scope = "TIA \u5de5\u7a0b\u5bf9\u8c61";
            do {
                Thread.Sleep(200);
                if (IsWindowVisible(dialog)) continue;
                try {
                    Find(window, "tia-engineering-tree", scope);
                    Find(window, "expected-five-fixtures", heading);
                    SetWindowPos(window,new IntPtr(-1),0,0,0,0,0x0043);
                    SetForegroundWindow(window);
                    return;
                } catch (InvalidOperationException) { }
            } while (DateTime.UtcNow < end);
            throw new InvalidOperationException("The installed workbench never reported the five imported fixture files. File dialog still visible: " + IsWindowVisible(dialog));
        } catch {
            if (IsWindowVisible(dialog)) Save(dialog, Path.Combine(destination, "installed-tia-import-dialog-failure.png"));
            Save(window, Path.Combine(destination, "installed-tia-import-failure.png"));
            throw;
        }
    }
    private static void BuildDiffPreview(IntPtr window) {
        SelectTab(window, "tia-tab-programming", "\u7a0b\u5e8f\u7f16\u8f91");
        const string template = "\u8f7d\u5165\u7f29\u653e\u6a21\u677f";
        Click(Find(window, "tia-scl-template", template));
        var blockName = Find(window, "tia-scl-name", "\u7f29\u653e\u5757\u540d\u79f0");
        Click(blockName);
        SendKeys.SendWait("^a");
        SendKeys.SendWait("FB_AnalogScaleReviewed");
        Click(Find(window, "tia-scl-template", template));
        Find(window, "tia-scl-diff-lines", "SCL \u53cc\u680f\u884c\u5dee\u5f02");
    }
    private static IntPtr OwnedWindow(IntPtr window, string caption) {
        uint process;
        GetWindowThreadProcessId(window, out process);
        IntPtr result = IntPtr.Zero;
        var deadline = DateTime.UtcNow.AddSeconds(6);
        do {
            EnumWindows(delegate(IntPtr handle, IntPtr state) {
                uint owner;
                GetWindowThreadProcessId(handle, out owner);
                if (owner != process || !IsWindowVisible(handle)) return true;
                var title = new StringBuilder(160);
                GetWindowText(handle, title, title.Capacity);
                if (title.ToString() == caption) result = handle;
                return result == IntPtr.Zero;
            }, IntPtr.Zero);
            if (result == IntPtr.Zero) Thread.Sleep(100);
        } while (result == IntPtr.Zero && DateTime.UtcNow < deadline);
        if (result == IntPtr.Zero) throw new InvalidOperationException("The installed app did not open " + caption);
        return result;
    }
    public static int CaptureTiaPages(IntPtr window, string destination, string fixtureDirectory) {
        var shots = new List<string>();
        Action<IntPtr, string> capture = delegate(IntPtr handle, string name) {
            if (!Save(handle, Path.Combine(destination, name + ".png"))) throw new InvalidOperationException("Could not capture " + name);
            shots.Add(name);
        };
        SendKeys.SendWait("^a");
        capture(window, "installed-tia-input-selection");
        bool hasFixtures = !string.IsNullOrEmpty(fixtureDirectory);
        if (hasFixtures) {
            ImportFixtures(window, fixtureDirectory, destination);
            Click(Find(window, "tia-bind-project", "\u7ed1\u5b9a\u5de5\u7a0b"));
            var binding = OwnedWindow(window, "\u7ed1\u5b9a\u5de5\u7a0b \u00b7 PLC\u3001\u5bf9\u8c61\u4e0e\u80fd\u529b");
            Click(Find(binding, "mark-binding-test-source", "\u6807\u8bb0\u4e3a\u6d4b\u8bd5\u8d44\u6599"));
            if (!Save(binding, Path.Combine(destination, "installed-v43-binding.png"))) throw new InvalidOperationException("Could not capture the installed binding wizard");
            Click(Find(binding, "confirm-installed-tia-binding", "\u786e\u8ba4\u7ed1\u5b9a"));
            if (IsWindowVisible(binding)) throw new InvalidOperationException("The target binding was not confirmed");
            SetWindowPos(window,new IntPtr(-1),0,0,0,0,0x0043);
            SetForegroundWindow(window);
            if (!Save(window, Path.Combine(destination, "installed-v43-target.png"))) throw new InvalidOperationException("Could not capture the installed bound workbench");
        }
        string[] labels = { "\u7a0b\u5e8f\u5206\u6790", "\u786c\u4ef6\u7ec4\u6001", "\u7f51\u7edc\u62d3\u6251", "\u53d8\u91cf\u7ba1\u7406", "\u5de5\u7a0b\u5bf9\u6bd4", "\u6d4b\u8bd5\u9a8c\u8bc1" };
        string[] names = { "analysis", "hardware", "topology", "variables", "diff", "validation" };
        for (int i = 0; i < labels.Length; i++) {
            if (hasFixtures && names[i] == "diff") BuildDiffPreview(window);
            else SelectTab(window, "tia-tab-" + names[i], labels[i]);
            if (hasFixtures && names[i] == "validation") {
                SelectTab(window, "tia-tab-programming", "\u7a0b\u5e8f\u7f16\u8f91");
                Click(Find(window, "tia-scl-check", "\u9759\u6001\u68c0\u67e5\u8349\u7a3f"));
            }
            capture(window, "installed-tia-" + names[i]);
        }
        Click(Find(window, "tia-inspect", "\u67e5\u770b\u80fd\u529b\u4e0e\u73af\u5883"));
        Thread.Sleep(300);
        capture(window, "installed-tia-environment");
        Click(Find(window, "tia-toggle-assistant", "\u6536\u8d77\u52a9\u624b"));
        capture(window, "installed-tia-assistant-collapsed");
        Click(Find(window, "tia-reset-layout", "\u6062\u590d\u9ed8\u8ba4\u5e03\u5c40"));
        if (hasFixtures) SelectTab(window, "tia-tab-programming", "\u7a0b\u5e8f\u7f16\u8f91");
        capture(window, "installed-tia-layout-default");
        Click(Find(window, "tia-voice", "\u5de5\u7a0b\u8bed\u97f3"));
        IntPtr voice = OwnedWindow(window, "\u535a\u56fe\u5b9e\u65f6\u8bed\u97f3");
        try { capture(voice, "installed-tia-voice"); }
        finally { PostMessage(voice, 0x0010, IntPtr.Zero, IntPtr.Zero); }
        File.WriteAllLines(Path.Combine(destination, "installed-tia-screenshots.txt"), shots.ToArray());
        return shots.Count + 1;
    }
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
    if ($env:GITHUB_ACTIONS -eq 'true') {
        $initialDisplay = [System.Windows.Forms.SystemInformation]::PrimaryMonitorSize
        & (Join-Path $root 'tools\prepare-ci-display.ps1')
    }
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
    if ($env:GITHUB_ACTIONS -eq 'true') {
        Check ([DeskBoxInstallCapture]::SizeWorkbench($window)) 'The actual installed workbench is 1920 by 1080 before capture'
    }
    $dpi = [DeskBoxInstallCapture]::GetDpiForWindow($window)
    Check ($dpi -eq $ExpectedDpi) ('Actual installed window reports ' + $ExpectedDpi + ' DPI')
    $dpiMatrix.Add(@{Dpi=$dpi; Status='PASS'; Evidence='Installed window, input gestures, and TIA screenshots'})
    $capture = [DeskBoxInstallCapture]::Save($window, (Join-Path $output 'installed-main-window.png'))
    Check $capture 'Native capture of the real installed window'
    $typed = [DeskBoxInstallCapture]::ExerciseTiaPrompt($window)
    Check ($typed -match 'S71500[\r\n]+\p{IsCJKUnifiedIdeographs}{4}') 'Installed TIA input receives mouse focus, English typing, line breaks and Chinese clipboard text'
    Check ([DeskBoxInstallCapture]::Save($window, (Join-Path $output 'installed-tia-input.png'))) 'Native capture of the installed TIA input'
    $tiaScreenshotCount = [DeskBoxInstallCapture]::CaptureTiaPages($window, $output, $TiaFixtureDir)
    Check ($tiaScreenshotCount -eq 12) 'Twelve TIA screenshots captured directly from the installed DeskBox process'
    $pngs = @('installed-tia-input') + (Get-Content -LiteralPath (Join-Path $output 'installed-tia-screenshots.txt')) |
        ForEach-Object { Join-Path $output ($_ + '.png') }
    $index = Join-Path $output 'SCREENSHOT-INDEX.md'
    $dataState = if ($TiaFixtureDir) { 'Labeled export fixtures loaded in the real installed UI' } else { 'Empty state' }
    @(
        '# Installed DeskBox TIA screenshots', '',
        ('Version ' + $version + ' at ' + $dpi + ' DPI. These PNGs come from the installed executable.'),
        'TIA V20 project reads, compilation and connected provider audio are outside this screenshot evidence.', '',
        '| PNG | Function | Test state |', '| --- | --- | --- |',
        '| installed-tia-input.png | Programming and requirement input | PASS - mouse focus, typing and paste. Empty project. |',
        '| installed-tia-input-selection.png | Requirement selection | PASS - keyboard selection. Empty project. |',
        ('| installed-tia-analysis.png | Analysis | CAPTURED - ' + $dataState + ' |'),
        ('| installed-tia-hardware.png | Rack, module inventory and details | CAPTURED - ' + $dataState + ' |'),
        ('| installed-tia-topology.png | Zoomable topology and connection sources | CAPTURED - ' + $dataState + ' |'),
        ('| installed-tia-variables.png | Variables and address table | CAPTURED - ' + $dataState + ' |'),
        ('| installed-tia-diff.png | Draft difference | CAPTURED - ' + $(if ($TiaFixtureDir) { 'actual local template changes awaiting approval' } else { 'empty state' }) + ' |'),
        ('| installed-tia-validation.png | Verification report | ' + $(if ($TiaFixtureDir) { 'PASS - local static check ran. TIA compile BLOCKED.' } else { 'CAPTURED - no run.' }) + ' |'),
        '| installed-tia-environment.png | Installation and dependency inspection | CAPTURED - current machine. TIA round trip BLOCKED on the hosted runner. |',
        '| installed-tia-assistant-collapsed.png | Assistant pane collapse | CAPTURED - collapsed. |',
        '| installed-tia-layout-default.png | Default panes | CAPTURED - restored. |',
        '| installed-tia-voice.png | Engineering voice window | CAPTURED - idle. Active provider and microphone BLOCKED. |'
    ) | Set-Content -LiteralPath $index -Encoding UTF8
    Compress-Archive -Path ($pngs + @($index)) -DestinationPath (Join-Path $output 'installed-tia-screenshots.zip') -Force
    $windows = Get-CimInstance -ClassName Win32_OperatingSystem
    $inputProfiles = @(if (Get-Command Get-WinUserLanguageList -ErrorAction SilentlyContinue) { (Get-WinUserLanguageList).InputMethodTips })
    @{ Path=$app.MainModule.FileName; SHA256=$expected; Version=$version; DeviceDpi=$dpi; SyntheticProfile=$profile } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'installed-launch.json') -Encoding UTF8
    Check ([DeskBoxInstallCapture]::PostMessage($window, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) 'Installed window accepts a normal close request'
    Check ($app.WaitForExit(15000)) 'Application exits normally and releases the tray instance'
    Check ($app.ExitCode -eq 0) 'Installed application exits without a process error'

    if ($env:GITHUB_ACTIONS -eq 'true' -and $env:RUNNER_ENVIRONMENT -eq 'github-hosted' -and $ExpectedDpi -eq 96) {
        $displayScale = $null
        $probeFailure = ''
        try { $displayScale = [DeskBoxCIDisplay]::ReadScale() }
        catch { $probeFailure = $_.Exception.GetBaseException().Message }
        foreach ($level in 120,144) {
            $scaledApp = $null
            $scaleApplied = $false
            $result = @{Dpi=$level; Status='BLOCKED'; Evidence=$probeFailure}
            try {
                if (-not $displayScale) { continue }
                $relative = $displayScale.Minimum + ($level - 96) / 24
                if ($relative -gt $displayScale.Maximum) { $result.Evidence = 'Requested scale is outside the runner display range'; continue }
                $changed = [DeskBoxCIDisplay]::SetScale($displayScale, [int]$relative)
                if ($changed -ne 0) { $result.Evidence = 'Windows display scale request returned ' + $changed; continue }
                $scaleApplied = $true
                Start-Sleep -Milliseconds 750
                $scaledApp = Start-Process -FilePath $installed -WorkingDirectory $target -PassThru
                $scaledWindow = [IntPtr]::Zero
                for ($i=0; $i -lt 50 -and $scaledWindow -eq [IntPtr]::Zero -and -not $scaledApp.HasExited; $i++) {
                    Start-Sleep -Milliseconds 100
                    $scaledWindow = [DeskBoxInstallCapture]::FindMainWindow($scaledApp.Id)
                }
                if ($scaledWindow -eq [IntPtr]::Zero) { $result.Evidence = 'No installed owner window after changing scale'; continue }
                $actual = [DeskBoxInstallCapture]::GetDpiForWindow($scaledWindow)
                $result.ObservedDpi = $actual
                if ($actual -ne $level) { $result.Evidence = 'Installed window reported ' + $actual + ' DPI'; continue }
                $result.Status = 'FAIL'
                Start-Sleep -Milliseconds 400
                $text = [DeskBoxInstallCapture]::ExerciseTiaPrompt($scaledWindow)
                if ($text -notmatch 'S71500[\r\n]+\p{IsCJKUnifiedIdeographs}{4}') { throw 'The scaled input did not retain the multiline text' }
                $fileName = 'installed-tia-dpi-' + $level + '.png'
                if (-not [DeskBoxInstallCapture]::Save($scaledWindow, (Join-Path $output $fileName))) { throw 'The scaled workbench screenshot could not be written' }
                $result.Status = 'PASS'
                $result.Evidence = $fileName + ' / actual DPI and physical input verified'
            } catch { $result.Evidence = $_.Exception.GetBaseException().Message }
            finally {
                if ($scaledApp -and -not $scaledApp.HasExited) { $scaledApp.Kill(); $scaledApp.WaitForExit() }
                if ($scaleApplied) {
                    $restoredScale = [DeskBoxCIDisplay]::SetScale($displayScale, $displayScale.Current)
                    if ($restoredScale -ne 0) { throw ('Failed to restore the runner scale: ' + $restoredScale) }
                }
                $dpiMatrix.Add($result)
                Write-Output ($result.Status + ' ' + $level + ' DPI: ' + $result.Evidence)
            }
        }
    }
    if ($dpiMatrix.Count -eq 1) {
        foreach ($level in 120,144) { if ($level -ne $ExpectedDpi) { $dpiMatrix.Add(@{Dpi=$level; Status='BLOCKED'; Evidence='No hosted scale probe in this session'}) } }
    }
    @{ Windows=$windows.Caption; Build=$windows.BuildNumber; DeviceDpi=$dpi; InputMethodTips=$inputProfiles; MicrosoftPinyinTested=$false; DpiMatrix=$dpiMatrix.ToArray() } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'installed-tia-environment.json') -Encoding UTF8
} finally {
    if ($app -and -not $app.HasExited) { $app.Kill(); $app.WaitForExit() }
    $env:DESKBOX_HOME = $oldHome
    if ($initialDisplay -and [System.Windows.Forms.SystemInformation]::PrimaryMonitorSize -ne $initialDisplay) {
        $restored = [DeskBoxCIDisplay]::SetSize($initialDisplay.Width, $initialDisplay.Height)
        if ($restored -ne 0) { throw ('Could not restore CI display size after capturing the installer: ' + $restored) }
    }
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
foreach ($name in @('state.json', 'tasks.json', 'ai-providers.json', 'chats.json', 'realtime-voice.json', 'ai-environment.json', 'ai-providers-local.json', 'chats-local.json', 'realtime-voice-local.json', 'trusted-roots.json', 'trusted-roots-local.json', 'engineering-current.json', 'engineering-workspaces.json', 'chats-projects.json', 'chats-projects-local.json', 'tia-mcp-policy.json')) {
    $path = Join-Path $userData $name
    $(if ($name -eq 'ai-environment.json') { '"network"' } else { '{"fixture":"preserve-' + $name + '"}' }) | Set-Content -LiteralPath $path -Encoding UTF8
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
if ($env:GITHUB_ACTIONS -eq 'true') { & (Join-Path $root 'tools\prepare-ci-display.ps1') }
& (Join-Path $root 'tools\ui-regression.ps1') -ExpectedDpi $ExpectedDpi -SourceExe $installed
Check ($?) ('Installed-payload 12-page navigation and three-layout matrix at ' + $ExpectedDpi + ' DPI')

$uninstall = Start-Process -FilePath (Join-Path $target 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -PassThru
Check ($uninstall.WaitForExit(60000)) 'Uninstaller completes'
Check ($uninstall.ExitCode -eq 0) 'Uninstaller returns success'
Check (-not (Test-Path -LiteralPath $installed)) 'Uninstall removes the program'
foreach ($path in $markers.Keys) { Check ((Get-FileHash -LiteralPath $path).Hash -eq $markers[$path]) ('Uninstall preserves ' + [IO.Path]::GetFileName($path)) }
Check (-not (Test-Path -LiteralPath $registration)) 'Uninstall removes only its registration'
@{ Result='PASS'; Version=$version; SourceSHA256=$expected; Checks=$checks.ToArray(); Count=$checks.Count; NativeChecks=[int]$nativeResult.Groups[1].Value; UIExpectedDpi=$ExpectedDpi; TiaScreenshotCount=$tiaScreenshotCount; Windows=$windows.Caption; WindowsBuild=$windows.BuildNumber; RealVoiceTested=$false } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'install-report.json') -Encoding UTF8
Write-Output ('PASS Windows setup / upgrade / rollback / uninstall: ' + $checks.Count + ' checks')

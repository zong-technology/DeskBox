#ifndef ReleaseVersion
  #define ReleaseVersion "2.1.0-hologram.6"
#endif
#ifndef PayloadSHA256
  #error Build the installer using tools/package-windows.ps1
#endif
#ifndef FileVersion
  #error Build the installer using tools/package-windows.ps1
#endif
#define RepoRoot SourcePath + ".."

[Setup]
AppId={{C6D8C809-F8BD-4D24-AB93-7F3AC1902101}
AppName=DeskBox
AppVersion={#ReleaseVersion}
VersionInfoVersion={#FileVersion}
AppVerName=DeskBox {#ReleaseVersion}
AppPublisher=zong-technology
AppPublisherURL=https://github.com/zong-technology/DeskBox
AppSupportURL=https://github.com/zong-technology/DeskBox/issues
DefaultDirName={code:DefaultInstallDir}
DefaultGroupName=DeskBox
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UsePreviousAppDir=yes
UsePreviousGroup=yes
DisableProgramGroupPage=yes
WizardStyle=modern
SetupIconFile={#RepoRoot}\assets\deskbox.ico
UninstallDisplayIcon={app}\DeskBox.exe
OutputDir={#RepoRoot}\dist\installer
OutputBaseFilename=DeskBox-{#ReleaseVersion}-Setup
Compression=lzma2
SolidCompression=yes
AppMutex=DeskBox.SingleInstance.7F3A
SetupMutex=DeskBox.Setup.7F3A
CloseApplications=no
RestartApplications=no
Uninstallable=yes
UninstallDisplayName=DeskBox {#ReleaseVersion}

[Languages]
Name: "chinesesimp"; MessagesFile: "{#SourcePath}languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked

[Files]
Source: "{#RepoRoot}\dist\DeskBox.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\docs\WINDOWS-INSTALL.md"; DestDir: "{app}\docs"; DestName: "安装升级说明.txt"; Flags: ignoreversion
Source: "{#RepoRoot}\docs\SILVERWING-NATIVE.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RepoRoot}\public\assets\NOVA-model-license.txt"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#RepoRoot}\tools\rollback-install.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "{#RepoRoot}\tools\install-document-skills.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "{#RepoRoot}\packaging\rollback.cmd"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\DeskBox"; Filename: "{app}\DeskBox.exe"; WorkingDir: "{app}"
Name: "{group}\安装升级说明"; Filename: "{app}\docs\安装升级说明.txt"
Name: "{group}\回退到升级前版本"; Filename: "{app}\rollback.cmd"; WorkingDir: "{app}"
Name: "{group}\卸载 DeskBox"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DeskBox"; Filename: "{app}\DeskBox.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\DeskBox.exe"; Description: "启动 DeskBox"; Flags: nowait postinstall skipifsilent

[Code]
var
  BackupPrepared: Boolean;

function DefaultInstallDir(Param: String): String;
begin
  if FileExists('D:\DeskBox\DeskBox.exe') then
    Result := 'D:\DeskBox'
  else
    Result := ExpandConstant('{localappdata}\Programs\DeskBox');
end;

function InitializeSetup(): Boolean;
var
  FrameworkRelease: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', FrameworkRelease)
    and (FrameworkRelease >= 528040);
  if not Result then
    MsgBox('需要 Microsoft .NET Framework 4.8 或更新版本。请安装后重新运行本安装包。' + #13#10 +
      '官方下载：https://dotnet.microsoft.com/download/dotnet-framework/net48', mbError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  CurrentExe, PreviousExe: String;
begin
  Result := '';
  if BackupPrepared then Exit;
  CurrentExe := ExpandConstant('{app}\DeskBox.exe');
  PreviousExe := ExpandConstant('{app}\DeskBox.previous.exe');
  if FileExists(CurrentExe) then
  begin
    { Reinstalling the same candidate must not discard the earlier rollback copy. }
    if CompareText(GetSHA256OfFile(CurrentExe), '{#PayloadSHA256}') = 0 then
    begin
      BackupPrepared := True;
      Exit;
    end;
    if not FileCopy(CurrentExe, PreviousExe, False) then
    begin
      Result := '无法备份现有 DeskBox。请先从托盘退出旧程序，并检查所选目录的写入权限。未开始替换程序。';
      Exit;
    end;
  end;
  BackupPrepared := True;
end;

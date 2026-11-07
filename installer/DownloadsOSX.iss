; Inno Setup 6 script for a per-user install of Downloads.exe (no admin rights; the self-updater keeps working).
; Build it with tools/build-installer.ps1: it passes the version and, when a certificate is configured,
; the sign tool that signs the setup and its uninstaller.

#ifndef SourceExe
  #define SourceExe "..\src\DownloadsOSX\bin\Release\net48\Downloads.exe"
#endif
#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(SourceExe)
#endif
#ifndef FileVersion
  #define FileVersion GetVersionNumbersString(SourceExe)
#endif

#define AppName "DownloadsOSX"
#define ExeName "Downloads.exe"
#define RepoUrl "https://github.com/fhnb16/DownloadsOSX"
; Must match UpdateService.UninstallKey.
#define AppId "{{B7E3A1C4-5D2F-4E8A-9C6B-1F0D2A3E4B5C}"
; Must match SingleInstance.MutexName and Autostart.ValueName.
#define AppMutex "Local\DownloadsOSX.SingleInstance.7F3B2C1E"
#define RunValue "DownloaderOSX"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=fhnb
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#FileVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=6.1sp1
; Without the version: the app updates itself and keeps DisplayVersion current (UpdateService.SyncInstalledVersion).
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#ExeName}
SetupIconFile=..\src\DownloadsOSX\Assets\Downloads.ico
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir=..\dist
OutputBaseFilename=DownloadsOSX-Setup
#ifdef Sign
; The "signtool" command is defined by tools/build-installer.ps1 (ISCC /S); signs the setup and the uninstaller.
SignTool=signtool
SignedUninstaller=yes
#endif

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "uk"; MessagesFile: "compiler:Languages\Ukrainian.isl"

[CustomMessages]
en.CloseApp={#AppName} is still running. Close it and try again.
ru.CloseApp={#AppName} всё ещё запущен. Закройте его и повторите попытку.
uk.CloseApp={#AppName} досі запущено. Закрийте його та спробуйте ще раз.

[Tasks]
Name: "autostart"; Description: "{cm:AutoStartProgram,{#AppName}}"; GroupDescription: "{cm:AutoStartProgramGroupDescription}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; No ignoreversion: an older setup never replaces a newer exe the app has already updated itself to.
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: replacesameversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Registry]
; The app adds --minimized itself on start if that setting is on (Autostart.Refresh).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#RunValue}"; ValueData: """{app}\{#ExeName}"""; Tasks: autostart

[Run]
Filename: "{app}\{#ExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Leftovers of self-updates. Settings in %AppData%\fhnb16\DownloadsOSX are kept.
Type: files; Name: "{app}\{#ExeName}.old*"
Type: files; Name: "{app}\{#ExeName}.new"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

{ Asks the running instance (any copy) to exit via "Downloads.exe --exit" and waits up to 10 s for it. }
function CloseRunningApp(Exe: String): Boolean;
var
  ResultCode, I: Integer;
begin
  Result := not CheckForMutexes('{#AppMutex}');
  if Result then Exit;
  Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  for I := 1 to 50 do
  begin
    Sleep(200);
    if not CheckForMutexes('{#AppMutex}') then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CheckForMutexes('{#AppMutex}') then
  begin
    ExtractTemporaryFile('{#ExeName}');
    if not CloseRunningApp(ExpandConstant('{tmp}\{#ExeName}')) then
      Result := CustomMessage('CloseApp');
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := CloseRunningApp(ExpandConstant('{app}\{#ExeName}'));
  if not Result then
    MsgBox(CustomMessage('CloseApp'), mbError, MB_OK);
end;

{ Removes the autostart entry whether the setup or the app's settings created it, but only if it points to this copy. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: String;
begin
  if (CurUninstallStep = usUninstall) and RegQueryStringValue(HKCU, RunKey, '{#RunValue}', Value) and
     (Pos(Lowercase(ExpandConstant('{app}\{#ExeName}')), Lowercase(Value)) > 0) then
    RegDeleteValue(HKCU, RunKey, '{#RunValue}');
end;

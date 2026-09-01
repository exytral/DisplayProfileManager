; =============================================
; Inno Setup Script for Display Profile Manager
; Multi-architecture (x64 / x86 / arm64)
; =============================================

; ---- Common Settings ----
#define MyAppName       "Display Profile Manager"
#define MyAppPublisher  "exytral"
#define MyAppURL        "https://github.com/exytral/DisplayProfileManager"
#define MyAppExeName    "DisplayProfileManager.exe"
#define MyIconFile      ".\icon.ico"
#define MyLicenseFile   "..\LICENSE"
#define MyOutputFolder  ".\setup"

; ---- Version ----
; Overridden at build time via /DMyAppVersion=<tag> from GitHub Actions.
; Falls back to "dev" for local builds.
#ifndef MyAppVersion
  #define MyAppVersion "dev"
#endif

; ---- Select Target Architecture (can be x64 / x86 / arm64) ----
#define TargetArch "x64"
; #define TargetArch "x86"
; #define TargetArch "arm64"

; ---- Architecture-specific Settings ----
#if TargetArch == "x64"
  #define MyBuildFolder       ".\bin\x64\Release"
  #define MyOutputFile        "DisplayProfileManager-" + MyAppVersion + "-" + TargetArch + "-Setup"
  #define MyAppId             "{{CFD9DD98-5D17-43AB-88DD-549D154A64D2}-x64}"
  #define ArchAllowed         "x64os"
  #define ArchInstall64       "x64os"
  #define DotNetArch          "x64"
  #define DotNetRoot          HKLM64
  #define DotNetRuntimeUrl    "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"
  #define DotNetInstallerName "windowsdesktop-runtime-win-x64.exe"
#elif TargetArch == "x86"
  #define MyBuildFolder       ".\bin\x86\Release"
  #define MyOutputFile        "DisplayProfileManager-" + MyAppVersion + "-" + TargetArch + "-Setup"
  #define MyAppId             "{{CFD9DD98-5D17-43AB-88DD-549D154A64D2}-x86}"
  #define ArchAllowed         "x86"
  #define ArchInstall64       ""
  #define DotNetArch          "x86"
  #define DotNetRoot          HKLM32
  #define DotNetRuntimeUrl    "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x86.exe"
  #define DotNetInstallerName "windowsdesktop-runtime-win-x86.exe"
#elif TargetArch == "arm64"
  #define MyBuildFolder       ".\bin\ARM64\Release"
  #define MyOutputFile        "DisplayProfileManager-" + MyAppVersion + "-" + TargetArch + "-Setup"
  #define MyAppId             "{{CFD9DD98-5D17-43AB-88DD-549D154A64D2}-arm64}"
  #define ArchAllowed         "arm64"
  #define ArchInstall64       "arm64"
  #define DotNetArch          "arm64"
  #define DotNetRoot          HKLM64
  #define DotNetRuntimeUrl    "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-arm64.exe"
  #define DotNetInstallerName "windowsdesktop-runtime-win-arm64.exe"
#else
  #error "Please set a valid TargetArch (x64 / x86 / arm64)"
#endif

; ---- Setup Configuration ----
[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=no
DefaultDirName={autopf}\{#MyAppName}
DisableDirPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed={#ArchAllowed}
ArchitecturesInstallIn64BitMode={#ArchInstall64}
DisableProgramGroupPage=yes
LicenseFile={#MyLicenseFile}
OutputDir={#MyOutputFolder}
OutputBaseFilename={#MyOutputFile}
SetupIconFile={#MyIconFile}
SolidCompression=yes
WizardStyle=modern

; ---- Languages ----
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; ---- Tasks ----
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; ---- Files to Clean ----
[InstallDelete]
Type: files; Name: "{app}\AudioSwitcher.AudioApi.dll"
Type: files; Name: "{app}\AudioSwitcher.AudioApi.CoreAudio.dll"
Type: files; Name: "{app}\DisplayProfileManager.exe.config"
Type: files; Name: "{app}\*.pdb"

; ---- Files to Package ----
[Files]
Source: "{#MyBuildFolder}\*.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyBuildFolder}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyBuildFolder}\*.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyBuildFolder}\runtimes\*"; DestDir: "{app}\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MyBuildFolder}\NLog.config"; DestDir: "{app}"; Flags: ignoreversion

; ---- Shortcuts ----
[Icons]
Name: "{autoprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; ---- Run after Installation ----
[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runasoriginaluser shellexec

; ---- Run before Uninstallation ----
[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--exit"; Flags: runhidden; Check: InstalledDpmMeetsLifecycleVersion; RunOnceId: "ExitApplication"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM ""{#MyAppExeName}"""; Flags: runhidden; Check: ShouldForceTerminateDpm; RunOnceId: "ForceTerminateApplication"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unshell"; Flags: runhidden; Check: InstalledDpmMeetsLifecycleVersion; RunOnceId: "UnregisterShellMenu"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""\DisplayProfileManager\DisplayProfileManager_Startup"" /F"; Flags: runhidden; RunOnceId: "DeleteStartupTask"

; ---- Additional Uninstall Cleanup ----
[UninstallDelete]
Type: dirifempty; Name: "{app}"

; ---- Prerequisite Handling ----
[Code]
var
  DotNetPage: TWizardPage;
  DotNetStatusLabel: TNewStaticText;
  DotNetDownloadPage: TDownloadWizardPage;
  ShellExtStateKnown: Boolean;
  ShellExtWasRegisteredBeforeInstall: Boolean;

const
  RegistryRunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RegistryRunValue = 'DisplayProfileManager';

  DpmMutexName = 'DPM_Mutex';
  DpmLifecycleMajor = 2;
  DpmLifecycleMinor = 2;
  DpmLifecycleRevision = 0;
  DpmLifecycleBuild = 0;
  DpmShutdownTimeoutMs = 1000;
  DpmShutdownPollMs = 100;

function ExecuteApplicationCommand(const Parameters: String; Wait: TExecWait; var ExitCode: Integer): Boolean;
var
  ExecutablePath: String;
begin
  Result := False;
  ExitCode := 0;

  ExecutablePath := ExpandConstant('{app}\{#MyAppExeName}');

  if not FileExists(ExecutablePath) then
    Exit;

  Result := ExecAsOriginalUser(
    ExecutablePath,
    Parameters,
    ExpandConstant('{app}'),
    SW_HIDE,
    Wait,
    ExitCode);
end;

function GetInstalledDpmVersion(var Major, Minor, Revision, Build: Word): Boolean;
begin
  Result := GetVersionComponents(
    ExpandConstant('{app}\{#MyAppExeName}'),
    Major,
    Minor,
    Revision,
    Build);
end;

function InstalledDpmMeetsLifecycleVersion: Boolean;
var
  Major, Minor, Revision, Build: Word;
begin
  Result := False;

  if not GetInstalledDpmVersion(Major, Minor, Revision, Build) then
    Exit;

  Result :=
    (Major > DpmLifecycleMajor) or
    ((Major = DpmLifecycleMajor) and (Minor > DpmLifecycleMinor)) or
    ((Major = DpmLifecycleMajor) and (Minor = DpmLifecycleMinor) and
     (Revision > DpmLifecycleRevision)) or
    ((Major = DpmLifecycleMajor) and (Minor = DpmLifecycleMinor) and
     (Revision = DpmLifecycleRevision) and (Build >= DpmLifecycleBuild));
end;

function WaitForDpmToExit: Boolean;
var
  ElapsedMs: Integer;
begin
  Result := True;
  ElapsedMs := 0;

  while CheckForMutexes(DpmMutexName) do
  begin
    if ElapsedMs >= DpmShutdownTimeoutMs then
    begin
      Result := False;
      Exit;
    end;

    Sleep(DpmShutdownPollMs);
    ElapsedMs := ElapsedMs + DpmShutdownPollMs;
  end;
end;

function ForceTerminateDpm: Boolean;
var
  ResultCode: Integer;
begin
  if not CheckForMutexes(DpmMutexName) then
  begin
    Result := True;
    Exit;
  end;

  Log('Display Profile Manager did not exit gracefully; forcing process termination.');

  if not Exec(
    ExpandConstant('{sys}\taskkill.exe'),
    '/F /IM "{#MyAppExeName}"',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    Log('Failed to launch taskkill.exe.');
    Result := False;
    Exit;
  end;

  if ResultCode <> 0 then
  begin
    Log('taskkill.exe returned exit code ' + IntToStr(ResultCode) + '.');
    Result := False;
    Exit;
  end;

  Result := WaitForDpmToExit;

  if Result then
    Log('Display Profile Manager terminated successfully.')
  else
    Log('Display Profile Manager is still running after forced termination.');
end;

function ShouldForceTerminateDpm: Boolean;
begin
  Result := CheckForMutexes(DpmMutexName);

  if Result and InstalledDpmMeetsLifecycleVersion then
  begin
    if not WaitForDpmToExit then
      Result := True
    else
      Result := False;
  end;
end;

function PrepareDpmForInstall: String;
var
  ExitCode: Integer;
  SupportsShellLifecycle: Boolean;
begin
  Result := '';
  ShellExtStateKnown := False;
  ShellExtWasRegisteredBeforeInstall := False;

  if not FileExists(ExpandConstant('{app}\{#MyAppExeName}')) then
  begin
    ShellExtStateKnown := True;
    Exit;
  end;

  SupportsShellLifecycle := InstalledDpmMeetsLifecycleVersion;

  if SupportsShellLifecycle then
  begin
    Log('Installed Display Profile Manager supports shell lifecycle commands.');

    if not ExecuteApplicationCommand('--exit', ewNoWait, ExitCode) then
      Log('Could not launch --exit; continuing to shutdown verification.');

    if not WaitForDpmToExit then
    begin
      if not ForceTerminateDpm then
      begin
        Result :=
          'Display Profile Manager could not be stopped before installation. ' +
          'Please close it and try again.';
        Exit;
      end;
    end;
  end
  else
  begin
    Log('Installed Display Profile Manager predates shell lifecycle commands; forcing termination if running.');

    if not ForceTerminateDpm then
    begin
      Result :=
        'Display Profile Manager could not be stopped before installation. ' +
        'Please close it and try again.';
      Exit;
    end;
  end;

  if not SupportsShellLifecycle then
  begin
    ShellExtStateKnown := True;
    Exit;
  end;

  if not ExecuteApplicationCommand('--unshell', ewWaitUntilTerminated, ExitCode) then
  begin
    Result :=
      'Display Profile Manager could not unregister its Explorer shell extension ' +
      'before installation. Please try again.';
    Exit;
  end;

  case ExitCode of
    0:
      ShellExtWasRegisteredBeforeInstall := True;

    2:
      ShellExtWasRegisteredBeforeInstall := False;

  else
    begin
      Result :=
        'Display Profile Manager could not confirm its Explorer shell extension ' +
        'state before installation (exit code ' + IntToStr(ExitCode) + '). ' +
        'Please try again.';
      Exit;
    end;
  end;

  ShellExtStateKnown := True;
end;

function ShouldRegisterShellExtension: Boolean;
begin
  Result := ShellExtStateKnown and ShellExtWasRegisteredBeforeInstall;
end;

procedure RemoveRegistryAutoStart;
begin
  try
    if RegValueExists(HKCU, RegistryRunKey, RegistryRunValue) then
    begin
      if RegDeleteValue(HKCU, RegistryRunKey, RegistryRunValue) then
        Log('Removed registry auto-start value.')
      else
        Log('Failed to remove registry auto-start value.');
    end
    else
      Log('Registry auto-start value not found.');
  except
    Log('Exception while removing registry auto-start value.');
  end;
end;

procedure RemoveTaskSchedulerFolder;
var
  ScheduleService: Variant;
  RootFolder: Variant;
  TaskFolder: Variant;
  Tasks: Variant;
  Folders: Variant;
begin
  try
    ScheduleService := CreateOleObject('Schedule.Service');
    ScheduleService.Connect;

    RootFolder := ScheduleService.GetFolder('\');

    try
      TaskFolder := RootFolder.GetFolder('DisplayProfileManager');
    except
      Log('Task Scheduler folder not found.');
      Exit;
    end;

    Tasks := TaskFolder.GetTasks(0);
    Folders := TaskFolder.GetFolders(0);

    if (Tasks.Count = 0) and (Folders.Count = 0) then
    begin
      try
        RootFolder.DeleteFolder('DisplayProfileManager', 0);
        Log('Removed empty Task Scheduler folder.');
      except
        Log('Failed to remove empty Task Scheduler folder.');
      end;
    end
    else
      Log('Task Scheduler folder is not empty; leaving it in place.');
  except
    Log('Exception while removing Task Scheduler folder.');
  end;
end;

procedure CurUninstallStepChanged(UninstallStep: TUninstallStep);
begin
  if UninstallStep = usUninstall then
    RemoveRegistryAutoStart
  else if UninstallStep = usPostUninstall then
    RemoveTaskSchedulerFolder;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  NeedsRestart := False;
  Result := PrepareDpmForInstall;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if ShouldRegisterShellExtension then
    begin
      if (not ExecuteApplicationCommand('--shell', ewWaitUntilTerminated, ExitCode)) or (ExitCode <> 0) then
      begin
        MsgBox(
          'Display Profile Manager was installed successfully, but its Explorer ' +
          'right-click menu integration could not be re-enabled automatically.' + #13#10#13#10 +
          'You can re-enable it from within Display Profile Manager.',
          mbInformation,
          MB_OK);
      end;
    end;
  end;
end;

function IsDotNetDesktopInstalled: Boolean;
var
  ResultCode: Integer;
  Output: TExecOutput;
  I: Integer;
  Line: String;
begin
  Result := False;

  try
    if not ExecAndCaptureOutput(
      'dotnet.exe',
      '--list-runtimes',
      '',
      SW_SHOWNORMAL,
      ewWaitUntilTerminated,
      ResultCode,
      Output) then
      Exit;
  except
    Exit;
  end;

  if ResultCode <> 0 then
    Exit;

  for I := 0 to GetArrayLength(Output.StdOut) - 1 do
  begin
    Line := Trim(Output.StdOut[I]);

    if Pos('Microsoft.WindowsDesktop.App 10.', Line) = 1 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

procedure InitializeWizard;
begin
  DotNetPage := CreateCustomPage(
    wpLicense,
    '.NET 10 Desktop Runtime',
    'Display Profile Manager requires the .NET 10 Windows Desktop Runtime.');

  DotNetStatusLabel := TNewStaticText.Create(WizardForm);
  DotNetStatusLabel.Parent := DotNetPage.Surface;
  DotNetStatusLabel.Left := ScaleX(0);
  DotNetStatusLabel.Top := ScaleY(8);
  DotNetStatusLabel.Width := DotNetPage.SurfaceWidth;
  DotNetStatusLabel.Height := ScaleY(80);
  DotNetStatusLabel.AutoSize := False;
  DotNetStatusLabel.WordWrap := True;

  DotNetDownloadPage := CreateDownloadPage(
    'Downloading .NET 10 Desktop Runtime',
    'Setup is downloading the Microsoft Windows Desktop Runtime required by Display Profile Manager.',
    nil);

  DotNetDownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

procedure UpdateDotNetPage;
begin
  if IsDotNetDesktopInstalled then
  begin
    DotNetStatusLabel.Caption :=
      '.NET 10 Desktop Runtime is already installed.' + #13#10#13#10 +
      'Setup can continue installing Display Profile Manager.';
  end
  else
  begin
    DotNetStatusLabel.Caption :=
      'The .NET 10 Windows Desktop Runtime is required to run Display Profile Manager.' + #13#10#13#10 +
      'Click Next to download and install the Microsoft runtime. ' +
      'If the runtime installer requires elevation, Windows will request administrator approval.';
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := PageID = DotNetPage.ID;
  if Result then
    Result := IsDotNetDesktopInstalled;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = DotNetPage.ID then
    UpdateDotNetPage;
end;

function InstallDotNetDesktopRuntime: Boolean;
var
  InstallerPath: String;
  ResultCode: Integer;
begin
  Result := False;

  DotNetDownloadPage.Clear;
  DotNetDownloadPage.Add(
    '{#DotNetRuntimeUrl}',
    '{#DotNetInstallerName}',
    '');

  try
    DotNetDownloadPage.Show;
    try
      DotNetDownloadPage.Download;
      if DotNetDownloadPage.AbortedByUser then
        Exit;
    finally
      DotNetDownloadPage.Hide;
    end;
  except
    MsgBox(
      'The .NET 10 Desktop Runtime could not be downloaded.' + #13#10#13#10 +
      GetExceptionMessage,
      mbError,
      MB_OK);
    Exit;
  end;

  InstallerPath := ExpandConstant('{tmp}\{#DotNetInstallerName}');

  if not Exec(
    InstallerPath,
    '',
    '',
    SW_SHOWNORMAL,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    MsgBox(
      'The .NET 10 Desktop Runtime installer could not be started.' + #13#10#13#10 +
      SysErrorMessage(ResultCode),
      mbError,
      MB_OK);
    Exit;
  end;

  if (ResultCode <> 0) and (ResultCode <> 3010) then
  begin
    MsgBox(
      'The .NET 10 Desktop Runtime installation did not complete.' + #13#10#13#10 +
      'Microsoft installer exit code: ' + IntToStr(ResultCode),
      mbError,
      MB_OK);
    Exit;
  end;

  if not IsDotNetDesktopInstalled then
  begin
    MsgBox(
      'The .NET 10 Desktop Runtime is still not detected after installation.' + #13#10#13#10 +
      'Display Profile Manager cannot continue until the runtime is installed.',
      mbError,
      MB_OK);
    Exit;
  end;

  Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = DotNetPage.ID then
  begin
    if IsDotNetDesktopInstalled then
      Exit;

    Result := InstallDotNetDesktopRuntime;
  end;
end;
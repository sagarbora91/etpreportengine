#define AppName "ETP Reporting Engine"
#ifndef AppVersion
#define AppVersion "1.1.0"
#endif
#define AppPublisher "Saagar Traders"
#define AppExeName "Etp.Reporting.Desktop.exe"
#define AppMutexName "Global\EtpReportingEngineRunning"
#ifndef ReleaseDirectory
#define ReleaseDirectory "..\artifacts\windows-release"
#endif
#ifndef InstallerOutputDirectory
#define InstallerOutputDirectory "..\artifacts\installer"
#endif

[Setup]
AppId={{9FB6D99C-2EE3-48BC-B342-8E80F6D81FF5}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Saagar Traders\ETP Reporting Engine
DefaultGroupName={#AppName}
AppMutex={#AppMutexName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\src\Etp.Reporting.Desktop\Assets\EtpReporting.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir={#InstallerOutputDirectory}
OutputBaseFilename=EtpReportingEngine-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes

[Files]
Source: "{#ReleaseDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; P4-11. The SQL media travels inside the installer only when the build supplied
; it. Without it there is no option and no licence prompt, so setup never offers
; an installation it cannot perform.
#ifdef SqlPayloadDirectory
; The media runs with the administrator's full token, so it sits where only administrators
; can change it - inside the protected installation folder, never setup's {tmp} under the
; user profile, which bootstrap's ownership/ACL check refuses. Copied only when the option
; is ticked, and deleted again as soon as bootstrap returns.
Source: "{#SqlPayloadDirectory}\*"; DestDir: "{app}\SqlSetupMedia"; Tasks: sqlprerequisites; Flags: deleteafterinstall recursesubdirs createallsubdirs
#endif

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
#ifdef SqlPayloadDirectory
Name: "sqlprerequisites"; Description: "Install Microsoft SQL Server 2025 Express, its ODBC drivers and Sqlcmd from the media included with this installer (accepts Microsoft's licence terms)"; GroupDescription: "Optional database prerequisites:"; Flags: checkedonce
#endif

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\SqlSetupMedia"
Type: files; Name: "{app}\SETUP-INCOMPLETE.txt"

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy RemoteSigned -File ""{app}\scripts\remove-etp-scheduled-tasks.ps1"" -ApplicationDirectory ""{app}"""; RunOnceId: "RemoveEtpScheduledTasks"; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
// P4-12. Setup used to raise an exception when its mandatory post-install step
// failed and still return exit code 0, so a silent or scripted install reported
// success while leaving the application registered against a database that was
// never migrated. Reproduced on a machine with a legacy database and on a clean
// unprovisioned one. Inno offers no supported way to choose its exit code, so
// take the process exit directly.
procedure ExitProcess(uExitCode: UINT);
  external 'ExitProcess@kernel32.dll stdcall';

const
  SetupIncompleteExitCode = 1603;

var
  MandatorySetupFailed: Boolean;

procedure RecordSetupOutcome(Succeeded: Boolean; Detail: String);
var
  Marker: String;
begin
  Marker := ExpandConstant('{app}\SETUP-INCOMPLETE.txt');
  if Succeeded then
    DeleteFile(Marker)
  else
  begin
    SaveStringToFile(Marker, 'Setup did not complete. ' + Detail + #13#10 +
      'Setup''s post-install step (SQL Server preparation, database migration or health validation) failed, so this installation is not ready to use.' + #13#10 +
      'Do not launch ETP. Review %ProgramData%\EtpReporting\SetupLogs, or setup''s own log in %TEMP% if that folder does not exist yet, and run setup again.' + #13#10, False);
    DeleteFile(ExpandConstant('{group}\{#AppName}.lnk'));
    DeleteFile(ExpandConstant('{autodesktop}\{#AppName}.lnk'));
  end;
end;

procedure DeinitializeSetup();
begin
  if MandatorySetupFailed then
    ExitProcess(SetupIncompleteExitCode);
end;

// P4-12c. Upgrading while ETP is open used to fail on the locked executable, show a
// suppressed Abort/Retry/Ignore dialog, roll back and exit 5 - which reads as 'user
// cancelled'. Refuse up front instead, with the same deliberate exit code as any other
// setup failure. Setup never closes the application itself: it may be holding cash or
// walk-in entries behind its own unsaved-draft guards.
function InitializeSetup(): Boolean;
begin
  Result := True;
  if CheckForMutexes('{#AppMutexName}') then
  begin
    Result := False;
    MandatorySetupFailed := True;
    if not WizardSilent then
      MsgBox('ETP Reporting Engine is running. Close it and run setup again. Setup will not close it for you, because it may be holding entries that have not been saved.', mbError, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Parameters: String;
  Launched: Boolean;
begin
  if (CurStep = ssPostInstall) then
  begin
    Parameters := '-NoProfile -ExecutionPolicy RemoteSigned -File "' + ExpandConstant('{app}\scripts\bootstrap-etp-prerequisites.ps1') + '" -ApplicationDirectory "' + ExpandConstant('{app}') + '"';
#ifdef SqlPayloadDirectory
    if WizardIsTaskSelected('sqlprerequisites') then
      Parameters := Parameters + ' -SqlPayloadDirectory "' + ExpandConstant('{app}\SqlSetupMedia') + '"'
    else
      Parameters := Parameters + ' -SkipSqlInstallation';
#else
    Parameters := Parameters + ' -SkipSqlInstallation';
#endif
    Launched := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
#ifdef SqlPayloadDirectory
    // The media has done its job whatever the outcome; do not leave ~800 MB of installers
    // behind. Removed here, before any exception below and before DeinitializeSetup's
    // ExitProcess, which can skip setup's own deleteafterinstall clean-up.
    DelTree(ExpandConstant('{app}\SqlSetupMedia'), True, True, True);
#endif
    if (not Launched) or (ResultCode <> 0) then
    begin
      MandatorySetupFailed := True;
      RecordSetupOutcome(False, 'Bootstrap exit code: ' + IntToStr(ResultCode) + '.');
      // A silent install has no interactive desktop. This dialog then blocks forever
      // instead of failing: an unattended deployment hung here for forty minutes
      // with the failure already decided. Show it only when someone can answer it;
      // the marker file and the log carry the same message either way.
      if not WizardSilent then
        MsgBox('Setup''s post-install step (SQL Server preparation, database migration or health validation) failed after application files were installed. No automatic restore or database deletion was attempted. Do not launch ETP until setup completes successfully; review %ProgramData%\EtpReporting\SetupLogs (or setup''s own log in %TEMP% if that folder does not exist yet) and retry.', mbError, MB_OK);
      RaiseException('Mandatory SQL Server preparation, database migration or health validation failed; setup cannot be completed safely.');
    end
    else
      RecordSetupOutcome(True, '');
  end;
end;

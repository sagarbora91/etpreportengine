#define AppName "ETP Reporting Engine"
; Dependency 10. No default version: the build script (build-windows-installer.ps1) always passes
; /DAppVersion from Directory.Build.props, and a hand-run compile without it must fail, not label
; the installer 1.1.0.
#ifndef AppVersion
#error AppVersion is not defined. Build with scripts\build-windows-installer.ps1 (it passes /DAppVersion=<VersionPrefix>), or pass /DAppVersion=x.y.z.
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
; 1.12 (B1). The in-app update compares the setup file's ProductVersion with the signed release.json before running it,
; so the version must be exactly AppVersion ("1.12.0"), not Inno's default.
VersionInfoVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}

[InstallDelete]
; 1.12.1 IR-8. Object files are re-copied on every install; a renamed or re-layered file (30- to 40-) must not linger beside
; its replacement, or the runner sees two files for one object. Deleted before [Files] copies the new set.
Type: filesandordirs; Name: "{app}\database\objects"

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
; Unticked, setup prepares SQL Server and the protected configuration but does not create a
; missing database, so a backup from another PC can be restored first (restore-etp-database.ps1).
; An empty database created now could only be replaced by a restore WITH REPLACE, which the
; recovery procedures never do. An existing database is upgraded either way.
Name: "newdatabase"; Description: "Create a new empty ETP database if this PC has none (untick only if you will restore an existing ETP backup first)"; GroupDescription: "Database:"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent; Check: ShouldOfferLaunch
; 1.12 (B1). An in-app update (/ETPUPDATE=1, always silent) starts ETP again, only when setup succeeded, and as the user who
; started setup - not as the administrator setup elevated to (ETP is never meant to run elevated; AUDIT-2 S-13).
Filename: "{app}\{#AppExeName}"; Description: "Start {#AppName} again"; Flags: nowait postinstall runasoriginaluser; Check: ShouldRestartAfterUpdate

[UninstallDelete]
Type: filesandordirs; Name: "{app}\SqlSetupMedia"
Type: files; Name: "{app}\SETUP-INCOMPLETE.txt"
Type: files; Name: "{app}\DATABASE-RESTORE-PENDING.txt"

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
  // Bootstrap's "SQL Server and the configuration are ready; restore the database, then run
  // setup again". It must equal $DatabaseRestorePendingExitCode in
  // scripts\bootstrap-etp-prerequisites.ps1, and it is a success only when "Create a new
  // empty database" was unticked - anything else that returns it is still a failure.
  DatabaseRestorePendingExitCode = 2;

var
  MandatorySetupFailed: Boolean;
  DatabaseRestorePending: Boolean;
  // 1.12 (B1): for setup-result.json of an in-app update.
  SetupFinished: Boolean;
  EtpStillRunning: Boolean;

// A failed setup, or one waiting for a restore, has no database ETP can open yet.
function ShouldOfferLaunch(): Boolean;
begin
  Result := (not MandatorySetupFailed) and (not DatabaseRestorePending);
end;

// 1.12 (B1). Started by ETP's in-app update: /ETPUPDATE=1 /ETPATTEMPT=<32 lower-case hex>.
function IsEtpUpdate(): Boolean;
begin
  Result := ExpandConstant('{param:ETPUPDATE|0}') = '1';
end;

// The attempt id is copied into setup-result.json, so only the exact form ETP writes is accepted.
function EtpUpdateAttemptId(): String;
var
  I: Integer;
begin
  Result := ExpandConstant('{param:ETPATTEMPT|}');
  if Length(Result) <> 32 then
  begin
    Result := '';
    Exit;
  end;
  for I := 1 to Length(Result) do
    if Pos(Result[I], '0123456789abcdef') = 0 then
    begin
      Result := '';
      Exit;
    end;
end;

function ShouldRestartAfterUpdate(): Boolean;
begin
  Result := IsEtpUpdate() and ShouldOfferLaunch();
end;

// %ProgramData%\EtpReporting\Updates\setup-result.json, read by ETP at its next start (UpdateSetupResult). Fixed reason
// codes only. Written only when the folder exists (setup prepares it; an in-app update cannot start without it).
procedure RecordEtpUpdateResult();
var
  Folder, Outcome, Reason: String;
begin
  Folder := ExpandConstant('{commonappdata}\EtpReporting\Updates');
  if not DirExists(Folder) then Exit;
  if EtpStillRunning then
  begin
    Outcome := 'FAILED'; Reason := 'ETP_STILL_RUNNING';
  end
  else if MandatorySetupFailed then
  begin
    Outcome := 'FAILED'; Reason := 'SETUP_STEP_FAILED';
  end
  else if DatabaseRestorePending then
  begin
    Outcome := 'FAILED'; Reason := 'DATABASE_RESTORE_PENDING';
  end
  else if SetupFinished then
  begin
    Outcome := 'SUCCEEDED'; Reason := '';
  end
  else
  begin
    Outcome := 'CANCELLED'; Reason := 'SETUP_CANCELLED';
  end;
  SaveStringToFile(Folder + '\setup-result.json',
    '{"attemptId":"' + EtpUpdateAttemptId() + '","version":"{#AppVersion}","outcome":"' + Outcome + '","reason":"' + Reason + '"}', False);
end;

function RestoreInstructions(): String;
begin
  Result := 'SQL Server and the protected ETP configuration are ready. No database was created, because "Create a new empty database" was not ticked.' + #13#10 + #13#10 +
    'Next:' + #13#10 +
    '1. In an administrator PowerShell window run:' + #13#10 +
    'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\scripts\restore-etp-database.ps1') + '" -BackupPath "<full path of the .bak>"' + #13#10 +
    '(add -ReceiptPath "<the .bak.receipt.json>" if you have it).' + #13#10 +
    '2. Run this setup again. It takes a verified safety backup of the restored data before it updates it.' + #13#10;
end;

procedure RecordSetupOutcome(Succeeded: Boolean; Detail: String);
var
  Marker: String;
begin
  DeleteFile(ExpandConstant('{app}\DATABASE-RESTORE-PENDING.txt'));
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

// Not a failure, and not ready either: the application stays installed, and the marker
// says what to do next for anyone who missed the message.
procedure RecordRestorePending();
begin
  DeleteFile(ExpandConstant('{app}\SETUP-INCOMPLETE.txt'));
  SaveStringToFile(ExpandConstant('{app}\DATABASE-RESTORE-PENDING.txt'), RestoreInstructions(), False);
end;

procedure DeinitializeSetup();
begin
  if IsEtpUpdate() then
    RecordEtpUpdateResult();
  if MandatorySetupFailed then
    ExitProcess(SetupIncompleteExitCode);
end;

// P4-12c. Upgrading while ETP is open used to fail on the locked executable, show a
// suppressed Abort/Retry/Ignore dialog, roll back and exit 5 - which reads as 'user
// cancelled'. Refuse up front instead, with the same deliberate exit code as any other
// setup failure. Setup never closes the application itself: it may be holding cash or
// walk-in entries behind its own unsaved-draft guards.
function InitializeSetup(): Boolean;
var
  Waited: Integer;
begin
  Result := True;
  // 1.12 (B1). ETP starts this setup and then closes itself: give it up to two minutes to finish closing. After that the
  // rule below is unchanged: setup never closes ETP.
  if IsEtpUpdate() then
  begin
    Waited := 0;
    while CheckForMutexes('{#AppMutexName}') and (Waited < 240) do
    begin
      Sleep(500);
      Waited := Waited + 1;
    end;
  end;
  if CheckForMutexes('{#AppMutexName}') then
  begin
    Result := False;
    MandatorySetupFailed := True;
    EtpStillRunning := True;
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
    if not WizardIsTaskSelected('newdatabase') then
      Parameters := Parameters + ' -DeferDatabaseCreation';
    Launched := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
#ifdef SqlPayloadDirectory
    // The media has done its job whatever the outcome; do not leave ~800 MB of installers
    // behind. Removed here, before any exception below and before DeinitializeSetup's
    // ExitProcess, which can skip setup's own deleteafterinstall clean-up.
    DelTree(ExpandConstant('{app}\SqlSetupMedia'), True, True, True);
#endif
    if Launched and (ResultCode = DatabaseRestorePendingExitCode) and (not WizardIsTaskSelected('newdatabase')) then
    begin
      DatabaseRestorePending := True;
      RecordRestorePending();
      if not WizardSilent then
        MsgBox(RestoreInstructions(), mbInformation, MB_OK);
    end
    else if (not Launched) or (ResultCode <> 0) then
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
  if CurStep = ssDone then
    SetupFinished := True;
end;

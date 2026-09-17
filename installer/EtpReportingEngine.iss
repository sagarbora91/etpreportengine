#define AppName "ETP Reporting Engine"
#ifndef AppVersion
#define AppVersion "1.1.0"
#endif
#define AppPublisher "Saagar Traders"
#define AppExeName "Etp.Reporting.Desktop.exe"
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
Source: "{#SqlPayloadDirectory}\*"; DestDir: "{tmp}\SqlPayload"; Flags: deleteafterinstall recursesubdirs createallsubdirs
#endif

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
#ifdef SqlPayloadDirectory
Name: "sqlprerequisites"; Description: "Install Microsoft SQL Server 2022 Express and Sqlcmd from the media included with this installer (accepts Microsoft's licence terms)"; GroupDescription: "Optional database prerequisites:"; Flags: checkedonce
#endif

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy RemoteSigned -File ""{app}\scripts\remove-etp-scheduled-tasks.ps1"" -ApplicationDirectory ""{app}"""; RunOnceId: "RemoveEtpScheduledTasks"; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Parameters: String;
begin
  if (CurStep = ssPostInstall) then
  begin
    Parameters := '-NoProfile -ExecutionPolicy RemoteSigned -File "' + ExpandConstant('{app}\scripts\bootstrap-etp-prerequisites.ps1') + '" -ApplicationDirectory "' + ExpandConstant('{app}') + '"';
#ifdef SqlPayloadDirectory
    if WizardIsTaskSelected('sqlprerequisites') then
      Parameters := Parameters + ' -SqlPayloadDirectory "' + ExpandConstant('{tmp}\SqlPayload') + '"'
    else
      Parameters := Parameters + ' -SkipSqlInstallation';
#else
    Parameters := Parameters + ' -SkipSqlInstallation';
#endif
    if (not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)) or (ResultCode <> 0) then
    begin
      MsgBox('Mandatory database migration and health validation failed after application files were installed. No automatic restore or database deletion was attempted. Do not launch ETP until setup completes successfully; review %ProgramData%\EtpReporting\SetupLogs and retry.', mbError, MB_OK);
      RaiseException('Mandatory database migration and health validation failed; setup cannot be completed safely.');
    end;
  end;
end;

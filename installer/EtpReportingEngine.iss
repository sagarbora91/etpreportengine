#define AppName "ETP Reporting Engine"
#ifndef AppVersion
#define AppVersion "1.1.0"
#endif
#define AppPublisher "Saagar Traders"
#define AppExeName "Etp.Reporting.Desktop.exe"

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
OutputDir=..\artifacts\installer
OutputBaseFilename=EtpReportingEngine-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes

[Files]
Source: "..\artifacts\windows-release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "sqlbootstrap"; Description: "Install and configure Microsoft SQL Server 2022 Express when required (accepts Microsoft's SQL Server license terms)"; GroupDescription: "Database prerequisites:"; Flags: checkedonce

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\remove-etp-scheduled-tasks.ps1"" -ApplicationDirectory ""{app}"""; RunOnceId: "RemoveEtpScheduledTasks"; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
procedure ExitProcess(ExitCode: Integer);
  external 'ExitProcess@kernel32.dll stdcall';

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Parameters: String;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('sqlbootstrap') then
  begin
    Parameters := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\scripts\bootstrap-etp-prerequisites.ps1') + '" -ApplicationDirectory "' + ExpandConstant('{app}') + '"';
    if (not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)) or (ResultCode <> 0) then
    begin
      MsgBox('SQL Server and database configuration failed. Review %ProgramData%\EtpReporting\SetupLogs before retrying.', mbError, MB_OK);
      ExitProcess(1);
    end;
  end;
end;

[Setup]
AppName=LogFileCollector
DefaultDirName={commonpf}\Virinco\LogFileCollector
DefaultGroupName=LogFileCollector
OutputDir=Output
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
AppVersion=1.4
AppVerName=LogFileCollector 1.4
OutputBaseFilename=LogFileCollectorSetup-1.4
VersionInfoVersion=1.4.0.0
VersionInfoCompany=Virinco AS
VersionInfoDescription=LogFileCollector Installer
VersionInfoProductName=LogFileCollector
VersionInfoCopyright=Copyright © Virinco AS 2025

[Files]
; Install binaries to Program Files.
; The project multi-targets, so the .NET Framework output now sits under net48\.
; Build with: dotnet build -c Release -f net48
Source: "bin\Release\net48\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Place config in ProgramData (only if not already there)
Source: "appsettings.json"; DestDir: "{commonappdata}\Virinco\WATS\LogFileCollector"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\LogFileCollector"; Filename: "{app}\LogFileCollector.exe"

[Run]
; Open appsettings.json in Notepad after install
Filename: "notepad.exe"; \
  Parameters: """{commonappdata}\Virinco\WATS\LogFileCollector\appsettings.json"""; \
  Description: "Edit configuration"; \
  Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; Remove scheduled task on uninstall
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN \Virinco\LogFileCollector /F"; Flags: runhidden

[Tasks]
Name: createtask; Description: "Create scheduled task in Task Scheduler (\Virinco\LogFileCollector, run as NetworkService with --rescan)"; GroupDescription: "Additional tasks:"; Flags: unchecked

[Code]
procedure LogToFile(const Msg: string);
var
  LogPath, DateTimeStr: string;
  SL: TStringList;
begin
  LogPath := ExpandConstant('{commonappdata}\Virinco\WATS\LogFileCollector\setup-task.log');
  ForceDirectories(ExtractFileDir(LogPath));

  SL := TStringList.Create;
  try
    if FileExists(LogPath) then
      SL.LoadFromFile(LogPath);

    DateTimeStr := GetDateTimeString('yyyy-mm-dd hh:nn:ss', #0, #0);
    SL.Add(DateTimeStr + ' ' + Msg);

    SL.SaveToFile(LogPath);
  finally
    SL.Free;
  end;
end;

procedure CreateTaskXml(const XmlPath, ExePath: string);
var
  SL: TStringList;
begin
  SL := TStringList.Create;
  try
    SL.Text :=
      '<?xml version="1.0" encoding="UTF-16"?>' + #13#10 +
      '<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">' +
      '  <RegistrationInfo>' +
      '    <Author>Virinco AS</Author>' +
      '    <Description>LogFileCollector scheduled task</Description>' +
      '  </RegistrationInfo>' +
      '  <Triggers>' +
      '    <BootTrigger>' +
      '      <Enabled>true</Enabled>' +
      '    </BootTrigger>' +
      '  </Triggers>' +
      '  <Principals>' +
      '    <Principal id="Author">' +
      '      <UserId>S-1-5-20</UserId>' + // NetworkService SID
      '      <RunLevel>LeastPrivilege</RunLevel>' +
      '    </Principal>' +
      '  </Principals>' +
      '  <Settings>' +
      '    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>' +
      '    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>' +
      '    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>' +
      '    <AllowHardTerminate>true</AllowHardTerminate>' +
      '    <StartWhenAvailable>true</StartWhenAvailable>' +
      '    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>' +
      '    <IdleSettings>' +
      '      <StopOnIdleEnd>false</StopOnIdleEnd>' +
      '      <RestartOnIdle>false</RestartOnIdle>' +
      '    </IdleSettings>' +
      '    <AllowStartOnDemand>true</AllowStartOnDemand>' +
      '    <Enabled>true</Enabled>' +
      '    <Hidden>false</Hidden>' +
      '    <RunOnlyIfIdle>false</RunOnlyIfIdle>' +
      '    <WakeToRun>false</WakeToRun>' +
      '    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>' +
      '    <Priority>7</Priority>' +
      '  </Settings>' +
      '  <Actions Context="Author">' +
      '    <Exec>' +
      '      <Command>' + ExePath + '</Command>' +
      '      <Arguments>--rescan</Arguments>' +
      '    </Exec>' +
      '  </Actions>' +
      '</Task>';
    SL.SaveToFile(XmlPath);
  finally
    SL.Free;
  end;
end;

procedure CreateTaskViaXml();
var
  ResultCode: Integer;
  XmlPath, TaskExe, Args, ExePath: String;
begin
  TaskExe := ExpandConstant('{sys}\schtasks.exe');
  XmlPath := ExpandConstant('{commonappdata}\Virinco\WATS\LogFileCollector\LogFileCollector.xml');
  ExePath := ExpandConstant('{app}\LogFileCollector.exe');

  ForceDirectories(ExtractFileDir(XmlPath));
  CreateTaskXml(XmlPath, ExePath);

  LogToFile('Task XML created at: ' + XmlPath);

  Exec(TaskExe, '/Delete /TN "\Virinco\LogFileCollector" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  LogToFile('Deleting old scheduled task if present...');

  Args := '/Create /TN "\Virinco\LogFileCollector" /RU "NT AUTHORITY\NetworkService" /XML "' + XmlPath + '" /F';
  if Exec(TaskExe, Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    LogToFile('Scheduled task created successfully. Run this manually to test: ' + TaskExe + ' ' + Args)
  else
    LogToFile('Failed to create scheduled task. Command: ' + TaskExe + ' ' + Args);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('createtask') then
    CreateTaskViaXml();
end;

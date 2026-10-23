#ifndef MyAppVersion
#define MyAppVersion "0.1.0"
#endif

[Setup]
AppName=Umber
AppVersion={#MyAppVersion}
UninstallDisplayName=Umber
AppId=Umber
DefaultDirName={localappdata}\Umber
DefaultGroupName=Umber
UninstallDisplayIcon={app}\Umber.exe
Compression=lzma2
SolidCompression=yes
OutputDir=Releases
OutputBaseFilename=Umber-win-Setup
SetupIconFile=icons\umber.ico
PrivilegesRequired=lowest
CloseApplications=force
RestartApplications=no

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Umber"; Filename: "{app}\Umber.exe"; AppUserModelID: "Umber"
Name: "{autodesktop}\Umber"; Filename: "{app}\Umber.exe"; Tasks: desktopicon; AppUserModelID: "Umber"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Registry]
; Remove a stale Velopack/Squirrel uninstall key so Control Panel never lists both
; the packaged entry and the Inno Setup entry.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\Umber"; Flags: deletekey dontcreatekey uninsdeletekey

[Run]
Filename: "{app}\Umber.exe"; Description: "Launch Umber"; Flags: nowait postinstall skipifsilent

[Code]
procedure SHChangeNotify(lEvent: Longint; uFlags: Cardinal; dwItem1: Longint; dwItem2: Longint); external 'SHChangeNotify@shell32.dll stdcall';

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    SHChangeNotify($08000000, 0, 0, 0);
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpReady then
  begin
    if RegKeyExists(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Umber_is1') or
       RegKeyExists(HKEY_LOCAL_MACHINE, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Umber_is1') or
       RegKeyExists(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Umber') then
    begin
      WizardForm.NextButton.Caption := '&Upgrade';
    end;
  end;
end;




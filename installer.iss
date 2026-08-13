[Setup]
AppName=Stride
AppVersion=1.1.3
DefaultDirName={localappdata}\Stride
DefaultGroupName=Stride
UninstallDisplayIcon={app}\Stride.exe
Compression=lzma2
SolidCompression=yes
OutputDir=Releases
OutputBaseFilename=Stride-win-Setup
SetupIconFile=icons\stride.ico
PrivilegesRequired=lowest
CloseApplications=force
RestartApplications=no

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Stride"; Filename: "{app}\Stride.exe"
Name: "{autodesktop}\Stride"; Filename: "{app}\Stride.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Registry]
; Remove the old Velopack/Squirrel uninstall registry key so users don't see two Stride entries in Control Panel
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\Stride"; Flags: deletekey dontcreatekey uninsdeletekey

[Run]
Filename: "{app}\Stride.exe"; Description: "Launch Stride"; Flags: nowait postinstall skipifsilent

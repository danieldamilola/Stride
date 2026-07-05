[Setup]
AppName=Stride Browser
AppVersion=1.0.0
AppPublisher=Daniel Damilola
AppId={{5C1B8F3D-4A2D-4B8F-8C7D-2E3D4F5A6B7C}
DefaultDirName={pf}\StrideBrowser
DefaultGroupName=Stride Browser
UninstallDisplayIcon={app}\Stride.exe
Compression=lzma2
SolidCompression=yes
OutputDir=C:\dev\SpurBrowser\Output
OutputBaseFilename=StrideSetup
PrivilegesRequired=lowest

[Files]
Source: "C:\dev\SpurBrowser\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Stride"; Filename: "{app}\Stride.exe"
Name: "{commondesktop}\Stride"; Filename: "{app}\Stride.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\Stride.exe"; Description: "Launch Stride Browser"; Flags: nowait postinstall skipifsilent

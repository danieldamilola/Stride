; Stride Browser — Inno Setup Installer Script
; Requires Inno Setup 6+

#define MyAppName "Stride"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Daniel Damilola"
#define MyAppURL "https://stride.browser"
#define MyAppExeName "Stride.exe"
#define MySourceDir "bin\Release\net9.0-windows\win-x64\publish"

[Setup]
AppId={{B8F2A9D1-3E7C-4A5B-9D6F-1C2E8F4A7B3D}
AppName={#MyAppName}
UninstallDisplayName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=installer
OutputBaseFilename=Stride-Setup
SetupIconFile=icons\stride.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DisableWelcomePage=no


; Branding
AppContact={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoDescription=Stride Web Browser

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "taskbarpin"; Description: "Pin to taskbar"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main application files and self-contained runtime
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up user data on uninstall (optional — only cache, not settings)
Type: files; Name: "{localappdata}\StrideBrowser\favicons\*"
Type: dirifempty; Name: "{localappdata}\StrideBrowser\favicons"

[Code]


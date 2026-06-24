; Stride Browser — Inno Setup Installer Script
; Requires Inno Setup 6+

#define MyAppName "Stride"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Stride"
#define MyAppURL "https://stride.browser"
#define MyAppExeName "Stride.exe"
#define MySourceDir "publish"

[Setup]
AppId={{B8F2A9D1-3E7C-4A5B-9D6F-1C2E8F4A7B3D}
AppName={#MyAppName}
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
; Main application files
Source: "{#MySourceDir}\Stride.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\Stride.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\Stride.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\Stride.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion

; Dependencies
Source: "{#MySourceDir}\CommunityToolkit.Mvvm.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\Microsoft.Extensions.DependencyInjection.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\Microsoft.Extensions.DependencyInjection.Abstractions.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\Microsoft.Web.WebView2.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\Microsoft.Web.WebView2.Wpf.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\Microsoft.Web.WebView2.WinForms.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySourceDir}\WebView2Loader.dll"; DestDir: "{app}"; Flags: ignoreversion

; Native runtime
Source: "{#MySourceDir}\runtimes\win-x64\native\*"; DestDir: "{app}\runtimes\win-x64\native"; Flags: ignoreversion recursesubdirs

; Icons
Source: "{#MySourceDir}\icons\*"; DestDir: "{app}\icons"; Flags: ignoreversion recursesubdirs

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
// Check if .NET 9 Desktop Runtime is installed
function IsDotNet9Installed(): Boolean;
var
  ResultCode: Integer;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') or
            RegKeyExists(HKCU, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
  if Result then
  begin
    // Check for version 9.x specifically
    Result := Exec('dotnet', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  // Warn if .NET 9 might not be installed
  if not IsDotNet9Installed() then
  begin
    if MsgBox('Stride requires the .NET 9 Desktop Runtime.' + #13#10 + #13#10 +
              'If Stride fails to start, download it from:' + #13#10 +
              'https://dotnet.microsoft.com/download/dotnet/9.0' + #13#10 + #13#10 +
              'Continue installation?', mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;

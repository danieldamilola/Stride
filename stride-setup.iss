; Stride Browser — Inno Setup Installer Script
; Requires Inno Setup 6+

#define MyAppName "Stride"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "Daniel Damilola"
#define MyAppURL "https://stride.browser"
#define MyAppExeName "Stride.exe"
#define MySourceDir "bin\Release\net9.0-windows\publish"

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

; Native runtime
Source: "{#MySourceDir}\runtimes\win-x64\native\*"; DestDir: "{app}\runtimes\win-x64\native"; Flags: ignoreversion recursesubdirs

; Icons
Source: "{#MySourceDir}\icons\*"; DestDir: "{app}\icons"; Flags: ignoreversion recursesubdirs
Source: "{#MySourceDir}\Resources\*"; DestDir: "{app}\Resources"; Flags: ignoreversion recursesubdirs

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
function IsDotNet9Installed(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App\9.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function GetInstalledVersion(): String;
var
  InstalledVersion: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8F2A9D1-3E7C-4A5B-9D6F-1C2E8F4A7B3D}_is1', 'DisplayVersion', InstalledVersion) then
    Result := InstalledVersion
  else if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8F2A9D1-3E7C-4A5B-9D6F-1C2E8F4A7B3D}_is1', 'DisplayVersion', InstalledVersion) then
    Result := InstalledVersion;
end;

function CompareVersionStr(V1, V2: string): Integer;
var
  P1, P2: Integer;
  N1, N2: Integer;
begin
  Result := 0;
  while (V1 <> '') or (V2 <> '') do
  begin
    P1 := Pos('.', V1);
    if P1 > 0 then begin N1 := StrToIntDef(Copy(V1, 1, P1 - 1), 0); Delete(V1, 1, P1); end
    else begin N1 := StrToIntDef(V1, 0); V1 := ''; end;

    P2 := Pos('.', V2);
    if P2 > 0 then begin N2 := StrToIntDef(Copy(V2, 1, P2 - 1), 0); Delete(V2, 1, P2); end
    else begin N2 := StrToIntDef(V2, 0); V2 := ''; end;

    if N1 > N2 then begin Result := 1; Exit; end;
    if N1 < N2 then begin Result := -1; Exit; end;
  end;
end;

function InitializeSetup(): Boolean;
var
  InstalledVersion: String;
begin
  Result := True;
  InstalledVersion := GetInstalledVersion();
  if InstalledVersion <> '' then
  begin
    if CompareVersionStr(InstalledVersion, '{#MyAppVersion}') > 0 then
    begin
      MsgBox('A newer version (' + InstalledVersion + ') of {#MyAppName} is already installed.' + #13#13 +
             'This installer is for an older version ({#MyAppVersion}). Setup will now exit.', mbInformation, MB_OK);
      Result := False;
    end;
  end;
end;

var
  DownloadPage: TDownloadWizardPage;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and not IsDotNet9Installed() then
  begin
    if MsgBox('Stride requires the Microsoft .NET 9 Desktop Runtime (~58 MB) to work fully.' + #13#10 + #13#10 +
              'Would you like to download and install it now?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      DownloadPage.Clear;
      DownloadPage.Add('https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe', 'dotnet9.exe', '');
      DownloadPage.Show;
      try
        try
          DownloadPage.Download;
        except
          SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
          Result := False;
        end;
      finally
        DownloadPage.Hide;
      end;
    end
    else
    begin
      Result := False;
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpReady then
  begin
    if GetInstalledVersion() <> '' then
    begin
      WizardForm.NextButton.Caption := 'Upgrade';
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  if not IsDotNet9Installed() then
  begin
    if not Exec(ExpandConstant('{tmp}\dotnet9.exe'), '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    begin
      Result := 'Failed to install .NET 9 Desktop Runtime automatically. Please install it manually.';
    end;
  end;
end;

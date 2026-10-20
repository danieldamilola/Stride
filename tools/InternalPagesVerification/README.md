# Internal page verification

The Windows harness loads the compiled Settings page in an isolated WebView2 profile. It changes controls through the page scripts, routes their messages through the browser bridge and Settings handler, and compares saved preferences with the live model. It also checks reopening Settings and rejects commands from an unregistered document.

The harness exports all ten internal pages in both themes. The browser check uses those rendered files to test search, keyboard editing, update error recovery, contrast, narrow layouts, and dialogs. It saves screenshots beside the HTML files.

Run from the repository root in PowerShell. Keep the output in a dedicated temporary directory so the build can run while Stride is open.

```powershell
$verificationOutput = Join-Path $env:TEMP 'StridePagesVerification'
dotnet build tools/InternalPagesVerification/InternalPagesVerification.csproj '-p:DefaultItemExcludesInProjectFolder=tools/**' "-p:OutDir=$verificationOutput/app/"
& "$verificationOutput/app/InternalPagesVerification.exe" "$verificationOutput/evidence"
```

The browser check requires an existing Playwright installation. Set `STRIDE_PLAYWRIGHT` to its module path if it is outside Node's module search path.

```powershell
node tools/InternalPagesVerification/browser-check.cjs "$verificationOutput/evidence"
```

The application has no new package dependency. Native extension downloads are excluded from the harness. The unit suite checks the ad blocking preference alongside the other switches.

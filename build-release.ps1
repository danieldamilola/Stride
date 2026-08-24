$ErrorActionPreference = "Stop"

# 1. Kill Stride if running
Write-Host "Stopping Stride processes..." -ForegroundColor Cyan
Stop-Process -Name "Stride" -Force -ErrorAction SilentlyContinue

# 2. Extract Version from csproj
$csprojPath = "Stride.csproj"
[xml]$projectXml = Get-Content $csprojPath
$version = $projectXml.Project.PropertyGroup.Version
if (-not $version) {
    Write-Host "Error: Could not find <Version> tag in $csprojPath" -ForegroundColor Red
    exit 1
}
Write-Host "Building release for Version: $version" -ForegroundColor Green

# 3. Publish the app and updater
# Clean previous publish to avoid stale files from removed features
if (Test-Path ".\publish") { Remove-Item ".\publish" -Recurse -Force }
New-Item -ItemType Directory -Path ".\publish" | Out-Null
if (-not (Test-Path ".\Releases")) { New-Item -ItemType Directory -Path ".\Releases" | Out-Null }

Write-Host "Publishing Stride.Updater..." -ForegroundColor Cyan
dotnet publish Stride.Updater\Stride.Updater.csproj -c Release -r win-x64 --self-contained true -o .\publish

Write-Host "Publishing Stride..." -ForegroundColor Cyan
dotnet publish Stride.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish

# 4. Create ZIP for Auto-Updates - atomic via temp file
$zipPath = ".\Releases\Stride-win-x64.zip"
$zipTemp = ".\Releases\Stride-win-x64_tmp.zip"
if (Test-Path $zipTemp) { Remove-Item $zipTemp -Force }
Write-Host "Creating ZIP archive for auto-updates at $zipPath..." -ForegroundColor Cyan
Compress-Archive -Path ".\publish\*" -DestinationPath $zipTemp -Force
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Move-Item -Path $zipTemp -Destination $zipPath

# 5. Pack with Inno Setup for First-Time Installs
Write-Host "Packaging with Inno Setup for new users..." -ForegroundColor Cyan
& "$env:USERPROFILE\AppData\Local\Programs\Inno Setup 6\ISCC.exe" "/DMyAppVersion=$version" "installer.iss"

Write-Host "Done! Release created in .\Releases" -ForegroundColor Green

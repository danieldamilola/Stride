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

# 3. Publish the app
Write-Host "Publishing project..." -ForegroundColor Cyan
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish

# 4. Pack with Inno Setup
Write-Host "Packaging with Inno Setup..." -ForegroundColor Cyan
& "$env:USERPROFILE\AppData\Local\Programs\Inno Setup 6\ISCC.exe" "installer.iss"

Write-Host "Done! Release created in .\Releases" -ForegroundColor Green

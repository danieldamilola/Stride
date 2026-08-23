
$csproj = Get-Content Stride.csproj -Raw
$csproj = $csproj -replace "<Version>1.2.0</Version>", "<Version>1.2.2</Version>"
Set-Content Stride.csproj $csproj

$iss = Get-Content installer.iss -Raw
$iss = $iss -replace "AppVersion=1.2.0", "AppVersion=1.2.2"
Set-Content installer.iss $iss

$rn = Get-Content ReleaseNotes.md -Raw
$rn = $rn -replace "# Release Note: Unreleased", "# Release Note: v1.2.2 UI Updates & Fixes"
Set-Content ReleaseNotes.md $rn


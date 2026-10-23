<#
.SYNOPSIS
    Clean generated build output across C:\dev

.DESCRIPTION
    Scans for and deletes common generated build folders:
    bin, obj, publish, TestResults, target, .next, .dart_tool, etc.

.PARAMETER DryRun
    Show what would be deleted without deleting

.PARAMETER IncludeNodeModules
    Also delete node_modules folders (default: false)

.PARAMETER IncludeFlutterSDK
    Also delete Flutter SDK folders (default: false)

.EXAMPLE
    .\cleanup.ps1 -DryRun

.EXAMPLE
    .\cleanup.ps1

.EXAMPLE
    .\cleanup.ps1 -IncludeNodeModules
#>

param(
    [switch]$DryRun,
    [switch]$IncludeNodeModules,
    [switch]$IncludeFlutterSDK
)

$ROOT = "C:\dev"
$GENERATED_DIRS = @(
    "bin", "obj", "publish", "TestResults", "testresults",
    "target", "__pycache__", ".pytest_cache", ".mypy_cache",
    ".next", ".turbo", "coverage", ".dart_tool"
)

if ($IncludeNodeModules) { $GENERATED_DIRS += "node_modules" }

$SKIP_PATTERNS = @()
if (-not $IncludeFlutterSDK) {
    $SKIP_PATTERNS += "\\.tools\\flutter", "\\.tools\\flutter-sdk"
}

$script:results = @()

function Scan($path, $depth) {
    if ($depth -gt 5) { return }
    Get-ChildItem $path -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object {
        $n = $_.Name
        if ($n -eq '.git' -or ($n -eq 'node_modules' -and -not $IncludeNodeModules)) { return }
        if ($GENERATED_DIRS -contains $n) {
            $skip = $false
            foreach ($pat in $SKIP_PATTERNS) {
                if ($_.FullName -match $pat) { $skip = $true; break }
            }
            if (-not $skip) {
                $size = (Get-ChildItem $_.FullName -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
                if ($size -gt 0) {
                    $script:results += [pscustomobject]@{
                        Path = $_.FullName
                        Name = $n
                        MB   = [math]::Round($size/1MB, 1)
                    }
                }
            }
        } else {
            Scan $_.FullName ($depth + 1)
        }
    }
}

Write-Host "Scanning $ROOT..." -ForegroundColor Cyan
Scan $ROOT 0

$totalMB = ($script:results | Measure-Object MB -Sum).Sum
$count = $script:results.Count

Write-Host "Found $count folders, $([math]::Round($totalMB,1)) MB" -ForegroundColor Yellow
$script:results | Sort-Object MB -Descending | Format-Table Path, MB -AutoSize

if ($DryRun) {
    Write-Host "DRY RUN - no files deleted" -ForegroundColor Green
    exit 0
}

Write-Host "Deleting..." -ForegroundColor Cyan
$freed = 0
foreach ($item in $script:results) {
    if (Test-Path -LiteralPath $item.Path) {
        try {
            Remove-Item -LiteralPath $item.Path -Recurse -Force -ErrorAction Stop
            Write-Host "  Deleted $($item.Path) ($($item.MB) MB)" -ForegroundColor DarkGray
            $freed += $item.MB
        } catch {
            Write-Host "  FAILED $($item.Path) : $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

Write-Host "Freed $([math]::Round($freed,1)) MB" -ForegroundColor Green
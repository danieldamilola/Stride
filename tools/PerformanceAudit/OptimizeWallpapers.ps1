param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore
$sourceRoot = (Resolve-Path -LiteralPath $SourceDirectory).Path
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { throw 'Use an absolute output directory.' }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$thumbnailRoot = Join-Path $OutputDirectory 'Thumbnails'
New-Item -ItemType Directory -Path $thumbnailRoot | Out-Null

$results = foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceRoot -Filter '*.jpg' -File) {
    $sourceStream = [IO.File]::OpenRead($sourceFile.FullName)
    try {
        $frame = [System.Windows.Media.Imaging.BitmapDecoder]::Create($sourceStream,
            [System.Windows.Media.Imaging.BitmapCreateOptions]::IgnoreColorProfile,
            [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad).Frames[0]
        $frame.Freeze()
    } finally { $sourceStream.Dispose() }

    foreach ($variant in @(
        @{ Width = 2560; Height = 1440; Quality = 82; Folder = $OutputDirectory },
        @{ Width = 320; Height = 180; Quality = 72; Folder = $thumbnailRoot }
    )) {
        [double]$scale = [double]$variant.Width / [double]$frame.PixelWidth
        [double]$heightScale = [double]$variant.Height / [double]$frame.PixelHeight
        if ($heightScale -lt $scale) { $scale = $heightScale }
        if ($scale -gt 1.0) { $scale = 1.0 }
        $transform = [System.Windows.Media.ScaleTransform]::new($scale, $scale)
        $bitmap = [System.Windows.Media.Imaging.TransformedBitmap]::new($frame, $transform)
        $bitmap.Freeze()
        if ($bitmap.PixelWidth -lt 200 -or $bitmap.PixelWidth -gt $variant.Width -or $bitmap.PixelHeight -gt $variant.Height) {
            throw 'Unexpected resized dimensions.'
        }
        $encoder = [System.Windows.Media.Imaging.JpegBitmapEncoder]::new()
        $encoder.QualityLevel = $variant.Quality
        $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $destination = Join-Path $variant.Folder $sourceFile.Name
        $outputStream = [IO.File]::Create($destination)
        try { $encoder.Save($outputStream) } finally { $outputStream.Dispose() }
        [pscustomobject]@{ File = $sourceFile.Name; Width = $bitmap.PixelWidth; Height = $bitmap.PixelHeight;
            Bytes = (Get-Item -LiteralPath $destination).Length; Thumbnail = $variant.Width -eq 320 }
    }
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'dimensions.json')
$results | Group-Object Thumbnail | ForEach-Object {
    [pscustomobject]@{ Thumbnail = $_.Name; Files = $_.Count; Bytes = ($_.Group | Measure-Object Bytes -Sum).Sum }
} | ConvertTo-Json

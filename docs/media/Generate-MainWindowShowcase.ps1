[CmdletBinding()]
param(
    [string]$ConfigPath,
    [string]$OutputPath,
    [ValidateRange(0, 100)]
    [int]$Quality = 80,
    [string]$MagickPath = "magick"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-Executable {
    param([string]$Command)

    if ([System.IO.Path]::IsPathRooted($Command)) {
        if (-not (Test-Path -LiteralPath $Command -PathType Leaf)) {
            throw "Executable not found: $Command"
        }
        return [System.IO.Path]::GetFullPath($Command)
    }

    $matches = @(Get-Command $Command -CommandType Application -All -ErrorAction SilentlyContinue)
    if ($matches.Count -eq 0) {
        throw "Executable '$Command' was not found."
    }
    return $matches[0].Source
}

function Resolve-InputPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot $Path))
}

function Assert-Number {
    param(
        $Value,
        [string]$Name,
        [double]$Min,
        [double]$Max
    )

    if ($null -eq $Value -or $Value -is [bool] -or $Value -is [string]) {
        throw "Invalid ${Name}: expected a number."
    }
    $number = [double]$Value
    if ([double]::IsNaN($number) -or [double]::IsInfinity($number) -or
        $number -lt $Min -or $number -gt $Max) {
        throw "Invalid ${Name}: expected a number between $Min and $Max."
    }
    return $number
}

function Get-CubicBezierValue {
    param(
        [double]$Progress,
        [double]$X1,
        [double]$Y1,
        [double]$X2,
        [double]$Y2
    )

    if ($Progress -le 0) { return 0.0 }
    if ($Progress -ge 1) { return 1.0 }

    $low = 0.0
    $high = 1.0
    $t = 0.5
    for ($i = 0; $i -lt 30; $i++) {
        $t = ($low + $high) / 2.0
        $u = 1.0 - $t
        $x = 3 * $u * $u * $t * $X1 + 3 * $u * $t * $t * $X2 + $t * $t * $t
        if ($x -lt $Progress) { $low = $t } else { $high = $t }
    }

    $u = 1.0 - $t
    return 3 * $u * $u * $t * $Y1 + 3 * $u * $t * $t * $Y2 + $t * $t * $t
}

function Get-UInt24LittleEndian {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    return [int]$Bytes[$Offset] -bor
        ([int]$Bytes[$Offset + 1] -shl 8) -bor
        ([int]$Bytes[$Offset + 2] -shl 16)
}

function Get-WebPAnimationInfo {
    param([string]$Path)

    [byte[]]$bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 12) {
        throw "Generated WebP is too short to contain a RIFF header."
    }

    $ascii = [System.Text.Encoding]::ASCII
    if ($ascii.GetString($bytes, 0, 4) -ne "RIFF" -or
        $ascii.GetString($bytes, 8, 4) -ne "WEBP") {
        throw "Generated output is not a RIFF WebP."
    }

    $riffSize = [BitConverter]::ToUInt32($bytes, 4)
    if ([long]$riffSize + 8 -ne $bytes.LongLength) {
        throw "Generated WebP has an invalid RIFF size."
    }

    $offset = 12
    $canvasWidth = 0
    $canvasHeight = 0
    $hasAnimation = $false
    $loopCount = $null
    $frames = @()

    while ($offset -lt $bytes.Length) {
        if ($offset + 8 -gt $bytes.Length) {
            throw "Generated WebP has a truncated chunk header."
        }

        $chunkType = $ascii.GetString($bytes, $offset, 4)
        $chunkSize = [BitConverter]::ToUInt32($bytes, $offset + 4)
        $dataOffset = $offset + 8
        $chunkEnd = $dataOffset + [long]$chunkSize
        if ($chunkEnd -gt $bytes.Length) {
            throw "Generated WebP has a truncated $chunkType chunk."
        }

        switch ($chunkType) {
            "VP8X" {
                if ($chunkSize -lt 10) {
                    throw "Generated WebP has an invalid VP8X chunk."
                }
                $canvasWidth = (Get-UInt24LittleEndian $bytes ($dataOffset + 4)) + 1
                $canvasHeight = (Get-UInt24LittleEndian $bytes ($dataOffset + 7)) + 1
            }
            "ANIM" {
                if ($chunkSize -lt 6) {
                    throw "Generated WebP has an invalid ANIM chunk."
                }
                $hasAnimation = $true
                $loopCount = [BitConverter]::ToUInt16($bytes, $dataOffset + 4)
            }
            "ANMF" {
                if ($chunkSize -lt 16) {
                    throw "Generated WebP has an invalid ANMF chunk."
                }
                $frameX = 2 * (Get-UInt24LittleEndian $bytes $dataOffset)
                $frameY = 2 * (Get-UInt24LittleEndian $bytes ($dataOffset + 3))
                $frameWidth = (Get-UInt24LittleEndian $bytes ($dataOffset + 6)) + 1
                $frameHeight = (Get-UInt24LittleEndian $bytes ($dataOffset + 9)) + 1
                $durationMs = Get-UInt24LittleEndian $bytes ($dataOffset + 12)
                if ($durationMs -lt 1) {
                    throw "Generated WebP contains a zero-duration animation frame."
                }
                $frames += [pscustomobject]@{
                    X = $frameX
                    Y = $frameY
                    Width = $frameWidth
                    Height = $frameHeight
                    DurationMs = $durationMs
                }
            }
        }

        $offset = [int]($chunkEnd + ($chunkSize % 2))
    }

    if ($offset -ne $bytes.Length) {
        throw "Generated WebP has invalid chunk padding."
    }
    if (-not $hasAnimation -or $frames.Count -lt 1) {
        throw "Generated WebP does not contain an animation."
    }
    if ($canvasWidth -lt 1 -or $canvasHeight -lt 1) {
        throw "Generated WebP does not declare a valid animation canvas."
    }
    foreach ($frame in $frames) {
        if ($frame.X -lt 0 -or $frame.Y -lt 0 -or
            $frame.X + $frame.Width -gt $canvasWidth -or
            $frame.Y + $frame.Height -gt $canvasHeight) {
            throw "Generated WebP contains an animation frame outside its canvas."
        }
    }

    return [pscustomobject]@{
        CanvasWidth = $canvasWidth
        CanvasHeight = $canvasHeight
        FrameCount = $frames.Count
        DurationMs = [int](($frames | Measure-Object -Property DurationMs -Sum).Sum)
        LoopCount = $loopCount
    }
}

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $PSScriptRoot "showcase-motion.json"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot "..\img\main-window.webp"
}

$configFullPath = Resolve-InputPath $ConfigPath
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
if ([System.IO.Path]::GetExtension($outputFullPath) -ine ".webp") {
    throw "OutputPath must use the .webp extension."
}
$config = Get-Content -LiteralPath $configFullPath -Raw | ConvertFrom-Json -AsHashtable
if ($config.schemaVersion -ne 1) {
    throw "Expected showcase-motion.json schemaVersion 1."
}
if ($config.images -isnot [array] -or $config.images.Count -lt 2) {
    throw "At least two source images are required."
}

$frameRateNumber = Assert-Number $config.fps "fps" 1 120
if ($frameRateNumber -ne [Math]::Floor($frameRateNumber)) {
    throw "fps must be an integer."
}
$frameRate = [int]$frameRateNumber
$secondsPerImage = Assert-Number $config.secondsPerImage "secondsPerImage" 0.2 30
$transitionSeconds = Assert-Number $config.transitionSeconds "transitionSeconds" 0.05 10
$slotFrames = [int][Math]::Round($frameRate * $secondsPerImage)
$transitionFrames = [int][Math]::Round($frameRate * $transitionSeconds)
$holdFrames = $slotFrames - $transitionFrames
if ($transitionFrames -lt 1 -or $holdFrames -lt 1) {
    throw "Timing requires at least one hold and one transition frame."
}

if ($config.wipe -isnot [hashtable] -or $config.motion -isnot [hashtable] -or
    $config.easing -isnot [hashtable]) {
    throw "Missing wipe, motion, or easing settings."
}
if ($config.wipe.enabled -isnot [bool]) {
    throw "wipe.enabled must be a Boolean."
}
$wipe = [bool]$config.wipe.enabled
$direction = [string]$config.wipe.direction
$alignment = [string]$config.wipe.alignment
if ($direction -notin @("up", "down", "left", "right", "centerX", "centerY")) {
    throw "Unrecognized wipe direction: $direction"
}
if ($alignment -notin @("window", "canvas")) {
    throw "Unrecognized wipe alignment: $alignment"
}
$easing = $config.easing
$x1 = Assert-Number $easing.x1 "easing.x1" 0 1
$y1 = Assert-Number $easing.y1 "easing.y1" -2 3
$x2 = Assert-Number $easing.x2 "easing.x2" 0 1
$y2 = Assert-Number $easing.y2 "easing.y2" -2 3

$incoming = $config.motion.incoming
$outgoing = $config.motion.outgoing
if ($incoming -isnot [hashtable] -or $outgoing -isnot [hashtable]) {
    throw "Missing incoming or outgoing motion."
}
foreach ($motion in @($incoming, $outgoing)) {
    foreach ($axis in @("xPercent", "yPercent")) {
        $motion[$axis] = Assert-Number $motion[$axis] "motion.$axis" -100 100
    }
    $motion.zoomPercent = Assert-Number $motion.zoomPercent "motion.zoomPercent" 25 250
    $motion.opacityPercent = Assert-Number $motion.opacityPercent "motion.opacityPercent" 0 100
}

if ($config.background -isnot [string] -or
    $config.background -cnotmatch '^#[0-9a-fA-F]{6}$') {
    throw "background must be a six-digit #RRGGBB color."
}
$background = [System.Drawing.ColorTranslator]::FromHtml($config.background)
$imagePaths = @(
    foreach ($source in $config.images) {
        if ($source -isnot [hashtable] -or
            $source.path -isnot [string] -or
            [string]::IsNullOrWhiteSpace($source.path)) {
            throw "Each image needs a nonempty path."
        }
        $path = Resolve-InputPath $source.path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Source image not found: $path"
        }
        $path
    }
)

Add-Type -AssemblyName System.Drawing
$magick = Resolve-Executable $MagickPath

# Keep the integer reference wipe exact for the accepted source motion.
$referenceMotion =
    $wipe -and $direction -eq "up" -and $alignment -eq "window" -and
    $incoming.xPercent -eq 0 -and $incoming.yPercent -eq 0 -and
    $incoming.zoomPercent -eq 100 -and $incoming.opacityPercent -eq 100 -and
    $outgoing.xPercent -eq 0 -and $outgoing.yPercent -eq 0 -and
    $outgoing.zoomPercent -eq 100 -and $outgoing.opacityPercent -eq 100

if (-not $referenceMotion) {
    Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class DpmShowcaseRaster
{
    public static void DrawPage(
        Graphics graphics, Image image, int width, int height,
        double progress, bool incoming, bool wipe, string direction,
        string alignment, float offsetX, float offsetY,
        float zoomPercent, float opacityPercent)
    {
        float t = (float)progress;
        float x = 0, y = 0, w = width, h = height;

        if (incoming && wipe)
        {
            float p = Math.Max(0, Math.Min(1, t));
            switch (direction)
            {
                case "up": y = height * (1 - p); h = height * p; break;
                case "down": h = height * p; break;
                case "right": w = width * p; break;
                case "left": x = width * (1 - p); w = width * p; break;
                case "centerX": x = width * (1 - p) / 2; w = width * p; break;
                case "centerY": y = height * (1 - p) / 2; h = height * p; break;
            }
        }

        if (w <= 0 || h <= 0) return;
        RectangleF cover = incoming && wipe && alignment == "window"
            ? new RectangleF(x, y, w, h)
            : new RectangleF(0, 0, width, height);
        float factor = Math.Max(cover.Width / image.Width, cover.Height / image.Height);
        float cropW = cover.Width / factor, cropH = cover.Height / factor;
        float sourceX = (image.Width - cropW) / 2, sourceY = (image.Height - cropH) / 2;

        float delta = incoming ? (1 - t) : t;
        float zoom = (incoming ? 1 + (zoomPercent / 100 - 1) * (1 - t) :
            1 + (zoomPercent / 100 - 1) * t);
        float alpha = incoming ? opacityPercent / 100 + (1 - opacityPercent / 100) * t :
            1 + (opacityPercent / 100 - 1) * t;
        float dx = width * offsetX * delta / 100, dy = height * offsetY * delta / 100;

        RectangleF destination = new RectangleF(
            width / 2f + dx + zoom * (cover.X - width / 2f),
            height / 2f + dy + zoom * (cover.Y - height / 2f),
            zoom * cover.Width, zoom * cover.Height);

        GraphicsState previous = graphics.Save();
        try
        {
            if (incoming && wipe)
                graphics.SetClip(new RectangleF(x, y, w, h));
            using (var attrs = new ImageAttributes())
            {
                var matrix = new ColorMatrix();
                matrix.Matrix33 = Math.Max(0, Math.Min(1, alpha));
                attrs.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                graphics.DrawImage(image, Rectangle.Round(destination),
                    sourceX, sourceY, cropW, cropH, GraphicsUnit.Pixel, attrs);
            }
        }
        finally
        {
            graphics.Restore(previous);
        }
    }
}
'@ -ReferencedAssemblies @([System.Drawing.Graphics].Assembly.Location, [System.Drawing.Color].Assembly.Location)
}

$images = @()
$tempDirectory = $null
try {
    foreach ($path in $imagePaths) {
        $images += [System.Drawing.Image]::FromFile($path)
    }
    $width = $images[0].Width
    $height = $images[0].Height
    foreach ($image in $images) {
        if ($image.Width -ne $width -or $image.Height -ne $height) {
            throw "All source images must have identical dimensions."
        }
    }

    $imageAnalysis = @(
        foreach ($path in $imagePaths) {
            $analysis = [string](& $magick identify -format "%[opaque]|%#" $path)
            if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($analysis)) {
                throw "ImageMagick could not analyze source image: $path"
            }
            $parts = $analysis.Trim().Split("|", 2)
            if ($parts.Count -ne 2 -or
                $parts[0] -notin @("True", "False") -or
                [string]::IsNullOrWhiteSpace($parts[1])) {
                throw "ImageMagick returned invalid source analysis for: $path"
            }
            [pscustomobject]@{
                HasTransparency = $parts[0] -eq "False"
                PixelHash = $parts[1]
            }
        }
    )
    $uniqueSourceStateCount = @($imageAnalysis.PixelHash | Sort-Object -Unique).Count

    $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("dpm-showcase-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempDirectory | Out-Null
    $frameNumber = 1
    for ($sourceIndex = 0; $sourceIndex -lt $images.Count; $sourceIndex++) {
        $nextIndex = ($sourceIndex + 1) % $images.Count

        for ($i = 0; $i -lt $slotFrames; $i++) {
            $framePath = Join-Path $tempDirectory ("frame-{0:D6}.png" -f $frameNumber)
            if ($i -lt $holdFrames) {
                if (-not $imageAnalysis[$sourceIndex].HasTransparency) {
                    Copy-Item -LiteralPath $imagePaths[$sourceIndex] -Destination $framePath
                } else {
                    $holdBitmap = [System.Drawing.Bitmap]::new($width, $height)
                    try {
                        $holdGraphics = [System.Drawing.Graphics]::FromImage($holdBitmap)
                        try {
                            $holdGraphics.Clear($background)
                            $holdGraphics.CompositingMode =
                                [System.Drawing.Drawing2D.CompositingMode]::SourceOver
                            $holdGraphics.DrawImageUnscaled($images[$sourceIndex], 0, 0)
                        }
                        finally {
                            $holdGraphics.Dispose()
                        }
                        $holdBitmap.Save($framePath, [System.Drawing.Imaging.ImageFormat]::Png)
                    }
                    finally {
                        $holdBitmap.Dispose()
                    }
                }
                $frameNumber++
                continue
            }

            $progress = ($i - $holdFrames + 1) / [double]$transitionFrames
            $eased = Get-CubicBezierValue $progress $x1 $y1 $x2 $y2
            $bitmap = [System.Drawing.Bitmap]::new($width, $height)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    if ($referenceMotion) {
                        $opaqueReferenceFrame =
                            -not $imageAnalysis[$sourceIndex].HasTransparency -and
                            -not $imageAnalysis[$nextIndex].HasTransparency
                        if ($opaqueReferenceFrame) {
                            $graphics.CompositingMode =
                                [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                        } else {
                            $graphics.Clear($background)
                            $graphics.CompositingMode =
                                [System.Drawing.Drawing2D.CompositingMode]::SourceOver
                        }
                        $graphics.DrawImageUnscaled($images[$sourceIndex], 0, 0)
                        $visible = [int][Math]::Round($height * [Math]::Min(1.0, [Math]::Max(0.0, $eased)))
                        if ($visible -gt 0) {
                            $destination = [System.Drawing.Rectangle]::new(0, $height - $visible, $width, $visible)
                            $source = [System.Drawing.Rectangle]::new(
                                0, [int][Math]::Round(($height - $visible) / 2.0), $width, $visible)
                            $graphics.DrawImage(
                                $images[$nextIndex], $destination, $source,
                                [System.Drawing.GraphicsUnit]::Pixel)
                        }
                    } else {
                        $graphics.Clear($background)
                        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
                        [DpmShowcaseRaster]::DrawPage(
                            $graphics, $images[$sourceIndex], $width, $height,
                            $eased, $false, $false, $direction, $alignment,
                            [float]$outgoing.xPercent, [float]$outgoing.yPercent,
                            [float]$outgoing.zoomPercent, [float]$outgoing.opacityPercent)
                        [DpmShowcaseRaster]::DrawPage(
                            $graphics, $images[$nextIndex], $width, $height,
                            $eased, $true, $wipe, $direction, $alignment,
                            [float]$incoming.xPercent, [float]$incoming.yPercent,
                            [float]$incoming.zoomPercent, [float]$incoming.opacityPercent)
                    }
                }
                finally {
                    $graphics.Dispose()
                }
                $bitmap.Save($framePath, [System.Drawing.Imaging.ImageFormat]::Png)
                $frameNumber++
            }
            finally {
                $bitmap.Dispose()
            }
        }
    }

    $expectedFrames = $slotFrames * $images.Count
    if (($frameNumber - 1) -ne $expectedFrames) {
        throw "Generated $($frameNumber - 1) frames; expected $expectedFrames."
    }

    $frames = @(Get-ChildItem -LiteralPath $tempDirectory -Filter "frame-*.png" | Sort-Object Name)
    if ($frames.Count -ne $expectedFrames) {
        throw "Found $($frames.Count) frames; expected $expectedFrames."
    }

    $outputDir = [System.IO.Path]::GetDirectoryName($outputFullPath)
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    $temporaryOutput = Join-Path $outputDir (".main-window-" + [guid]::NewGuid().ToString("N") + ".webp")
    try {
        $arguments = @()
        $timelineDurationMs = 0
        for ($i = 0; $i -lt $expectedFrames; $i++) {
            $startMs = [Math]::Floor($i * 1000.0 / $frameRate)
            $endMs = [Math]::Floor(($i + 1) * 1000.0 / $frameRate)
            $delayMs = [int]($endMs - $startMs)
            if ($delayMs -lt 1) {
                throw "fps $frameRate cannot be represented with millisecond frame delays."
            }
            $timelineDurationMs += $delayMs
            $arguments += @("-delay", "$($delayMs)x1000", $frames[$i].Name)
        }
        $arguments += @("-loop", "0", "-quality", $Quality, $temporaryOutput)
        Push-Location $tempDirectory
        try {
            & $magick @arguments
        }
        finally {
            Pop-Location
        }

        if ($LASTEXITCODE -ne 0) {
            throw "ImageMagick failed with exit code $LASTEXITCODE."
        }
        $generated = Get-Item -LiteralPath $temporaryOutput -ErrorAction Stop
        if ($generated.Length -le 0) {
            throw "ImageMagick produced an empty WebP."
        }
        $animationInfo = Get-WebPAnimationInfo $temporaryOutput
        if ($animationInfo.CanvasWidth -ne $width -or $animationInfo.CanvasHeight -ne $height) {
            throw "Encoded animation canvas is $($animationInfo.CanvasWidth)x$($animationInfo.CanvasHeight); expected $($width)x$($height)."
        }
        if ($animationInfo.DurationMs -ne $timelineDurationMs) {
            throw "Encoded animation duration is $($animationInfo.DurationMs) ms; expected $timelineDurationMs ms."
        }
        if ($animationInfo.LoopCount -ne 0) {
            throw "Encoded animation loop count is $($animationInfo.LoopCount); expected infinite looping."
        }

        $transitionChangesPixels =
            $wipe -or
            $incoming.xPercent -ne 0 -or $incoming.yPercent -ne 0 -or
            $incoming.zoomPercent -ne 100 -or $incoming.opacityPercent -ne 100 -or
            $outgoing.xPercent -ne 0 -or $outgoing.yPercent -ne 0 -or
            $outgoing.zoomPercent -ne 100 -or $outgoing.opacityPercent -ne 100
        if ($transitionChangesPixels) {
            $minimumExpected =
                [Math]::Max($uniqueSourceStateCount,
                    ([Math]::Max(1, $transitionFrames - 4)) * $images.Count)
        } else {
            $minimumExpected = [Math]::Max(1, $uniqueSourceStateCount)
        }
        if ($animationInfo.FrameCount -lt $minimumExpected) {
            throw "Encoded animation collapsed to $($animationInfo.FrameCount) frames; expected at least $minimumExpected."
        }

        Move-Item -LiteralPath $temporaryOutput -Destination $outputFullPath -Force
    }
    finally {
        Remove-Item -LiteralPath $temporaryOutput -Force -ErrorAction SilentlyContinue
    }
}
finally {
    foreach ($image in $images) {
        $image.Dispose()
    }
    if ($null -ne $tempDirectory) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$outputFile = Get-Item -LiteralPath $outputFullPath
Write-Host "Generated showcase: $($outputFile.FullName)"
Write-Host "Config: $configFullPath"
Write-Host "Frames: $expectedFrames at $frameRate fps ($($images.Count) images, $transitionFrames transition frames each)"
Write-Host "Encoded: $($animationInfo.FrameCount) frames, $($animationInfo.DurationMs) ms"
Write-Host "Size: $($outputFile.Length) bytes"

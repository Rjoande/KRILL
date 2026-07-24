# Generates KRILL's toolbar icons (38x38 stock, 24x24 Blizzy) into ..\Textures.
# Ported from AGSetHUD's dev\make-icons.ps1 (same technique, System.Drawing).
Add-Type -AssemblyName System.Drawing

function New-KrillIcon([int]$size, [string]$outPath) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb(0, 0, 0, 0))

    # background: dark rounded square (KRAB/KRILL shared skin: #262a30 window color)
    $r = [Math]::Max(3, [int]($size * 0.18))
    $rect = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = $size - 1
    $rect.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
    $rect.AddArc($w - 2 * $r, 0, 2 * $r, 2 * $r, 270, 90)
    $rect.AddArc($w - 2 * $r, $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $rect.AddArc(0, $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $rect.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 38, 42, 48))
    $g.FillPath($bg, $rect)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 190, 200, 215), [Math]::Max(1, $size / 24))
    $g.DrawPath($pen, $rect)

    # "K" centered, acid-green accent (KrillUi.GreenHi #b9e34f)
    $fontSize = [single]($size * 0.52)
    $font = New-Object System.Drawing.Font("Arial", $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 185, 227, 79))
    $textRect = New-Object System.Drawing.RectangleF(0, (-$size * 0.03), $size, $size)
    $g.DrawString("K", $font, $textBrush, $textRect, $fmt)

    $g.Dispose()
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Created: $outPath"
}

# 2026-07-26: install/publish payload moved one level deeper (GameData/KRILL/GameData/KRILL/, mirrors the GitHub repo layout).
$textures = Join-Path $PSScriptRoot "..\GameData\KRILL\Textures"
New-KrillIcon 38 (Join-Path $textures "KRILL_38.png")
New-KrillIcon 24 (Join-Path $textures "KRILL_24.png")

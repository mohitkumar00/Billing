Add-Type -AssemblyName System.Drawing

$sizes = @{
    'mipmap-mdpi' = 48
    'mipmap-hdpi' = 72
    'mipmap-xhdpi' = 96
    'mipmap-xxhdpi' = 144
    'mipmap-xxxhdpi' = 192
}

$baseDir = "src/BillMate.App/Platforms/Android/Resources"

foreach ($entry in $sizes.GetEnumerator()) {
    $folder = $entry.Key
    $dim = $entry.Value
    $dir = Join-Path $baseDir $folder
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    
    $bmp = New-Object System.Drawing.Bitmap $dim, $dim
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 79, 70, 229))
    $g.FillEllipse($brush, 2, 2, ($dim - 4), ($dim - 4))
    
    $fontFamily = [System.Drawing.FontFamily]::GenericSansSerif
    $fontSize = [float]($dim * 0.35)
    $font = New-Object System.Drawing.Font ($fontFamily, $fontSize, [System.Drawing.FontStyle]::Bold)
    $textBrush = [System.Drawing.Brushes]::White
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF (0, 0, [float]$dim, [float]$dim)
    $g.DrawString("BM", $font, $textBrush, $rect, $sf)
    
    $g.Dispose()
    
    $bmp.Save((Join-Path $dir "appicon.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Save((Join-Path $dir "appicon_round.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Generated $folder app icons ($dim x $dim)"
}

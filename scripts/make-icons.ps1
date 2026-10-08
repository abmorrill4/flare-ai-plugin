# Renders the ribbon/toolbar icons from the Windows "Segoe Fluent Icons" font into src\FlareAI.Flare\Icons.
# Each icon is drawn at 2x and tagged 192 DPI, so WPF shows it crisp at 32 px (large) / 16 px (small) on HiDPI screens.
# Run with Windows PowerShell 5.1:  powershell.exe -File .\scripts\make-icons.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "..\src\FlareAI.Flare\Icons"
New-Item -ItemType Directory -Force $out | Out-Null

# name = glyph code point (Segoe Fluent Icons / MDL2 share these)
$icons = [ordered]@{
    check    = 0xE9D5  # CheckList
    rewrite  = 0xE70F  # Edit
    ask      = 0xE8F2  # ChatBubbles
    project  = 0xE8B7  # Folder
    settings = 0xE713  # Settings
    validate = 0xE82D  # Dictionary
    diag     = 0xE9D9  # Diagnostic
}
$color = [System.Drawing.Color]::FromArgb(255, 14, 124, 134)  # FlareAI teal; reads on Flare's light ribbon

function Render($glyph, $px, $file) {
    $bmp = New-Object System.Drawing.Bitmap $px, $px
    $bmp.SetResolution(192, 192)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)
    $font = New-Object System.Drawing.Font "Segoe Fluent Icons", ($px * 0.8), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $brush = New-Object System.Drawing.SolidBrush $color
    $g.DrawString([string][char]$glyph, $font, $brush, (New-Object System.Drawing.RectangleF 0, 0, $px, $px), $fmt)
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); $font.Dispose(); $brush.Dispose()
}

foreach ($name in $icons.Keys) {
    Render $icons[$name] 64 (Join-Path $out "$name.png")        # large: 32 px logical
    Render $icons[$name] 32 (Join-Path $out "$name-16.png")     # small: 16 px logical
}
Get-ChildItem $out | Select-Object Name, Length

# Regenerates styleguide/Project/TOCs/Main.fltoc from the topics in Company/ and Google/.
# Run after the Google guide compiler adds or removes pages so Build > Publish includes them.
param([string]$GuideProject = (Join-Path $PSScriptRoot '..\styleguide'))
$root = Join-Path $GuideProject 'Content\Resources\StyleGuide'
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine('<CatapultToc Version="1">')
foreach ($f in 'Company', 'Google') {
    [void]$sb.AppendLine("    <TocEntry Title=`"$f`">")
    $files = Get-ChildItem (Join-Path $root $f) -Filter '*.htm' | Sort-Object { if ($_.Name -eq 'About.htm') { '' } else { $_.Name } }
    foreach ($file in $files) {
        [void]$sb.AppendLine("        <TocEntry Title=`"[%=System.LinkedTitle%]`" Link=`"/Content/Resources/StyleGuide/$f/$($file.Name)`" />")
    }
    [void]$sb.AppendLine('    </TocEntry>')
}
[void]$sb.AppendLine('</CatapultToc>')
$out = Join-Path $GuideProject 'Project\TOCs\Main.fltoc'
[void][IO.Directory]::CreateDirectory((Split-Path $out))
[IO.File]::WriteAllText($out, $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
Write-Output "Wrote $out"

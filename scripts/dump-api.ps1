# Reflects over B3.PluginAPIKit.dll and writes every public type + member to docs\api-dump.txt.
# This is our ground truth for the plugin API; the online docs are incomplete.
param([string]$DllPath)

$repo = Split-Path $PSScriptRoot -Parent
if (-not $DllPath) { $DllPath = Join-Path $repo "lib\B3.PluginAPIKit.dll" }
if (-not (Test-Path $DllPath)) { throw "DLL not found: $DllPath (run bootstrap.ps1 first)" }

$out = Join-Path $repo "docs\api-dump.txt"
$sb  = New-Object System.Text.StringBuilder

# ReflectionOnly load; dependencies resolve from the GAC, then from the DLL's folder (Flare.app or lib)
$probeDirs = @((Split-Path $DllPath -Parent))
. "$PSScriptRoot\Find-Flare.ps1"
try { $probeDirs += (Get-FlareInstall).AppDir } catch { }
[System.AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve({
    param($s, $e)
    try { return [System.Reflection.Assembly]::ReflectionOnlyLoad($e.Name) } catch { }
    $file = (New-Object System.Reflection.AssemblyName $e.Name).Name + ".dll"
    foreach ($d in $probeDirs) {
        $p = Join-Path $d $file
        if (Test-Path $p) { return [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($p) }
    }
    return $null
})
$asm = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($DllPath)
$null = $sb.AppendLine("Assembly : " + $asm.FullName)
$null = $sb.AppendLine("Runtime  : " + $asm.ImageRuntimeVersion)
$tf = $asm.GetCustomAttributesData() | Where-Object { $_.AttributeType.Name -eq 'TargetFrameworkAttribute' }
if ($tf) { $null = $sb.AppendLine("Target   : " + $tf.ConstructorArguments[0].Value) }
$null = $sb.AppendLine("Refs     : " + (($asm.GetReferencedAssemblies() | ForEach-Object { $_.Name }) -join ", "))
$null = $sb.AppendLine("")

$flags = [System.Reflection.BindingFlags]'Public,Instance,Static,DeclaredOnly'
try { $types = $asm.GetTypes() } catch [System.Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types | Where-Object { $_ } }

foreach ($t in ($types | Where-Object { $_.IsPublic -or $_.IsNestedPublic } | Sort-Object FullName)) {
    $kind = if ($t.IsInterface) { "interface" } elseif ($t.IsEnum) { "enum" } elseif ($t.IsValueType) { "struct" } else { "class" }
    $base = if ($t.BaseType -and $t.BaseType.FullName -ne 'System.Object') { " : " + $t.BaseType.FullName } else { "" }
    $ifaces = $t.GetInterfaces() | ForEach-Object { $_.Name }
    if ($ifaces) { $base += $(if ($base) { ", " } else { " : " }) + ($ifaces -join ", ") }
    $null = $sb.AppendLine("$kind $($t.FullName)$base")

    if ($t.IsEnum) {
        foreach ($n in [System.Enum]::GetNames($t)) { $null = $sb.AppendLine("    $n") }
        $null = $sb.AppendLine(""); continue
    }
    foreach ($p in $t.GetProperties($flags))  { $null = $sb.AppendLine("    prop   $($p.PropertyType.Name) $($p.Name) { $(if($p.CanRead){'get;'}) $(if($p.CanWrite){'set;'}) }") }
    foreach ($e in $t.GetEvents($flags))      { $null = $sb.AppendLine("    event  $($e.EventHandlerType.Name) $($e.Name)") }
    foreach ($m in $t.GetMethods($flags) | Where-Object { -not $_.IsSpecialName }) {
        $ps = ($m.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
        $null = $sb.AppendLine("    method $($m.ReturnType.Name) $($m.Name)($ps)")
    }
    foreach ($c in $t.GetConstructors($flags)) {
        $ps = ($c.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
        $null = $sb.AppendLine("    ctor   ($ps)")
    }
    $null = $sb.AppendLine("")
}

[System.IO.File]::WriteAllText($out, $sb.ToString())
Write-Host "Wrote $out ($($types.Count) types)"

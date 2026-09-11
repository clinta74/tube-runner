<#
.SYNOPSIS
Exports a standalone Windows build to builds/windows and zips it for distribution.
Needs Godot's export templates (Editor > Manage Export Templates).

.EXAMPLE
./export.ps1
./export.ps1 -DebugBuild   # opens a console with errors; for testing exports
#>
param([switch]$DebugBuild)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools/godot.ps1')

$godot = Get-GodotPath -Console
# e.g. "4.7.2.stable.mono.official.ed1daf0bf" -> templates in export_templates/4.7.2.stable.mono
$version = (& $godot --version | Select-Object -Last 1).Trim()
$templates = Join-Path $env:APPDATA ('Godot/export_templates/' + (($version -split '\.')[0..4] -join '.'))
if (-not (Test-Path $templates)) {
    throw "Export templates for Godot $version aren't installed (expected $templates). " +
        "In the editor: Editor > Manage Export Templates > Download and Install."
}

$out = Join-Path $PSScriptRoot 'builds/windows'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

$exe = Join-Path $out 'TubeRunner.exe'
$mode = if ($DebugBuild) { '--export-debug' } else { '--export-release' }
& $godot --headless --path (Join-Path $PSScriptRoot 'game') $mode 'Windows Desktop' $exe
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { throw "Export failed (exit code $LASTEXITCODE)." }

$zip = Join-Path $PSScriptRoot 'builds/TubeRunner-windows.zip'
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -Force
"Exported $exe"
"Zipped   $zip"

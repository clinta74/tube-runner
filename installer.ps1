<#
.SYNOPSIS
Builds the Windows installer, an MSI with its cabinet embedded, from the export in builds/windows.
Run ./export.ps1 first. WiX comes from NuGet through dotnet build; nothing else needs installing.

.EXAMPLE
./installer.ps1                  # version from the latest git tag
./installer.ps1 -Version 0.4.0
#>
param([string]$Version)
$ErrorActionPreference = 'Stop'

$build = Join-Path $PSScriptRoot 'builds/windows'
if (-not (Test-Path (Join-Path $build 'TubeRunner.exe'))) {
    throw 'There is no export to package. Run ./export.ps1 first.'
}

if (-not $Version) {
    # The latest tag, e.g. v0.4.0. CI passes the tag it is releasing instead.
    $tag = git -C $PSScriptRoot describe --tags --abbrev=0 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $tag) { throw 'No git tag to take a version from; pass -Version.' }
    $Version = $tag.Trim()
}
$Version = $Version.TrimStart('v')
# MSI versions are numeric, so a tag like v0.4.0-beta can't be stamped as one.
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' isn't in x.y.z form." }

dotnet build (Join-Path $PSScriptRoot 'installer/TubeRunner.wixproj') -c Release -nologo -v q `
    "-p:GameVersion=$Version" "-p:GameBuild=$((Resolve-Path $build).Path)"
if ($LASTEXITCODE -ne 0) { throw "The installer build failed (exit code $LASTEXITCODE)." }

$msi = Join-Path $PSScriptRoot "builds/TubeRunner-$Version.msi"
if (-not (Test-Path $msi)) { throw "The build finished but $msi isn't there." }
"Installer $msi"

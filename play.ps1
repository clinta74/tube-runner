<#
.SYNOPSIS
Builds the C# code and runs the game (or the editor) with Godot .NET.

.EXAMPLE
./play.ps1
./play.ps1 -Level level_02.json -Start 3000
./play.ps1 -Editor
./play.ps1 -Level level_22.json -Start 900 -Record
#>
param(
    # Level file in game/levels to play.
    [string]$Level,
    # Distance along the track to start from, for testing a section.
    [double]$Start = -1,
    # Open the Godot editor instead of running the game.
    [switch]$Editor,
    # Record to recordings/ as an AVI, one frame per output frame.
    #
    # Godot renders every frame regardless of how fast the machine manages it, so the video comes out
    # perfectly smooth with nothing dropped - but the engine is no longer tied to real time, so play
    # will feel sped up or slowed down while it records. Good for a clean flythrough, awkward for
    # capturing an actual run: use a screen recorder for that.
    #
    # The file is uncompressed and runs to gigabytes a minute. Convert it before sharing, e.g.
    #   ffmpeg -i recordings/run-....avi -c:v libx264 -crf 20 -pix_fmt yuv420p run.mp4
    [switch]$Record,
    # Frame rate to record at.
    [int]$RecordFps = 60,
    # Open straight onto the end-of-run results screen, filled with the levels this run would have
    # covered. For checking the summary without playing the game to the end: the list only scrolls
    # past a dozen levels, and splits are only kept for levels actually finished.
    [switch]$Summary
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools/godot.ps1')

$godot = Get-GodotPath
$game = Join-Path $PSScriptRoot 'game'
dotnet build (Join-Path $game 'TubeRunner.sln') -v q -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$godotArgs = @('--path', $game)
if ($Editor) { $godotArgs += '--editor' }
if ($Record) {
    $dir = Join-Path $PSScriptRoot 'recordings'
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory $dir | Out-Null }
    $name = 'run'
    if ($Level) { $name = [IO.Path]::GetFileNameWithoutExtension($Level) }
    $file = Join-Path $dir ("$name-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.avi')
    # Engine flags, so they go before the '--' that separates the game's own arguments.
    $godotArgs += @('--write-movie', $file, '--fixed-fps', $RecordFps)
    Write-Host "Recording to $file"
    Write-Host "Play will not run at real time while recording; the video itself will be correct."
}
$gameArgs = @()
if ($Level) { $gameArgs += "--level=res://levels/$Level" }
if ($Start -ge 0) { $gameArgs += '--start=' + $Start.ToString([cultureinfo]::InvariantCulture) }
if ($Summary) { $gameArgs += '--summary' }
if ($gameArgs) { $godotArgs += @('--') + $gameArgs }

& $godot @godotArgs

<#
.SYNOPSIS
Builds the C# code and runs the game (or the editor) with Godot .NET.

.EXAMPLE
./play.ps1
./play.ps1 -Level level_02.json -Start 3000
./play.ps1 -Editor
#>
param(
    # Level file in game/levels to play.
    [string]$Level,
    # Distance along the track to start from, for testing a section.
    [double]$Start = -1,
    # Open the Godot editor instead of running the game.
    [switch]$Editor
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools/godot.ps1')

$godot = Get-GodotPath
$game = Join-Path $PSScriptRoot 'game'
dotnet build (Join-Path $game 'TubeRunner.sln') -v q -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$godotArgs = @('--path', $game)
if ($Editor) { $godotArgs += '--editor' }
$gameArgs = @()
if ($Level) { $gameArgs += "--level=res://levels/$Level" }
if ($Start -ge 0) { $gameArgs += '--start=' + $Start.ToString([cultureinfo]::InvariantCulture) }
if ($gameArgs) { $godotArgs += @('--') + $gameArgs }

& $godot @godotArgs

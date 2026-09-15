<#
.SYNOPSIS
Builds game/icon.ico from game/icon.svg. Rerun after editing the SVG, and commit both.

.DESCRIPTION
Godot renders the SVG at each icon size (game/tools/make_icon.gd), then the PNGs are packed into an
.ico here. An .ico is a 6-byte header, a 16-byte entry per image, then the images themselves; since
Windows Vista the images can simply be PNGs, so no image library is needed.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'godot.ps1')

$root = Split-Path $PSScriptRoot -Parent
$game = Join-Path $root 'game'
$svg = Join-Path $game 'icon.svg'
# Scratch PNGs go under builds/, which git ignores.
$work = Join-Path $root 'builds/icon'
New-Item -ItemType Directory -Force $work | Out-Null

$godot = Get-GodotPath -Console
& $godot --headless --path $game --script res://tools/make_icon.gd -- $svg $work
if ($LASTEXITCODE -ne 0) { throw "Rendering the icon failed (exit code $LASTEXITCODE)." }

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) {
    $png = Join-Path $work "icon_$size.png"
    if (-not (Test-Path $png)) { throw "Godot did not write $png." }
    # The leading comma keeps each byte array whole instead of letting PowerShell unroll it.
    , [IO.File]::ReadAllBytes($png)
}

$stream = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0)              # reserved
$writer.Write([uint16]1)              # type: icon
$writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $writer.Write([byte]($size % 256))  # width; 256 is stored as 0
    $writer.Write([byte]($size % 256))  # height
    $writer.Write([byte]0)              # palette size
    $writer.Write([byte]0)              # reserved
    $writer.Write([uint16]1)            # colour planes
    $writer.Write([uint16]32)           # bits per pixel
    $writer.Write([uint32]$images[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write([byte[]]$image) }
$writer.Flush()

$ico = Join-Path $game 'icon.ico'
[IO.File]::WriteAllBytes($ico, $stream.ToArray())
"Wrote $ico ($($sizes.Count) sizes, $($stream.Length) bytes)"

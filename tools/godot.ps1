# Shared helpers for play.ps1 and export.ps1.

# Path to the real Godot .NET executable. Godot .NET looks for its GodotSharp folder next to the
# executable it was launched as, so starting it through a symlink (like winget's `godot` alias)
# fails with ".NET assemblies not found". Set $env:GODOT to use a specific executable.
# -Console returns the console build, which prints to the terminal and waits for Godot to exit.
function Get-GodotPath([switch]$Console) {
    $godot = if ($env:GODOT) { $env:GODOT } else { (Get-Command godot -ErrorAction Stop).Source }
    $item = Get-Item $godot
    if ($item.LinkType -eq 'SymbolicLink') { $godot = @($item.Target)[0] }

    if ($Console) {
        $consoleExe = $godot -replace '\.exe$', '_console.exe'
        if (Test-Path $consoleExe) { return $consoleExe }
    }
    return $godot
}

using System.Text;

namespace TubeRunner.Build;

internal static class Godot
{
    /// <summary>
    /// The real Godot .NET executable. Godot .NET looks for its GodotSharp folder next to the
    /// executable it was launched as, so starting it through a symlink (like winget's <c>godot</c>
    /// alias) fails with ".NET assemblies not found". Set GODOT to use a specific executable.
    /// </summary>
    /// <param name="console">
    /// The console build, which prints to the terminal and runs until Godot exits. Without it, the
    /// ordinary build, which opens its own window.
    /// </param>
    public static string Find(bool console)
    {
        var path = Environment.GetEnvironmentVariable("GODOT") is { Length: > 0 } set
            ? set
            : OnPath("godot") ?? throw new BuildFailure(
                "Godot .NET wasn't found. Install it (winget install GodotEngine.GodotEngine.Mono) or set GODOT to its executable.");

        var file = new FileInfo(path);
        if (!file.Exists) throw new BuildFailure($"Godot isn't at {path}.");
        if (file.LinkTarget is not null && file.ResolveLinkTarget(returnFinalTarget: true) is { } target) path = target.FullName;

        if (console && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var consoleExe = path[..^".exe".Length] + "_console.exe";
            if (File.Exists(consoleExe)) return consoleExe;
        }
        return path;
    }

    /// <summary>The engine's version string, e.g. "4.7.2.stable.mono.official.ed1daf0bf".</summary>
    public static string Version(string godot) =>
        Proc.Capture(godot, ["--version"])?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault()
        ?? throw new BuildFailure($"Couldn't read the version of {godot}.");

    /// <summary>
    /// Stops the build unless export templates for <paramref name="engineVersion"/> are installed, and
    /// says as much as it can about why not.
    /// </summary>
    public static void RequireTemplates(string engineVersion)
    {
        // "4.7.2.stable.mono.official.ed1daf0bf" -> export_templates/4.7.2.stable.mono
        var name = string.Join('.', engineVersion.Split('.').Take(5));
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "export_templates");
        var templates = Path.Combine(root, name);
        if (Directory.Exists(templates) && Directory.EnumerateFileSystemEntries(templates).Any()) return;

        var message = new StringBuilder($"Export templates for Godot {engineVersion} aren't installed (expected {templates}).");
        message.Append(' ').Append(WhatIsThere(root));

        // A copy stranded in a packaged app's private storage. Windows redirects a packaged app's writes
        // into %APPDATA% to %LOCALAPPDATA%\Packages\<app>\LocalCache\Roaming, visible only to programs
        // started from inside that app. Templates downloaded from a terminal inside one - the Claude
        // desktop app, for instance - end up there, and every other shell sees an empty folder.
        foreach (var stranded in StrandedCopies(name))
        {
            message.Append($"\n\nA copy is in a packaged app's private storage, where only that app can see it:\n  {stranded}")
                .Append($"\nCopy it where Godot looks, from an ordinary terminal:\n  robocopy \"{Path.GetDirectoryName(stranded)}\" \"{root}\" /E");
        }

        message.Append("\n\nOr download them in the editor: Editor > Manage Export Templates > Download and Install.");
        throw new BuildFailure(message.ToString());
    }

    private static string WhatIsThere(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return $"{root} doesn't exist.";
            var names = Directory.GetDirectories(root).Select(Path.GetFileName).ToArray();
            return names.Length > 0 ? $"Installed: {string.Join(", ", names)}." : $"{root} is empty.";
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return $"{root} can't be read from here: {e.Message}";
        }
    }

    private static IEnumerable<string> StrandedCopies(string name)
    {
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        string[] apps;
        try
        {
            apps = Directory.Exists(packages) ? Directory.GetDirectories(packages) : [];
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            yield break;
        }

        foreach (var app in apps)
        {
            var copy = Path.Combine(app, "LocalCache", "Roaming", "Godot", "export_templates", name);
            if (Directory.Exists(copy)) yield return copy;
        }
    }

    private static string? OnPath(string program)
    {
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(dir.Trim('"'), program + extension.ToLowerInvariant());
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}

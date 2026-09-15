using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TubeRunner.Build;

/// <summary>A build step that cannot go on. Its message is shown as it is, so it should say what to do.</summary>
internal sealed class BuildFailure(string message) : Exception(message);

/// <summary>Paths inside the repository, wherever the tool was run from.</summary>
internal static class Repo
{
    public static string Root { get; } = FindRoot();

    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    // The tool runs from its own bin folder, so walk up to the folder that holds the Godot project.
    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "game", "project.godot"))) return dir.FullName;
        }
        throw new BuildFailure("Couldn't find the repository (a folder with game/project.godot) above the build tool.");
    }
}

/// <summary>Running other programs: Godot, dotnet, git.</summary>
internal static class Proc
{
    /// <summary>
    /// Runs a program with its output going straight to the terminal, and stops the build if it fails.
    /// </summary>
    /// <param name="environment">Variables to set for that program only; a null value removes one.</param>
    /// <param name="wait">False to start it and return at once, for the game and the editor.</param>
    public static void Run(string file, IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?>? environment = null, bool wait = true)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                if (value is null) start.Environment.Remove(name);
                else start.Environment[name] = value;
            }
        }

        using var process = Start(start);
        if (!wait) return;
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new BuildFailure($"{System.IO.Path.GetFileName(file)} failed (exit code {process.ExitCode}).");
        }
    }

    /// <summary>Runs a program and returns what it printed, or null if it failed.</summary>
    public static string? Capture(string file, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Start(start);
        // Read both, or a program that fills the error pipe waits forever for someone to drain it.
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        _ = error.Result;
        return process.ExitCode == 0 ? output : null;
    }

    private static Process Start(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start) ?? throw new BuildFailure($"Couldn't start {start.FileName}.");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new BuildFailure($"Couldn't start {start.FileName}: {e.Message}");
        }
    }
}

/// <summary>Release version numbers, as passed on the command line or read from a git tag.</summary>
internal static partial class Versions
{
    /// <summary>
    /// "0.4.1" or "v0.4.1" as x.y.z. Anything else stops the build: Windows file versions and MSI
    /// versions are numeric, so a tag like v0.5.0-beta cannot be stamped as one.
    /// </summary>
    public static string Require(string text)
    {
        var version = text.Trim();
        if (version.StartsWith('v') || version.StartsWith('V')) version = version[1..];
        if (!ReleaseVersion().IsMatch(version)) throw new BuildFailure($"Version '{text}' isn't in x.y.z form.");
        return version;
    }

    /// <summary>The newest git tag, e.g. v0.4.1.</summary>
    public static string LatestTag() =>
        Proc.Capture("git", ["-C", Repo.Root, "describe", "--tags", "--abbrev=0"])?.Trim() is { Length: > 0 } tag
            ? tag
            : throw new BuildFailure("There's no git tag to take a version from; pass --version.");

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex ReleaseVersion();
}

/// <summary>
/// A command's options: <c>--name value</c>, <c>--name=value</c>, or a bare <c>--flag</c>. Anything it
/// doesn't recognise stops the build rather than being quietly ignored.
/// </summary>
internal sealed class Options
{
    private readonly Dictionary<string, string?> _given = new();

    public static Options Parse(string[] arguments, string[] flags, string[] values)
    {
        var options = new Options();
        for (int i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];
            if (!argument.StartsWith("--"))
            {
                throw new BuildFailure($"Unexpected '{argument}'. Options start with --; see `tube help`.");
            }

            var name = argument[2..];
            string? value = null;
            int equals = name.IndexOf('=');
            if (equals >= 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }

            if (flags.Contains(name))
            {
                if (value is not null) throw new BuildFailure($"--{name} doesn't take a value.");
                options._given[name] = null;
            }
            else if (values.Contains(name))
            {
                if (value is null)
                {
                    if (i + 1 >= arguments.Length) throw new BuildFailure($"--{name} needs a value.");
                    value = arguments[++i];
                }
                options._given[name] = value;
            }
            else
            {
                throw new BuildFailure($"Unknown option --{name}. See `tube help`.");
            }
        }
        return options;
    }

    public bool Flag(string name) => _given.ContainsKey(name);

    /// <summary>The option's value, or null if it wasn't given or was given empty.</summary>
    public string? Value(string name) => _given.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    public double? Number(string name) => Value(name) switch
    {
        null => null,
        var text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
        var text => throw new BuildFailure($"--{name} needs a number, not '{text}'."),
    };
}

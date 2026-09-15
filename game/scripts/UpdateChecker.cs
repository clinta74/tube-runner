using System;
using System.Reflection;
using System.Text;
using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// Asks GitHub, once and in the background, whether a newer release is out. It never holds anything
/// up and never complains: offline, rate-limited, or any answer it cannot read, and the game simply
/// carries on as if there were nothing new.
/// </summary>
public partial class UpdateChecker : Node
{
    // Long enough for a slow connection, short enough that a dead one is given up on quietly.
    private const double TimeoutSeconds = 8.0;

    /// <summary>
    /// This build's version, stamped in by <c>tube export --version</c>. A development build has none,
    /// reads as 0.0.0, and never checks.
    /// </summary>
    public static GameVersion CurrentVersion { get; } = ReadVersion();

    /// <summary>Raised on the main thread if a release newer than this build is out.</summary>
    public event Action<ReleaseInfo>? UpdateFound;

    public void Start()
    {
        if (CurrentVersion.IsDevelopment) return;

        var http = new HttpRequest { Timeout = TimeoutSeconds };
        AddChild(http);
        http.RequestCompleted += (result, code, _, body) =>
        {
            http.QueueFree();
            if (result != (long)HttpRequest.Result.Success || code != 200) return;

            var latest = Updates.Newer(CurrentVersion, Updates.ParseLatest(Encoding.UTF8.GetString(body)));
            if (latest is not null) UpdateFound?.Invoke(latest);
        };

        // GitHub refuses API requests without a User-Agent.
        var error = http.Request(Updates.LatestReleaseApi,
            new[] { $"User-Agent: TubeRunner/{CurrentVersion}", "Accept: application/vnd.github+json" });
        if (error != Error.Ok) http.QueueFree();
    }

    private static GameVersion ReadVersion()
    {
        var stamped = typeof(UpdateChecker).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return GameVersion.TryParse(stamped, out var version) ? version : default;
    }
}

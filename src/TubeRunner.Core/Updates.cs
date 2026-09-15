using System.Globalization;
using System.Text.Json;

namespace TubeRunner.Core;

/// <summary>
/// A release version, x.y.z. The default, 0.0.0, marks a development build: one that was not stamped
/// with a version when it was exported, and so never checks for updates.
/// </summary>
public readonly record struct GameVersion(int Major, int Minor, int Patch) : IComparable<GameVersion>
{
    public bool IsDevelopment => this == default;

    /// <summary>
    /// Reads "0.4.1", "v0.4.1" (a tag), or "0.4.1+abc123" (.NET adds the commit to an assembly's
    /// version). A pre-release such as "0.5.0-beta" is refused: only finished releases are offered.
    /// </summary>
    public static bool TryParse(string? text, out GameVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];

        var parts = s.Split('.');
        if (parts.Length != 3) return false;
        var numbers = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        }
        version = new GameVersion(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    public int CompareTo(GameVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    public static bool operator >(GameVersion a, GameVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(GameVersion a, GameVersion b) => a.CompareTo(b) < 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

/// <summary>The newest published release, as GitHub reports it.</summary>
/// <param name="Url">Its page on GitHub, where the installer and zip are.</param>
public sealed record ReleaseInfo(GameVersion Version, string Tag, string Url);

/// <summary>
/// Reading GitHub's answer to "what is the latest release", and deciding whether it is worth
/// telling the player about. The request itself is the game's job; nothing here touches the network.
/// </summary>
public static class Updates
{
    public const string Repository = "clinta74/tube-runner";

    /// <summary>GitHub's latest-release endpoint. It skips drafts and pre-releases on its own.</summary>
    public static string LatestReleaseApi => $"https://api.github.com/repos/{Repository}/releases/latest";

    /// <summary>
    /// Reads a latest-release response, or returns null for anything that is not a finished release
    /// of this game. The link is checked as well as the version: it is what the game will open in a
    /// browser, so it must be this repository's own release page and nothing else.
    /// </summary>
    public static ReleaseInfo? ParseLatest(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (IsTrue(root, "draft") || IsTrue(root, "prerelease")) return null;

            var tag = StringProperty(root, "tag_name");
            var url = StringProperty(root, "html_url");
            if (tag is null || url is null) return null;
            if (!GameVersion.TryParse(tag, out var version)) return null;
            if (!IsReleasePage(url)) return null;

            return new ReleaseInfo(version, tag, url);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// <paramref name="latest"/> if it is newer than the running build, otherwise null. A development
    /// build is never told about updates: it is not a release, so there is nothing to compare.
    /// </summary>
    public static ReleaseInfo? Newer(GameVersion current, ReleaseInfo? latest) =>
        current.IsDevelopment || latest is null || !(latest.Version > current) ? null : latest;

    private static bool IsReleasePage(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host == "github.com"
        && uri.IsDefaultPort
        && uri.AbsolutePath.StartsWith($"/{Repository}/releases/", StringComparison.Ordinal);

    private static bool IsTrue(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? StringProperty(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class UpdateTests
{
    [Theory]
    [InlineData("0.4.1", 0, 4, 1)]
    [InlineData("v0.4.1", 0, 4, 1)]
    [InlineData("0.4.1+b998076abc", 0, 4, 1)]   // what .NET puts in an assembly's version
    [InlineData("  v12.0.30 ", 12, 0, 30)]
    public void Version_ReadsReleasesAndTags(string text, int major, int minor, int patch)
    {
        Assert.True(GameVersion.TryParse(text, out var version));
        Assert.Equal(new GameVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.4")]
    [InlineData("0.4.1.2")]
    [InlineData("0.5.0-beta")]   // a pre-release is never offered
    [InlineData("vx.y.z")]
    [InlineData("-1.0.0")]
    public void Version_RefusesAnythingElse(string? text)
    {
        Assert.False(GameVersion.TryParse(text, out _));
    }

    [Fact]
    public void Version_ComparesNumberByNumber()
    {
        // Compared as numbers, not text: "0.10.0" sorts before "0.9.9" as a string.
        Assert.True(new GameVersion(0, 10, 0) > new GameVersion(0, 9, 9));
        Assert.True(new GameVersion(1, 0, 0) > new GameVersion(0, 99, 99));
        Assert.True(new GameVersion(0, 4, 1) > new GameVersion(0, 4, 0));
        Assert.False(new GameVersion(0, 4, 1) > new GameVersion(0, 4, 1));
    }

    [Fact]
    public void Latest_ReadsGitHubsAnswer()
    {
        var release = Updates.ParseLatest(Release("v0.5.0", "https://github.com/clinta74/tube-runner/releases/tag/v0.5.0"));

        Assert.NotNull(release);
        Assert.Equal(new GameVersion(0, 5, 0), release.Version);
        Assert.Equal("v0.5.0", release.Tag);
        Assert.Equal("https://github.com/clinta74/tube-runner/releases/tag/v0.5.0", release.Url);
    }

    [Theory]
    [InlineData("""{ "tag_name": "v0.5.0", "html_url": "https://github.com/clinta74/tube-runner/releases/tag/v0.5.0", "draft": true }""")]
    [InlineData("""{ "tag_name": "v0.5.0", "html_url": "https://github.com/clinta74/tube-runner/releases/tag/v0.5.0", "prerelease": true }""")]
    [InlineData("""{ "html_url": "https://github.com/clinta74/tube-runner/releases/tag/v0.5.0" }""")]
    [InlineData("""{ "tag_name": "nightly", "html_url": "https://github.com/clinta74/tube-runner/releases/tag/nightly" }""")]
    [InlineData("""{ "message": "API rate limit exceeded" }""")]
    [InlineData("""[1, 2, 3]""")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void Latest_IgnoresAnythingThatIsNotAFinishedRelease(string json)
    {
        Assert.Null(Updates.ParseLatest(json));
    }

    [Theory]
    // The link is opened in a browser, so only this repository's own release pages are accepted.
    [InlineData("https://evil.example/tube-runner/releases/tag/v0.5.0")]
    [InlineData("https://github.com/someone-else/tube-runner/releases/tag/v0.5.0")]
    [InlineData("http://github.com/clinta74/tube-runner/releases/tag/v0.5.0")]
    [InlineData("https://github.com:8443/clinta74/tube-runner/releases/tag/v0.5.0")]
    [InlineData("https://github.com/clinta74/tube-runner/releases/../../../someone-else/releases/x")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    public void Latest_RefusesALinkAnywhereElse(string url)
    {
        Assert.Null(Updates.ParseLatest(Release("v0.5.0", url)));
    }

    [Fact]
    public void Newer_OnlyWhenTheReleaseIsAheadOfThisBuild()
    {
        var release = new ReleaseInfo(new GameVersion(0, 5, 0), "v0.5.0", "https://github.com/clinta74/tube-runner/releases/tag/v0.5.0");

        Assert.Same(release, Updates.Newer(new GameVersion(0, 4, 1), release));
        Assert.Null(Updates.Newer(new GameVersion(0, 5, 0), release));   // already on it
        Assert.Null(Updates.Newer(new GameVersion(0, 6, 0), release));   // ahead of it
        Assert.Null(Updates.Newer(new GameVersion(0, 4, 1), null));      // the check failed
    }

    [Fact]
    public void Newer_NeverForADevelopmentBuild()
    {
        var release = new ReleaseInfo(new GameVersion(0, 5, 0), "v0.5.0", "https://github.com/clinta74/tube-runner/releases/tag/v0.5.0");

        Assert.True(default(GameVersion).IsDevelopment);
        Assert.Null(Updates.Newer(default, release));
    }

    private static string Release(string tag, string url) =>
        $$"""{ "tag_name": "{{tag}}", "html_url": "{{url}}", "draft": false, "prerelease": false }""";
}

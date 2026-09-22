using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class GameSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("""{ "masterVolume": "loud" }""")]
    [InlineData("""{ "screenMode": "hologram" }""")]
    public void MissingOrDamagedSettings_GiveTheDefaults(string? json)
    {
        Assert.Equal(new GameSettings(), GameSettings.FromJson(json));
    }

    [Fact]
    public void Defaults_CheckForUpdatesInAWindowAtFullVolume()
    {
        var settings = new GameSettings();

        Assert.True(settings.CheckForUpdates);
        Assert.Equal(ScreenMode.Windowed, settings.ScreenMode);
        Assert.True(settings.VSync);
        Assert.Equal(1f, settings.MasterVolume);
        Assert.Equal(1f, settings.MusicVolume);
        Assert.Equal(1f, settings.EffectsVolume);
    }

    [Fact]
    public void Settings_SurviveASaveAndLoad()
    {
        var saved = new GameSettings
        {
            CheckForUpdates = false,
            ScreenMode = ScreenMode.Borderless,
            VSync = false,
            MasterVolume = 0.8f,
            MusicVolume = 0.35f,
            EffectsVolume = 0.6f,
        };

        Assert.Equal(saved, GameSettings.FromJson(saved.ToJson()));
    }

    // The window comes back where it was closed, on whichever screen that was - a negative X is a
    // monitor to the left - and maximised if it was. Until the game has been closed from a window
    // there is nowhere to come back to.
    [Fact]
    public void TheWindow_IsRememberedOnceItHasBeenClosed()
    {
        Assert.Null(new GameSettings().Window);

        var saved = new GameSettings { Window = new WindowPlace(-1920, 40, 1600, 900, Maximized: true) };

        Assert.Equal(saved, GameSettings.FromJson(saved.ToJson()));
    }

    [Theory]
    [InlineData("""{ "window": { "x": 0, "y": 0, "width": 10, "height": 10 } }""")]
    [InlineData("""{ "window": { "x": 0, "y": 0, "width": 100000, "height": 600 } }""")]
    [InlineData("""{ "window": null }""")]
    public void AWindowThatCouldNotBeUsed_IsForgotten(string json)
    {
        Assert.Null(GameSettings.FromJson(json).Window);
    }

    [Fact]
    public void Json_IsReadableByAPerson()
    {
        var json = new GameSettings { ScreenMode = ScreenMode.Fullscreen }.ToJson();

        Assert.Contains("\"screenMode\": \"fullscreen\"", json);
        Assert.Contains("\"checkForUpdates\": true", json);
    }

    [Fact]
    public void AFieldMissingFromAnOlderFile_KeepsItsDefault()
    {
        // A file written before a setting existed has to load, with the new setting at its default.
        var settings = GameSettings.FromJson("""{ "musicVolume": 0.25 }""");

        Assert.Equal(0.25f, settings.MusicVolume);
        Assert.True(settings.CheckForUpdates);
        Assert.Equal(1f, settings.MasterVolume);
    }

    [Fact]
    public void OutOfRangeValues_ArePulledBackIn()
    {
        var settings = GameSettings.FromJson("""{ "masterVolume": 3, "musicVolume": -1, "screenMode": 7 }""");

        Assert.Equal(1f, settings.MasterVolume);
        Assert.Equal(0f, settings.MusicVolume);
        Assert.Equal(ScreenMode.Windowed, settings.ScreenMode);
    }
}

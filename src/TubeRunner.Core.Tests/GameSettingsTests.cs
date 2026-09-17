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

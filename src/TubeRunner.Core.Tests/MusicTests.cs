using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

/// <summary>
/// The music tracks are data the synth trusts: four bars of three-note chords with a root each,
/// and a tune of one note per eighth over those bars. A track that breaks the shape would be an
/// index error in the audio thread, which is the worst place to find out.
/// </summary>
public class MusicTests
{
    [Fact]
    public void EveryTrack_HasFourBarsAndATuneToFit()
    {
        Assert.NotEmpty(MusicTracks.All);
        foreach (var track in MusicTracks.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(track.Name));
            Assert.InRange(track.Bpm, 60f, 180f);
            Assert.Equal(4, track.Chords.Length);
            Assert.All(track.Chords, chord => Assert.Equal(3, chord.Length));
            Assert.Equal(4, track.Roots.Length);
            Assert.Equal(32, track.Melody.Length);
            // Roots sit under their chords, and the tune stays in a range a lead can sing.
            for (int bar = 0; bar < 4; bar++) Assert.True(track.Roots[bar] < track.Chords[bar][0]);
            Assert.All(track.Melody, note => Assert.True(note == 0 || note is >= 48 and <= 96));
        }
    }

    [Fact]
    public void Tracks_HaveTheirOwnNames()
    {
        var names = MusicTracks.All.Select(t => t.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal(MusicTracks.DefaultName, MusicTracks.Default.Name);
        Assert.Equal(MusicTracks.TitleName, MusicTracks.Title.Name);
        Assert.NotSame(MusicTracks.Default, MusicTracks.Title);
        Assert.Null(MusicTracks.Get("polka"));
    }

    [Fact]
    public void ALevel_NamesItsTrack_OrPlaysTheDefault()
    {
        Assert.Same(MusicTracks.Default, LevelLoader.Parse(Minimal).Music);
        Assert.Same(MusicTracks.Get("wire"), LevelLoader.Parse(WithMusic("wire")).Music);
    }

    // A track that does not exist is a typo, and a level that loads with the default in its place
    // would never say so.
    [Fact]
    public void ALevelNamingATrackThatDoesNotExist_IsRefused()
    {
        var error = Assert.Throws<LevelFormatException>(() => LevelLoader.Parse(WithMusic("polka")));

        Assert.Contains("polka", error.Message);
        Assert.Contains("wire", error.Message);
    }

    private const string Minimal = """
        {
          "name": "Bare",
          "sections": { "tube": { "radius": 6 } },
          "start": "tube",
          "track": [ { "length": 400 } ]
        }
        """;

    private static string WithMusic(string name) => Minimal.Replace("\"name\": \"Bare\",", $"\"name\": \"Bare\", \"music\": \"{name}\",");
}

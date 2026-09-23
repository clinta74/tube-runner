namespace TubeRunner.Core;

/// <summary>
/// One piece of background music, as the data the synth plays it from: a tempo, four bars of
/// chords with a bass root each, and a tune of one note per eighth over those four bars, where 0 is
/// a rest. Notes are MIDI numbers. Everything else about the sound - the layers, how they arrive
/// with momentum, the unstoppable theme over the top - is the same for every track, so tracks
/// differ in key, pace and tune rather than in instrument, and any of them can follow any other at
/// a bar line without the music changing character.
/// </summary>
public sealed record MusicTrack(string Name, float Bpm, int[][] Chords, int[] Roots, int[] Melody);

/// <summary>
/// The tracks there are. A level names one with its <c>music</c> field and the title has one of its
/// own; a level that names nothing plays <see cref="Default"/>.
/// </summary>
public static class MusicTracks
{
    public const string DefaultName = "run";
    public const string TitleName = "drift";

    public static readonly IReadOnlyList<MusicTrack> All =
    [
        // Slow and open, for the title: D minor, with the tune hanging back on the beat.
        new("drift", 84f,
            [[62, 65, 69], [58, 62, 65], [57, 60, 65], [55, 60, 64]],
            [38, 34, 41, 36],
            [
                69, 0, 0, 72, 0, 74, 0, 0,   72, 0, 69, 0, 65, 0, 0, 0,
                67, 0, 0, 69, 0, 72, 0, 0,   74, 0, 72, 0, 69, 0, 0, 0,
            ]),

        // The original: A minor at a jog, Am F C G.
        new("run", 112f,
            [[57, 60, 64], [53, 57, 60], [48, 52, 55], [55, 59, 62]],
            [45, 41, 36, 43],
            [
                76, 0, 74, 72, 69, 0, 72, 0,   72, 0, 69, 67, 65, 0, 64, 0,
                67, 0, 72, 74, 76, 0, 74, 0,   74, 72, 71, 0, 67, 0, 0, 0,
            ]),

        // Brighter and quicker: E minor, Em C G D, the tune leaning forward.
        new("pulse", 124f,
            [[64, 67, 71], [60, 64, 67], [59, 62, 67], [62, 66, 69]],
            [40, 36, 43, 38],
            [
                71, 0, 74, 0, 76, 0, 74, 71,   72, 0, 71, 0, 67, 0, 0, 0,
                74, 0, 76, 0, 79, 0, 76, 74,   74, 72, 71, 0, 69, 0, 71, 0,
            ]),

        // Slower and darker, for the middle of the run: C minor, Cm Ab Eb Bb, the tune kept low.
        new("undertow", 100f,
            [[60, 63, 67], [56, 60, 63], [58, 63, 67], [58, 62, 65]],
            [36, 32, 39, 34],
            [
                67, 0, 0, 65, 0, 63, 0, 0,   60, 0, 0, 0, 63, 0, 65, 0,
                67, 0, 0, 70, 0, 67, 0, 0,   65, 0, 63, 0, 62, 0, 0, 0,
            ]),

        // The fast finish: F sharp minor, F#m D A E, busiest of the tunes.
        new("wire", 132f,
            [[66, 69, 73], [62, 66, 69], [61, 64, 69], [59, 64, 68]],
            [42, 38, 45, 40],
            [
                73, 0, 76, 73, 71, 0, 69, 0,   69, 0, 71, 0, 73, 0, 0, 0,
                76, 0, 78, 76, 73, 0, 71, 0,   73, 71, 69, 0, 66, 0, 0, 0,
            ]),
    ];

    /// <summary>The track called <paramref name="name"/>, or null if there is none.</summary>
    public static MusicTrack? Get(string name)
    {
        foreach (var track in All)
        {
            if (track.Name == name) return track;
        }
        return null;
    }

    public static MusicTrack Default => Get(DefaultName)!;

    public static MusicTrack Title => Get(TitleName)!;
}

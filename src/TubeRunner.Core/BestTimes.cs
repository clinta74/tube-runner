using System.Text.Json;

namespace TubeRunner.Core;

/// <summary>The player's best finishing time for each level, saved as JSON.</summary>
public sealed class BestTimes
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Prefix on a whole run's key, so it cannot collide with a level's.</summary>
    public const string RunPrefix = "run.";

    /// <summary>
    /// Times used to be keyed by a level's file name, which put the running order into the save: a
    /// level inserted anywhere renumbered the files after it, and every time from there on would
    /// have quietly slid onto the wrong level. Levels now carry an <see cref="Level.Id"/> instead,
    /// and this maps the names as they stood when that changed onto those ids, once, as an old save
    /// is read. The right-hand side is fixed forever; the left-hand side is history, so nothing new
    /// is ever added here.
    /// </summary>
    private static readonly Dictionary<string, string> Renamed = new()
    {
        ["level_00.json"] = "warp-test",
        ["level_01.json"] = "first-loop",
        ["level_02.json"] = "power-up",
        ["level_03.json"] = "throttle",
        ["level_04.json"] = "ovals",
        ["level_05.json"] = "flatlands",
        ["level_06.json"] = "over-and-under",
        ["level_07.json"] = "earthworks",
        ["level_08.json"] = "crossroads",
        ["level_08b.json"] = "loop-the-loop",
        ["level_09.json"] = "neon-run",
        ["level_10.json"] = "clockwork",
        ["level_11.json"] = "metronome",
        ["level_12.json"] = "firing-order",
        ["level_13.json"] = "gauntlet",
        ["level_14.json"] = "speed-trap",
        ["level_15.json"] = "restraint",
        ["level_16.json"] = "shoal",
        ["level_17.json"] = "undertow",
        ["level_18.json"] = "scarlands",
        ["level_19.json"] = "crossfire",
        ["level_20.json"] = "keys",
        ["level_21.json"] = "attrition",
        ["level_22.json"] = "roulette",
        ["level_23.json"] = "highwire",
        ["level_24.json"] = "blackout",
        ["level_25.json"] = "long-odds",
        ["level_26.json"] = "finale",
        ["victory.json"] = "victory",
    };

    private readonly Dictionary<string, float> _times;

    public BestTimes(Dictionary<string, float>? times = null)
    {
        _times = times ?? new();
    }

    public float? Get(string level) => _times.TryGetValue(level, out float time) ? time : null;

    /// <summary>Records a finishing time. Returns true if it's the level's new best.</summary>
    public bool Record(string level, float time)
    {
        if (Get(level) is float best && best <= time) return false;
        _times[level] = time;
        return true;
    }

    public string ToJson() => JsonSerializer.Serialize(_times, Options);

    /// <summary>Reads saved times. A missing or damaged save starts fresh rather than failing.</summary>
    public static BestTimes FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new BestTimes();
        try
        {
            return new BestTimes(Migrate(JsonSerializer.Deserialize<Dictionary<string, float>>(json)));
        }
        catch (JsonException)
        {
            return new BestTimes();
        }
    }

    /// <summary>
    /// Rewrites any key saved under an old file name to the level's id, for both level and run keys.
    /// A key already in the new form, or one naming nothing we know, is left exactly as it is: a save
    /// from a future version should come back unharmed from an older build. Where both forms are
    /// present the faster of the two wins, since they are two records of the same race.
    /// </summary>
    private static Dictionary<string, float> Migrate(Dictionary<string, float>? times)
    {
        if (times is null) return new();

        var migrated = new Dictionary<string, float>(times.Count);
        foreach (var (key, time) in times)
        {
            string moved = key.StartsWith(RunPrefix, StringComparison.Ordinal)
                ? Renamed.TryGetValue(key[RunPrefix.Length..], out string? run) ? RunPrefix + run : key
                : Renamed.GetValueOrDefault(key, key);

            if (!migrated.TryGetValue(moved, out float best) || time < best) migrated[moved] = time;
        }
        return migrated;
    }
}

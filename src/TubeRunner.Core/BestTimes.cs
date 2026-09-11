using System.Text.Json;

namespace TubeRunner.Core;

/// <summary>The player's best finishing time for each level, saved as JSON.</summary>
public sealed class BestTimes
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

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
            return new BestTimes(JsonSerializer.Deserialize<Dictionary<string, float>>(json));
        }
        catch (JsonException)
        {
            return new BestTimes();
        }
    }
}

using System.Text.Json;

namespace TubeRunner.Core;

public sealed class LevelFormatException(string message) : Exception(message);

/// <summary>Reads level files. The format is described in docs/level-format.md.</summary>
public static class LevelLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Level Parse(string json)
    {
        LevelData data;
        try
        {
            data = JsonSerializer.Deserialize<LevelData>(json, Options) ?? throw new LevelFormatException("Level file is empty.");
        }
        catch (JsonException e)
        {
            throw new LevelFormatException($"Invalid JSON: {e.Message}");
        }

        var sections = new Dictionary<string, CrossSection>();
        foreach (var (name, section) in data.Sections) sections[name] = ToSection(name, section);

        if (data.Track.Count == 0) throw new LevelFormatException("Level has no track pieces.");
        var current = Lookup(sections, data.Start, "start");
        var track = new Track(current);

        for (int i = 0; i < data.Track.Count; i++)
        {
            var p = data.Track[i];
            if (p.Length <= 0f) throw new LevelFormatException($"Track piece {i}: length must be positive.");
            var end = p.Section is null ? current : Lookup(sections, p.Section, $"track piece {i}");

            // Level files turn positive-right and climb positive-up; the track yaws positive-left.
            var piece = new TrackPiece(p.Length, end,
                YawRate: -Radians(p.Turn) / p.Length,
                PitchRate: Radians(p.Climb) / p.Length);
            try
            {
                track.Append(piece);
            }
            catch (ArgumentException e)
            {
                throw new LevelFormatException($"Track piece {i}: {e.Message}");
            }
            current = end;
        }

        return new Level
        {
            Name = data.Name,
            Next = data.Next,
            Speed = Positive(data.Speed, "speed"),
            SegmentLength = Positive(data.SegmentLength, "segmentLength"),
            Theme = ToTheme(data.Theme),
            Track = track,
            Obstacles = ToObstacles(data.Obstacles, track),
        };
    }

    private static List<Obstacle> ToObstacles(List<ObstacleData> list, Track track)
    {
        var obstacles = new List<Obstacle>();
        for (int i = 0; i < list.Count; i++)
        {
            var o = list[i];
            string where = $"Obstacle {i}";
            var kind = o.Kind.ToLowerInvariant() switch
            {
                "block" => ObstacleKind.Block,
                "target" => ObstacleKind.Target,
                _ => throw new LevelFormatException($"{where}: unknown kind '{o.Kind}' (use block or target)."),
            };
            var surface = o.Surface.ToLowerInvariant() switch
            {
                "floor" => Surface.Floor,
                "ceiling" => Surface.Ceiling,
                _ => throw new LevelFormatException($"{where}: unknown surface '{o.Surface}' (use floor or ceiling)."),
            };
            if (o.Count < 1 || o.Width <= 0f || o.Length <= 0f || o.Height <= 0f)
            {
                throw new LevelFormatException($"{where}: count and sizes must be positive.");
            }

            for (int n = 0; n < o.Count; n++)
            {
                double s = o.At + n * o.Spacing;
                if (s < 0 || s > track.Length)
                {
                    throw new LevelFormatException($"{where}: 'at' {s:0} is off the track (0 to {track.Length:0}).");
                }

                var (onSurface, x) = (surface, o.X + n * o.XStep);
                if (o.Angle is float angle)
                {
                    var shape = new ProfileShape(track.SectionAt(s));
                    if (!shape.IsClosed)
                    {
                        throw new LevelFormatException($"{where}: 'angle' only works in closed tubes; use 'x' and 'surface' on flat sections.");
                    }
                    // Degrees around the tube from the floor center, positive to the right.
                    (onSurface, x) = shape.Wrap(Surface.Floor, (angle + n * o.AngleStep) / 360f * shape.Perimeter);
                }

                obstacles.Add(new Obstacle
                {
                    Kind = kind,
                    S = s,
                    Surface = onSurface,
                    X = x,
                    Width = o.Width,
                    Length = o.Length,
                    Height = o.Height,
                });
            }
        }
        return obstacles;
    }

    private static CrossSection ToSection(string name, SectionData s)
    {
        float halfWidth = s.Radius > 0f ? s.Radius : s.HalfWidth;
        float halfHeight = s.Radius > 0f ? s.Radius : s.HalfHeight;
        if (halfWidth <= 0f || halfHeight <= 0f)
        {
            throw new LevelFormatException($"Section '{name}': needs a radius, or halfWidth and halfHeight.");
        }
        if (s.Squareness is < 0f or > 1f || s.Opening is < 0f or > 1f)
        {
            throw new LevelFormatException($"Section '{name}': squareness and opening must be between 0 and 1.");
        }
        return new CrossSection(halfWidth, halfHeight, s.Squareness, s.Opening);
    }

    private static Theme ToTheme(ThemeData? t)
    {
        var d = Theme.Earth;
        if (t is null) return d;
        try
        {
            return new Theme(
                Darks: Palette(t.Darks, d.Darks, "darks"),
                Lights: Palette(t.Lights, d.Lights, "lights"),
                SeamDark: Color(t.SeamDark, d.SeamDark),
                SeamLight: Color(t.SeamLight, d.SeamLight),
                Far: Color(t.Far, d.Far),
                Ship: Color(t.Ship, d.Ship),
                Block: Color(t.Block, d.Block),
                Target: Color(t.Target, d.Target),
                FadeStart: t.FadeStart ?? d.FadeStart,
                FadeEnd: t.FadeEnd ?? d.FadeEnd,
                Glow: t.Glow ?? d.Glow);
        }
        catch (FormatException e)
        {
            throw new LevelFormatException($"Theme: {e.Message}");
        }
    }

    private static IReadOnlyList<Rgb> Palette(string[]? hex, IReadOnlyList<Rgb> fallback, string field)
    {
        if (hex is null) return fallback;
        if (hex.Length is < 1 or > Theme.MaxPaletteColors)
        {
            throw new LevelFormatException($"Theme: {field} needs 1 to {Theme.MaxPaletteColors} colors.");
        }
        return hex.Select(Rgb.Parse).ToArray();
    }

    private static Rgb Color(string? hex, Rgb fallback) => hex is null ? fallback : Rgb.Parse(hex);

    private static CrossSection Lookup(Dictionary<string, CrossSection> sections, string name, string where) =>
        sections.TryGetValue(name, out var section)
            ? section
            : throw new LevelFormatException($"{Capitalize(where)}: unknown section '{name}'.");

    private static float Positive(float value, string field) =>
        value > 0f ? value : throw new LevelFormatException($"{field} must be positive.");

    private static float Radians(float degrees) => degrees * MathF.PI / 180f;

    private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    private sealed class LevelData
    {
        public string Name { get; set; } = "Untitled";
        public string? Next { get; set; }
        public float Speed { get; set; } = 80f;
        public float SegmentLength { get; set; } = 60f;
        public ThemeData? Theme { get; set; }
        public Dictionary<string, SectionData> Sections { get; set; } = new();
        public string Start { get; set; } = "";
        public List<PieceData> Track { get; set; } = new();
        public List<ObstacleData> Obstacles { get; set; } = new();
    }

    private sealed class ObstacleData
    {
        public double At { get; set; }
        public string Kind { get; set; } = "block";
        public string Surface { get; set; } = "floor";
        public float X { get; set; }
        public float? Angle { get; set; }
        public float Width { get; set; } = 3f;
        public float Length { get; set; } = 2f;
        public float Height { get; set; } = 2f;
        public int Count { get; set; } = 1;
        public double Spacing { get; set; }
        public float XStep { get; set; }
        public float AngleStep { get; set; }
    }

    private sealed class SectionData
    {
        public float Radius { get; set; }
        public float HalfWidth { get; set; }
        public float HalfHeight { get; set; }
        public float Squareness { get; set; }
        public float Opening { get; set; }
    }

    private sealed class PieceData
    {
        public float Length { get; set; }
        public string? Section { get; set; }
        public float Turn { get; set; }
        public float Climb { get; set; }
    }

    private sealed class ThemeData
    {
        public string[]? Darks { get; set; }
        public string[]? Lights { get; set; }
        public string? SeamDark { get; set; }
        public string? SeamLight { get; set; }
        public string? Far { get; set; }
        public string? Ship { get; set; }
        public string? Block { get; set; }
        public string? Target { get; set; }
        public float? FadeStart { get; set; }
        public float? FadeEnd { get; set; }
        public float? Glow { get; set; }
    }
}

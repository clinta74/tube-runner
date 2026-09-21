using System.Numerics;
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
        // A key we don't recognise is a mistake, not something to skip. Levels are written by hand,
        // and a misspelled "turn" or an invented field that silently does nothing is the worst kind
        // of bug here: the level still loads, still plays, and is quietly not what was written.
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// A level's name, id and successor, without building any of it. Listing the levels in running
    /// order needs only these, and <see cref="Parse"/> lays a whole track to get them.
    /// </summary>
    public static LevelHeader ReadHeader(string json)
    {
        var data = Read(json);
        return new LevelHeader(data.Name, Identifier(data), data.Next);
    }

    public static Level Parse(string json) => Build(Read(json));

    /// <summary>
    /// The level with a straight, empty run of <paramref name="leadIn"/> units put in front of it,
    /// in the section and at the speed it starts with. Everything in the level sits that much
    /// further along and is otherwise untouched. The title screen flies this: a tube that is the
    /// first level's own, with the level itself waiting at the end of it.
    /// </summary>
    public static Level ParseWithLeadIn(string json, float leadIn)
    {
        var data = Read(json);
        data.Track.Insert(0, new PieceData { Length = leadIn });
        // Placements inside a piece move with their piece. Only those given against the whole track
        // have to be moved by hand.
        foreach (var list in new IEnumerable<PlacementData>[] { data.Obstacles, data.Pickups, data.Warps, data.ThrustZones })
        {
            foreach (var placed in list) placed.At += leadIn;
        }
        foreach (var aperture in data.Apertures) aperture.At += leadIn;
        return Build(data);
    }

    private static LevelData Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<LevelData>(json, Options) ?? throw new LevelFormatException("Level file is empty.");
        }
        catch (JsonException e)
        {
            throw new LevelFormatException($"Invalid JSON: {e.Message}");
        }
    }

    private static Level Build(LevelData data)
    {
        var sections = new Dictionary<string, CrossSection>();
        foreach (var (name, section) in data.Sections) sections[name] = ToSection(name, section);

        if (data.Track.Count == 0) throw new LevelFormatException("Level has no track pieces.");
        var current = Lookup(sections, data.Start, "start");
        var track = new Track(current, Positive(data.Speed, "speed"));
        var pieceStarts = new double[data.Track.Count];

        for (int i = 0; i < data.Track.Count; i++)
        {
            var p = data.Track[i];
            pieceStarts[i] = track.Length;
            if (p.Length <= 0f) throw new LevelFormatException($"Track piece {i}: length must be positive.");
            var end = p.Section is null ? current : Lookup(sections, p.Section, $"track piece {i}");

            // Level files turn positive-right and climb positive-up; the track yaws positive-left.
            var piece = new TrackPiece(p.Length, end,
                YawRate: -Radians(p.Turn) / p.Length,
                PitchRate: Radians(p.Climb) / p.Length,
                EndSpeed: p.Speed is float speed ? Positive(speed, $"Track piece {i}: speed") : null);
            try
            {
                if (p.Split is null)
                {
                    track.Append(piece);
                }
                else
                {
                    if (p.Section is not null)
                    {
                        throw new LevelFormatException($"Track piece {i}: a split keeps the current section; change it before or after.");
                    }
                    track.AppendSplit(piece, Lookup(sections, p.Split.Section, $"track piece {i} split"),
                        ToBranches(p.Split, i), ToBranchSections(p.Split, sections, i));
                }
            }
            catch (ArgumentException e)
            {
                throw new LevelFormatException($"Track piece {i}: {e.Message}");
            }
            current = end;
        }

        // Things inside a piece are placed from its start; top-level ones from the track's start.
        var obstacles = ToObstacles(data.Obstacles, track, 0, "Obstacle");
        obstacles.AddRange(ToApertures(data.Apertures, track, 0, "Aperture"));
        var pickups = ToPickups(data.Pickups, track, 0, "Pickup");
        var warps = ToWarps(data.Warps, track, 0, "Warp");
        var thrustZones = ToThrustZones(data.ThrustZones, track, 0, "Thrust zone");
        for (int i = 0; i < data.Track.Count; i++)
        {
            obstacles.AddRange(ToObstacles(data.Track[i].Obstacles, track, pieceStarts[i], $"Track piece {i}, obstacle"));
            obstacles.AddRange(ToApertures(data.Track[i].Apertures, track, pieceStarts[i], $"Track piece {i}, aperture"));
            pickups.AddRange(ToPickups(data.Track[i].Pickups, track, pieceStarts[i], $"Track piece {i}, pickup"));
            warps.AddRange(ToWarps(data.Track[i].Warps, track, pieceStarts[i], $"Track piece {i}, warp"));
            thrustZones.AddRange(ToThrustZones(data.Track[i].ThrustZones, track, pieceStarts[i], $"Track piece {i}, thrust zone"));
        }

        RequireKeysWork(obstacles, pickups);

        return new Level
        {
            Name = data.Name,
            Id = Identifier(data),
            Next = data.Next,
            Speed = Positive(data.Speed, "speed"),
            SegmentLength = Positive(data.SegmentLength, "segmentLength"),
            Theme = ToTheme(data.Theme),
            Track = track,
            Obstacles = obstacles,
            Pickups = pickups,
            Warps = warps,
            ThrustZones = thrustZones,
        };
    }

    /// <summary>
    /// Checks that every 'lockedBy' names a group something is in, and that the whole of that group
    /// stands before the thing it opens. Both mistakes load and play, and both look exactly like the
    /// player having missed a shot: a group that does not exist is a door that never opens or a pad
    /// that never lights, and a key further down the track than its door is one that cannot be shot
    /// in time however well it is flown.
    /// </summary>
    private static void RequireKeysWork(List<Obstacle> obstacles, List<Pickup> pickups)
    {
        var last = new Dictionary<string, double>();
        foreach (var o in obstacles)
        {
            if (o.Group is null) continue;
            last[o.Group] = Math.Max(last.GetValueOrDefault(o.Group, double.MinValue), o.S);
        }

        void Check(string what, double s, string group)
        {
            if (!last.TryGetValue(group, out double key))
            {
                throw new LevelFormatException($"{what} at {s:0}: 'lockedBy' names the group '{group}', which nothing is in.");
            }
            if (key >= s)
            {
                throw new LevelFormatException(
                    $"{what} at {s:0} is locked by '{group}', whose last key is at {key:0} - behind it. "
                    + "A key has to be shot before the thing it opens is reached.");
            }
        }

        foreach (var o in obstacles)
        {
            if (o.LockedBy is not null) Check("Obstacle", o.S, o.LockedBy);
        }
        foreach (var p in pickups)
        {
            if (p.LockedBy is not null) Check("Pickup", p.S, p.LockedBy);
        }
    }

    /// <summary>
    /// A level's <see cref="Level.Id"/>: what the file declares, or its name folded down to one if
    /// it declares none. The fallback is there so a bench or scratch level needs no ceremony;
    /// anything whose times are worth keeping should say its id out loud, because a name reworded
    /// later would otherwise take the best times with it.
    /// </summary>
    private static string Identifier(LevelData data)
    {
        if (!string.IsNullOrWhiteSpace(data.Id)) return data.Id.Trim();

        var id = new System.Text.StringBuilder();
        foreach (char c in data.Name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) id.Append(c);
            else if (id.Length > 0 && id[^1] != '-') id.Append('-');
        }
        return id.ToString().Trim('-') is { Length: > 0 } name ? name : "untitled";
    }

    private static List<IReadOnlyList<OffsetKey>> ToBranches(SplitData split, int piece)
    {
        var branches = new List<IReadOnlyList<OffsetKey>>();
        foreach (var branch in split.Branches)
        {
            var keys = new List<OffsetKey>();
            foreach (var o in branch.Offsets)
            {
                if (o.Length != 3) throw new LevelFormatException($"Track piece {piece}: branch offsets are [along, x, y].");
                keys.Add(new OffsetKey(o[0], new Vector2(o[1], o[2])));
            }
            branches.Add(keys);
        }
        return branches;
    }

    // A branch can name its own section, so the quick way round can be the tighter tube; the rest
    // take the split's.
    private static List<CrossSection> ToBranchSections(SplitData split, Dictionary<string, CrossSection> sections, int piece)
    {
        var result = new List<CrossSection>();
        foreach (var branch in split.Branches)
        {
            result.Add(branch.Section is null
                ? Lookup(sections, split.Section, $"track piece {piece} split")
                : Lookup(sections, branch.Section, $"track piece {piece} branch"));
        }
        return result;
    }

    // Obstacles whose "at" is measured from `offset` along the track; `label` prefixes error messages.
    private static List<Obstacle> ToObstacles(List<ObstacleData> list, Track track, double offset, string label)
    {
        var obstacles = new List<Obstacle>();
        for (int i = 0; i < list.Count; i++)
        {
            var o = list[i];
            string where = $"{label} {i}";
            var kind = o.Kind.ToLowerInvariant() switch
            {
                "block" => ObstacleKind.Block,
                "target" => ObstacleKind.Target,
                "plate" => ObstacleKind.Plate,
                _ => throw new LevelFormatException($"{where}: unknown kind '{o.Kind}' (use block, target or plate)."),
            };
            if (o.Width <= 0f || o.Length <= 0f || o.Height <= 0f)
            {
                throw new LevelFormatException($"{where}: sizes must be positive.");
            }
            if (o.Hits < 0 || (kind != ObstacleKind.Block && o.Hits > 0))
            {
                throw new LevelFormatException($"{where}: 'hits' is for blocks, and can't be negative.");
            }
            if (o.Period < 0f || o.Sweep < 0f || o.SweepTime <= 0f)
            {
                throw new LevelFormatException($"{where}: 'period' and 'sweep' can't be negative, and 'sweepTime' must be positive.");
            }
            if (o.Group is null && o.Order != 0)
            {
                throw new LevelFormatException($"{where}: 'order' needs a 'group' to be ordered within.");
            }
            if (o.Full && kind == ObstacleKind.Target)
            {
                throw new LevelFormatException($"{where}: 'full' is for blocks and plates; a target the whole way round has nowhere to be aimed at.");
            }
            if (o.Full && (o.Period > 0f || o.Sweep > 0f))
            {
                throw new LevelFormatException($"{where}: a 'full' collar cannot also be a gate or a mover.");
            }

            foreach (var (s, branch, surface, x) in Place(o, track, offset, where))
            {
                float width = o.Width;
                if (o.Full)
                {
                    // The whole way round this wall, whatever wall it is: a tube, a ring's outer
                    // wall, or its core - which is why the number is looked up rather than written.
                    var shape = new ProfileShape(track.SectionAt(s, branch));
                    if (!shape.IsClosed)
                    {
                        throw new LevelFormatException($"{where}: 'full' needs a wall that goes all the way round; on a flat section a wall across the whole floor is 'width' 80.");
                    }
                    width = shape.PerimeterOf(surface);
                }

                obstacles.Add(new Obstacle
                {
                    Kind = kind,
                    S = s,
                    Branch = branch,
                    Surface = surface,
                    X = x,
                    Width = width,
                    Full = o.Full,
                    Length = o.Length,
                    Height = o.Height,
                    Hits = o.Hits,
                    Period = o.Period,
                    Phase = o.Phase,
                    Sweep = o.Sweep,
                    SweepTime = o.SweepTime,
                    Group = o.Group,
                    Order = o.Order,
                    LockedBy = o.LockedBy,
                });
            }
        }
        return obstacles;
    }

    /// <summary>
    /// Expands each aperture into the blades that make it up. A blade is an ordinary locked
    /// obstacle covering its share of the way around the tube, so nothing about the collision, the
    /// shooting or unstoppable needs to know apertures exist; only the view does, and it finds them
    /// by the ring name stamped on every blade.
    /// </summary>
    private static List<Obstacle> ToApertures(List<ApertureData> list, Track track, double offset, string label)
    {
        var blades = new List<Obstacle>();
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            string where = $"{label} {i}";
            double s = offset + a.At;
            if (s < 0 || s > track.Length)
            {
                throw new LevelFormatException($"{where}: 'at' {s:0} is off the track (0 to {track.Length:0}).");
            }
            // Three blades is the fewest that reads as a ring turning rather than as a door; past a
            // dozen each one is thinner than the ship and the whole thing is a wall with a texture.
            if (a.Blades is < 3 or > 12) throw new LevelFormatException($"{where}: 'blades' must be between 3 and 12.");
            if (a.Open < 0 || a.Open >= a.Blades)
            {
                throw new LevelFormatException($"{where}: 'open' is how many blades are left out and must be between 0 and {a.Blades - 1}.");
            }
            if (a.Keys.Length == 0) throw new LevelFormatException($"{where}: 'keys' must name at least one target group.");
            if (a.Keys.Length > a.Blades - a.Open)
            {
                throw new LevelFormatException($"{where}: {a.Keys.Length} keys for {a.Blades - a.Open} blades - some key would open nothing.");
            }

            var split = track.SplitAt(s);
            int branch = a.Branch ?? -1;
            if (split is not null && (branch < 0 || branch >= split.BranchCount))
            {
                throw new LevelFormatException($"{where}: at {s:0} the track is split; set 'branch' (0 to {split.BranchCount - 1}).");
            }
            if (split is null && branch >= 0) throw new LevelFormatException($"{where}: 'branch' only applies inside a split.");

            var shape = new ProfileShape(track.SectionAt(s, branch));
            if (!shape.IsClosed)
            {
                throw new LevelFormatException($"{where}: an aperture needs a closed tube to ring; flat sections have no way round.");
            }
            // Blades reach from the wall to the middle of the section, which is where a core sits.
            if (shape.IsAnnulus)
            {
                throw new LevelFormatException($"{where}: an aperture cannot be built in a ring; its blades close on the middle, which the core is already in.");
            }

            float step = shape.Perimeter / a.Blades;
            for (int b = a.Open; b < a.Blades; b++)
            {
                // Blade 0 is centred on the floor, and they run round to the right from there.
                float along = (b + 0.5f) * step + a.Angle / 360f * shape.Perimeter;
                var (surface, x) = shape.Wrap(Surface.Floor, along);
                blades.Add(new Obstacle
                {
                    Kind = ObstacleKind.Block,
                    S = s,
                    Branch = branch,
                    Surface = surface,
                    X = x,
                    Width = step,
                    Length = a.Length,
                    Height = a.Height,
                    Hits = a.Hits,
                    LockedBy = a.Keys[(b - a.Open) % a.Keys.Length],
                    Aperture = $"{label.ToLowerInvariant().Replace(' ', '-')}-{i}",
                    Blade = b,
                    BladeCount = a.Blades,
                });
            }
        }
        return blades;
    }

    private static List<Pickup> ToPickups(List<PickupData> list, Track track, double offset, string label)
    {
        var pickups = new List<Pickup>();
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i];
            string where = $"{label} {i}";
            var kind = p.Kind.ToLowerInvariant() switch
            {
                "shield" => PickupKind.Shield,
                "full-shields" => PickupKind.FullShields,
                "shield-slot" => PickupKind.ShieldSlot,
                "rapid-fire" => PickupKind.RapidFire,
                "ring-gun" => PickupKind.RingGun,
                "unstoppable" => PickupKind.Unstoppable,
                "agility" => PickupKind.Agility,
                _ => throw new LevelFormatException(
                    $"{where}: unknown kind '{p.Kind}' (use shield, full-shields, shield-slot, rapid-fire, ring-gun, unstoppable, or agility)."),
            };

            foreach (var (s, branch, surface, x) in Place(p, track, offset, where))
            {
                pickups.Add(new Pickup
                {
                    Kind = kind, S = s, Branch = branch, Surface = surface, X = x, LockedBy = p.LockedBy,
                });
            }
        }
        return pickups;
    }

    // The one place a warp's defaults live, so the loader cannot disagree with the type again.
    private static readonly Warp WarpDefaults = new() { S = 0 };

    private static List<Warp> ToWarps(List<WarpData> list, Track track, double offset, string label)
    {
        var warps = new List<Warp>();
        for (int i = 0; i < list.Count; i++)
        {
            var w = list[i];
            string where = $"{label} {i}";
            if (w.Back < 0f) throw new LevelFormatException($"{where}: 'back' can't be negative.");
            if (w.Span is <= 0f or > 0.4f)
            {
                throw new LevelFormatException(
                    $"{where}: 'span' is the share of the way round the tube and must be over 0 and at most 0.4. "
                    + "Past that there is not enough wall left to fly.");
            }
            if (w.Length is <= 0f) throw new LevelFormatException($"{where}: 'length' must be positive.");

            foreach (var (s, branch, surface, x) in Place(w, track, offset, where))
            {
                // A well is sunk into the wall by the renderer against one closed profile, and its
                // width is a share of that profile - neither of which means anything on a core.
                if (track.SectionAt(s, branch).IsAnnulus)
                {
                    throw new LevelFormatException($"{where}: a warp well cannot be sunk into a ring's wall.");
                }
                // Defaults come from the Warp itself rather than being written out again here. They
                // were duplicated once, and the copy in this file quietly won: every well in the
                // game was a sixth of its intended size for as long as that went unnoticed.
                warps.Add(new Warp
                {
                    S = s,
                    Branch = branch,
                    Surface = surface,
                    X = x,
                    Span = w.Span ?? WarpDefaults.Span,
                    Length = w.Length ?? WarpDefaults.Length,
                    Back = w.Back,
                });
            }
        }
        return warps;
    }

    private static List<ThrustZone> ToThrustZones(List<ThrustZoneData> list, Track track, double offset, string label)
    {
        var zones = new List<ThrustZone>();
        for (int i = 0; i < list.Count; i++)
        {
            var z = list[i];
            string where = $"{label} {i}";
            if (z.Length <= 0f) throw new LevelFormatException($"{where}: 'length' must be positive.");
            var kind = z.Kind.ToLowerInvariant() switch
            {
                "floor" => ThrustZoneKind.Floor,
                "ceiling" => ThrustZoneKind.Ceiling,
                _ => throw new LevelFormatException($"{where}: unknown kind '{z.Kind}' (use floor or ceiling)."),
            };

            foreach (var (s, branch, _, _) in Place(z, track, offset, where))
            {
                zones.Add(new ThrustZone { S = s, Branch = branch, Length = z.Length, Kind = kind });
            }
        }
        return zones;
    }

    // Where a placement and each of its repeats land on the track.
    private static IEnumerable<(double S, int Branch, Surface Surface, float X)> Place(
        PlacementData p, Track track, double offset, string where)
    {
        var surface = p.Surface.ToLowerInvariant() switch
        {
            "floor" => Surface.Floor,
            "ceiling" => Surface.Ceiling,
            _ => throw new LevelFormatException($"{where}: unknown surface '{p.Surface}' (use floor or ceiling)."),
        };
        if (p.Count < 1) throw new LevelFormatException($"{where}: count must be at least 1.");

        for (int n = 0; n < p.Count; n++)
        {
            double s = offset + p.At + n * p.Spacing;
            if (s < 0 || s > track.Length)
            {
                throw new LevelFormatException($"{where}: 'at' {s:0} is off the track (0 to {track.Length:0}).");
            }

            var split = track.SplitAt(s);
            int branch = p.Branch ?? -1;
            if (split is not null && (branch < 0 || branch >= split.BranchCount))
            {
                throw new LevelFormatException($"{where}: at {s:0} the track is split; set 'branch' (0 to {split.BranchCount - 1}).");
            }
            if (split is null && branch >= 0)
            {
                throw new LevelFormatException($"{where}: 'branch' only applies inside a split.");
            }

            var (onSurface, x) = (surface, p.X + n * p.XStep);
            if (p.Angle is float angle)
            {
                var shape = new ProfileShape(track.SectionAt(s, branch));
                if (!shape.IsClosed)
                {
                    throw new LevelFormatException($"{where}: 'angle' only works in closed tubes; use 'x' and 'surface' on flat sections.");
                }
                // Degrees around the tube from the floor center, positive to the right.
                (onSurface, x) = shape.Wrap(surface, (angle + n * p.AngleStep) / 360f * shape.PerimeterOf(surface));
            }
            yield return (s, branch, onSurface, x);
        }
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
        var section = new CrossSection(halfWidth, halfHeight, s.Squareness, s.Opening);
        if (s.RingHeight <= 0f) return section;

        if (s.Opening > 0f)
        {
            throw new LevelFormatException(
                $"Section '{name}': a section cannot have a 'ringHeight' and be open; there would be nothing for the core to sit inside.");
        }
        // The floor is what the ship and its jump need. The ceiling is a share of the bore, so a
        // wider one carries a taller ring: what has to be left behind is a core big enough to be a
        // wall in its own right rather than a pole down the middle.
        if (s.RingHeight < CrossSection.MinRingHeight || s.RingHeight > section.MaxRingHeight)
        {
            throw new LevelFormatException(
                $"Section '{name}': 'ringHeight' is the room the ring leaves to fly in and must be between "
                + $"{CrossSection.MinRingHeight:0.#} and {section.MaxRingHeight:0.#} for a bore this size. "
                + "Widen the section to allow a taller ring.");
        }
        return section.WithRing(s.RingHeight);
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
                Breakable: Color(t.Breakable, d.Breakable),
                FadeStart: t.FadeStart ?? d.FadeStart,
                FadeEnd: t.FadeEnd ?? d.FadeEnd,
                Glow: t.Glow ?? d.Glow,
                Wire: t.Wire ?? d.Wire);
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
        public string? Id { get; set; }
        public string? Next { get; set; }
        public float Speed { get; set; } = 80f;
        public float SegmentLength { get; set; } = 60f;
        public ThemeData? Theme { get; set; }
        public Dictionary<string, SectionData> Sections { get; set; } = new();
        public string Start { get; set; } = "";
        public List<PieceData> Track { get; set; } = new();
        public List<ObstacleData> Obstacles { get; set; } = new();
        public List<ApertureData> Apertures { get; set; } = new();
        public List<PickupData> Pickups { get; set; } = new();
        public List<WarpData> Warps { get; set; } = new();
        public List<ThrustZoneData> ThrustZones { get; set; } = new();
    }

    private sealed class SectionData
    {
        public float Radius { get; set; }
        public float HalfWidth { get; set; }
        public float HalfHeight { get; set; }
        public float Squareness { get; set; }
        public float Opening { get; set; }
        public float RingHeight { get; set; }
    }

    private sealed class PieceData
    {
        public float Length { get; set; }
        public string? Section { get; set; }
        public float Turn { get; set; }
        public float Climb { get; set; }
        public float? Speed { get; set; }
        public SplitData? Split { get; set; }
        public List<ObstacleData> Obstacles { get; set; } = new();
        public List<ApertureData> Apertures { get; set; } = new();
        public List<PickupData> Pickups { get; set; } = new();
        public List<WarpData> Warps { get; set; } = new();
        public List<ThrustZoneData> ThrustZones { get; set; } = new();
    }

    private sealed class ApertureData
    {
        public double At { get; set; }
        public int? Branch { get; set; }
        public int Blades { get; set; } = 6;
        public string[] Keys { get; set; } = [];
        public int Open { get; set; }
        public float Angle { get; set; }
        public float Length { get; set; } = 2.5f;
        public float Height { get; set; } = 2.5f;
        public int Hits { get; set; }
    }

    // No numbers of its own: each kind makes a fixed cut. A level still carrying 'min' or 'max' fails
    // to load rather than quietly flying differently from how it was written.
    private sealed class ThrustZoneData : PlacementData
    {
        public float Length { get; set; } = 120f;
        public string Kind { get; set; } = "floor";
    }

    private sealed class SplitData
    {
        public string Section { get; set; } = "";
        public List<BranchData> Branches { get; set; } = new();
    }

    private sealed class BranchData
    {
        public List<float[]> Offsets { get; set; } = new();
        public string? Section { get; set; }
    }

    private sealed class WarpData : PlacementData
    {
        /// <summary>Share of the way round the tube; null takes the game's own default.</summary>
        public float? Span { get; set; }

        /// <summary>Extent along the track; null takes the game's own default.</summary>
        public float? Length { get; set; }

        /// <summary>How far back it throws the ship; 0 takes the game's default.</summary>
        public float Back { get; set; }
    }

    // Where something sits on the track, with optional repeats.
    private class PlacementData
    {
        public double At { get; set; }
        public string Surface { get; set; } = "floor";
        public int? Branch { get; set; }
        public float X { get; set; }
        public float? Angle { get; set; }
        public int Count { get; set; } = 1;
        public double Spacing { get; set; }
        public float XStep { get; set; }
        public float AngleStep { get; set; }
    }

    private sealed class ObstacleData : PlacementData
    {
        public string Kind { get; set; } = "block";
        public float Width { get; set; } = 3f;
        public bool Full { get; set; }
        public float Length { get; set; } = 2f;
        public float Height { get; set; } = 2f;
        public int Hits { get; set; }
        public float Period { get; set; }
        public float Phase { get; set; }
        public float Sweep { get; set; }
        public float SweepTime { get; set; } = 2f;
        public string? Group { get; set; }
        public int Order { get; set; }
        public string? LockedBy { get; set; }
    }

    private sealed class PickupData : PlacementData
    {
        public string Kind { get; set; } = "";
        public string? LockedBy { get; set; }
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
        public string? Breakable { get; set; }
        public float? FadeStart { get; set; }
        public float? FadeEnd { get; set; }
        public float? Glow { get; set; }
        public bool? Wire { get; set; }
    }
}

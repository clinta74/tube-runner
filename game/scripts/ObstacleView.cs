using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TubeRunner.Core;
using Theme = TubeRunner.Core.Theme;
// Track-space maths comes back in System.Numerics vectors, which collide with Godot's own.
using Vec3 = System.Numerics.Vector3;

namespace TubeRunner.Game;

/// <summary>
/// Draws a <see cref="GameSession"/>'s obstacles, power-ups, shots, ring shots, and bursts. Things
/// are anchored in world space and re-placed relative to the floating origin each frame. Power-ups
/// and breakable blocks use placeholder looks until they get real art.
/// </summary>
public partial class ObstacleView : Node3D
{
    private const float BurstSeconds = 0.8f;
    private const int FunnelSegments = 40;

    // Placeholder power-up looks: pad color and floating label.
    private static readonly Dictionary<PickupKind, (Color Color, string Label)> PickupLooks = new()
    {
        [PickupKind.Shield] = (new Color(0.3f, 1f, 0.45f), "+1"),
        [PickupKind.FullShields] = (new Color(0.2f, 0.9f, 1f), "MAX"),
        [PickupKind.ShieldSlot] = (new Color(1f, 0.85f, 0.3f), "SLOT"),
        [PickupKind.RapidFire] = (new Color(1f, 0.5f, 0.15f), "RAPID"),
        [PickupKind.RingGun] = (new Color(1f, 0.3f, 1f), "RING"),
        [PickupKind.Unstoppable] = (new Color(1f, 0.2f, 0.2f), "RAM"),
        [PickupKind.Agility] = (new Color(0.62f, 0.5f, 1f), "AGILE"),
    };

    // Warning signs stand in one ring around the tube, this far back from a warp well.
    private const float SignRing = 75f;
    private const int SignsInRing = 4;

    /// <summary>Seconds a blade takes to swing out of the way once its key falls.</summary>
    private const float IrisTime = 0.45f;

    /// <summary>How far in a shut blade reaches, as a share of the way to the middle. Not quite 0:
    /// blades that met at a point would flicker against each other there.</summary>
    private const float IrisShut = 0.06f;

    /// <summary>Blades are cut a little wider than their share, so they overlap rather than leave seams.</summary>
    private const float BladeOverlap = 1.12f;

    /// <summary>How far round its own sector a blade turns as it opens. What makes it read as an iris.</summary>
    private const float BladeSwirl = 0.9f;

    private readonly Dictionary<Obstacle, View> _views = new();
    private readonly Dictionary<Obstacle, View> _socketViews = new();
    private readonly Dictionary<Obstacle, View> _lipViews = new();
    private readonly List<Obstacle> _apertureRings = new();
    private readonly Dictionary<(double S, bool Opens, Surface Surface), View> _jumpMarkViews = new();
    private readonly List<(double S, bool Opens, Surface Surface)> _jumpMarks = new();
    private readonly Dictionary<Pickup, View> _pickupViews = new();
    private readonly Dictionary<(Warp Warp, int Index), View> _signViews = new();
    private readonly Dictionary<Warp, View> _dustViews = new();
    private readonly Dictionary<PickupKind, StandardMaterial3D> _pickupMaterials = new();
    private readonly List<MeshInstance3D> _shotViews = new();
    private readonly List<MeshInstance3D> _ringViews = new();
    private readonly List<Burst> _bursts = new();
    private readonly List<Flung> _flung = new();
    private readonly ProfileShapeCache _shapes = new();
    private GameSession _session = null!;
    private Func<Obstacle, View> _createObstacle = null!;
    private Func<Obstacle, View> _createSocket = null!;
    private Func<Obstacle, View> _createLip = null!;
    private Func<(double S, bool Opens, Surface Surface), View> _createJumpMark = null!;
    private Func<Pickup, View> _createPickup = null!;
    private Func<(Warp Warp, int Index), View> _createSign = null!;
    private Func<Warp, View> _createDust = null!;
    private StandardMaterial3D _dustMaterial = null!;
    private StandardMaterial3D _plateMaterial = null!;
    private StandardMaterial3D _iconMaterial = null!;
    private Mesh _signMesh = null!;
    private Mesh _signBorder = null!;
    private Mesh _signPost = null!;
    private Mesh _wellIcon = null!;
    private StandardMaterial3D _blockMaterial = null!;
    private StandardMaterial3D _hazardMaterial = null!;
    private StandardMaterial3D _gateMaterial = null!;
    private StandardMaterial3D _jumpOpenMaterial = null!;
    private StandardMaterial3D _jumpCloseMaterial = null!;

    // Hues for key-and-door pairs. Distinct from each other and from the target pink, the hazard
    // orange and the breakable green, so a key never reads as one of those.
    private static readonly Color[] KeyColors =
    {
        new(0.35f, 0.85f, 1f),    // cyan
        new(0.75f, 1f, 0.35f),    // lime
        new(1f, 0.55f, 0.95f),    // orchid
        new(1f, 0.85f, 0.3f),     // gold
    };

    // Group name to its place in the level, so colours are handed out in the order the player meets
    // the pairs. Hashing the name instead would let two pairs on screen at once land on the same
    // colour by chance, which is exactly the case the colour exists to disambiguate.
    private readonly Dictionary<string, int> _keyGroups = new();
    private readonly Dictionary<string, StandardMaterial3D> _keyMaterials = new();
    private readonly Dictionary<string, StandardMaterial3D> _doorMaterials = new();
    private readonly Dictionary<string, StandardMaterial3D> _bladeMaterials = new();
    private StandardMaterial3D _deadPadMaterial = null!;
    private readonly Dictionary<string, StandardMaterial3D> _waitingMaterials = new();
    private Color _targetColor;
    private float _themeGlow;
    private StandardMaterial3D _socketMaterial = null!;
    private StandardMaterial3D _targetMaterial = null!;
    private StandardMaterial3D _shotMaterial = null!;
    private StandardMaterial3D _ringMaterial = null!;
    private Color _breakable;
    private float _glow;
    private Mesh _shotMesh = null!;
    private Mesh _burstMesh = null!;
    private Mesh _ringMesh = null!;
    private Mesh _padMesh = null!;
    private float _time;
    // In the see-through style, the distances between which things are dithered in.
    private (float From, float To)? _wireFade;

    /// <summary>How far ahead views are made in the solid style, where the fade hides the rest.</summary>
    [Export] public float ViewAhead { get; set; } = 450f;

    /// <summary>A fixed reach instead of the style's own, for looking at the difference (`--view`).</summary>
    public float? ViewOverride { get; set; }

    // What this level actually uses. A wire level sees much further than a solid one, and obstacles
    // appearing out of nothing well inside a tube the player can already see reads worse than the
    // popping it replaced.
    private float _viewAhead;
    [Export] public float ViewBehind { get; set; } = 20f;
    [Export] public float RideHeight { get; set; } = 0.6f;

    /// <summary>Frees the current level's views, ready for <see cref="Init"/> with the next one.</summary>
    public void Reset()
    {
        foreach (var view in _views.Values) view.Node.QueueFree();
        foreach (var view in _socketViews.Values) view.Node.QueueFree();
        foreach (var view in _lipViews.Values) view.Node.QueueFree();
        foreach (var view in _jumpMarkViews.Values) view.Node.QueueFree();
        foreach (var view in _pickupViews.Values) view.Node.QueueFree();
        foreach (var view in _signViews.Values) view.Node.QueueFree();
        foreach (var view in _dustViews.Values) view.Node.QueueFree();
        foreach (var node in _shotViews) node.QueueFree();
        foreach (var node in _ringViews) node.QueueFree();
        foreach (var burst in _bursts) burst.Node.QueueFree();
        foreach (var flung in _flung) flung.Node.QueueFree();
        _views.Clear();
        // Sockets and jump marks are keyed by things that do not survive a level change, so without
        // clearing these their nodes would stay in the tree for the rest of the run, unreachable.
        _socketViews.Clear();
        _lipViews.Clear();
        _apertureRings.Clear();
        _jumpMarkViews.Clear();
        _dustViews.Clear();
        _pickupViews.Clear();
        _signViews.Clear();
        _shotViews.Clear();
        _ringViews.Clear();
        _bursts.Clear();
        _flung.Clear();
    }

    /// <param name="jumpWindows">
    /// Stretches where the ship can cross between floor and ceiling. Their edges are marked on the
    /// wall, so the window can be seen coming rather than found by trying the button against it.
    /// </param>
    public void Init(GameSession session, Theme theme, IReadOnlyList<JumpWindow>? jumpWindows = null)
    {
        _session = session;
        _createObstacle = CreateObstacleView;
        _createSocket = CreateSocketView;
        _createLip = CreateApertureLipView;
        _createJumpMark = CreateJumpMarkView;

        _jumpMarks.Clear();
        foreach (var window in jumpWindows ?? Array.Empty<JumpWindow>())
        {
            // A ring is left unmarked. A mark is a line laid across a surface, which is a line on a
            // flat section and a band right round the wall in a ring - and a band across a wide bore
            // reads as a gate to fly through rather than as a note about where jumping starts. The
            // HUD's own cue says the same thing without standing in the tube.
            //
            // Judged from the middle of the window, not its edges: an edge is found by bisection and
            // lands a hair outside the ring, on a section that is not one, which is how the marks
            // came back after being taken out.
            if (_shapes.Get(session.Track.SectionAt((window.From + window.To) * 0.5)).IsAnnulus) continue;

            foreach (var (at, opens) in new[] { (window.From, true), (window.To, false) })
            {
                // Both surfaces: the ship can be riding either one when the window opens or shuts.
                _jumpMarks.Add((at, opens, Surface.Floor));
                _jumpMarks.Add((at, opens, Surface.Ceiling));
            }
        }
        _createPickup = CreatePickupView;
        _createSign = CreateSignView;
        _createDust = CreateWarpDustView;
        _glow = theme.Glow;
        _breakable = theme.Breakable.ToColor();

        // In the see-through style the walls' lines dim with distance and the track is built out to
        // twice the fade, so things on the walls are made that far out too. Solid at that distance
        // they popped in at full brightness far down a tube already dimmed to a quarter. So out
        // there they are dithered in: nothing where the track's reach ends, whole by the fade
        // distance, arriving the way the lines do. A solid level hides its far end behind the fade
        // and needs none of this.
        _wireFade = theme.Wire ? (theme.FadeEnd, theme.FadeEnd * 2f) : null;

        _blockMaterial = Solid(theme.Block.ToColor());
        // Hazard stripes for plates. Nothing else in the game is this colour, because nothing else
        // has to be read as "no way through this one" from as far off as the player can see it.
        _hazardMaterial = Glowing(new Color(1f, 0.42f, 0.05f), 1.6f + theme.Glow);
        // Gates take the level's own seam colour rather than the block white, which is the brightest
        // thing on screen and painful to stare at for a stretch built around watching one thing.
        _gateMaterial = Glowing(theme.SeamLight.ToColor().Darkened(0.25f), 0.6f + theme.Glow);
        // Dust being pulled into a well. A well is a hole in a dark wall and holds still, which is
        // most of why three passes at making it bigger never finished the job - nothing about it
        // moved. This is the part that says the thing is live.
        _dustMaterial = Glowing(theme.SeamLight.ToColor(), 1.8f + theme.Glow);
        // The line where jumping starts and the line where it stops. Different colours because they
        // mean opposite things, and a mark that only says "something changes here" is half a mark.
        _jumpOpenMaterial = Glowing(new Color(0.45f, 1f, 0.7f), 1.7f + theme.Glow);
        _jumpCloseMaterial = Glowing(new Color(1f, 0.72f, 0.2f), 1.7f + theme.Glow);

        // A key and the door it opens share a colour. Without it a key is just another target and a
        // door is just another block, which makes the whole mechanic guesswork - and where a stretch
        // has two pairs interleaved, knowing which key opens which is the entire puzzle.
        _themeGlow = theme.Glow;
        _targetColor = theme.Target.ToColor();
        // Matches the track's own reach, so obstacles and the tube they sit in appear together.
        _viewAhead = ViewOverride ?? (theme.Wire ? Math.Max(ViewAhead, theme.FadeEnd * 2f) : ViewAhead);
        _keyGroups.Clear();
        _keyMaterials.Clear();
        _doorMaterials.Clear();
        _bladeMaterials.Clear();
        _waitingMaterials.Clear();
        // One blade of each aperture stands for the whole ring, for the lip left in the wall. The
        // lip stays whether the ring is shut, part open or long gone, for the same reason a gate
        // keeps its socket: where the machinery is should be readable on the approach.
        _apertureRings.Clear();
        var rings = new HashSet<string>();
        foreach (var o in session.Obstacles)
        {
            if (o.Aperture is not null && rings.Add(o.Aperture)) _apertureRings.Add(o);
        }
        // Obstacles and pads both arrive sorted by distance, so merging them by distance walks the
        // level in the order it is flown - and a pad is as much a locked thing as a door is, so a
        // stretch whose only lock switches a power-up on still gets the next colour in the cycle.
        var locked = session.Obstacles.Select(o => (o.S, o.LockedBy))
            .Concat(session.Pickups.Select(p => (p.S, p.LockedBy)))
            .Where(l => l.LockedBy is not null)
            .OrderBy(l => l.S);
        foreach (var (_, group) in locked)
        {
            if (!_keyGroups.ContainsKey(group!)) _keyGroups[group!] = _keyGroups.Count;
        }
        // The socket a gate withdraws into, left on the wall so its position is readable even when
        // nothing is standing there. Dark, unlit, and flush: a mark, not an obstacle.
        _socketMaterial = Faded(new StandardMaterial3D
        {
            AlbedoColor = theme.SeamDark.ToColor(),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        });
        // Everything below is set against the glow's HDR threshold of 1.3 in main.tscn. Under it a
        // material is flat paint; over it, it blooms. Move that threshold and these move with it.
        _targetMaterial = Glowing(theme.Target.ToColor(), 1.9f + theme.Glow);
        _shotMaterial = Glowing(theme.SeamLight.ToColor(), 4f);
        _ringMaterial = Glowing(new Color(1f, 0.3f, 1f), 4f);
        _ringMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        foreach (var (kind, look) in PickupLooks) _pickupMaterials[kind] = Glowing(look.Color, 2.5f);

        // A road sign: a yellow triangular plate with a black hole on it.
        _plateMaterial = Glowing(new Color(1f, 0.86f, 0.32f), 2.1f);
        _plateMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        _iconMaterial = Faded(new StandardMaterial3D
        {
            AlbedoColor = new Color(0.02f, 0.02f, 0.02f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        // A three-sided cylinder is a flat triangular plate; a many-sided one is the disc on it. The
        // border is a slightly wider, thinner triangle behind, so the yellow reads with a dark rim
        // rather than as another lump on the wall - a sign that looks like a block is worse than no
        // sign, because it is read as something to dodge instead of something to heed.
        _signMesh = new CylinderMesh { TopRadius = 1.9f, BottomRadius = 1.9f, Height = 0.09f, RadialSegments = 3 };
        _signBorder = new CylinderMesh { TopRadius = 2.25f, BottomRadius = 2.25f, Height = 0.05f, RadialSegments = 3 };
        _signPost = new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.07f, Height = 1.4f, RadialSegments = 6 };
        _wellIcon = new CylinderMesh { TopRadius = 0.8f, BottomRadius = 0.8f, Height = 0.06f, RadialSegments = 24 };

        // A pad whose keys are still standing. Unlit and almost black, so it reads as switched off
        // rather than as a power-up in an unusual colour - the label on it says what it will be.
        _deadPadMaterial = Faded(new StandardMaterial3D
        {
            AlbedoColor = new Color(0.08f, 0.09f, 0.11f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        });

        _shotMesh = new CapsuleMesh { Radius = 0.12f, Height = 1.4f };
        _burstMesh = new SphereMesh { Radius = 0.15f, Height = 0.3f, RadialSegments = 6, Rings = 3 };
        // A unit ring in the XZ plane, stretched to each section's size.
        _ringMesh = new TorusMesh { InnerRadius = 0.94f, OuterRadius = 1f, Rings = 64, RingSegments = 6 };
        _padMesh = new CylinderMesh { TopRadius = 1.5f, BottomRadius = 1.5f, Height = 0.12f, RadialSegments = 24 };
    }

    /// <summary>
    /// Where the ship is along this track, when it is not this session's ship that says: a view of
    /// the next level, drawn ahead of the one being flown, measures from a ship that is still on
    /// the level before, and may be a long way short of its own start.
    /// </summary>
    public double? ShipS { get; set; }

    /// <summary>
    /// The clock movers and gates run on, when it is not this session's: a view of the next level
    /// is drawn from a session that is never stepped, and its clock is set from outside to the time
    /// until that level starts, counting up to zero, so what moves in its opening is already moving
    /// as the ship approaches and is exactly where the real clock will find it at the line.
    /// </summary>
    public float? Clock { get; set; }

    private float Now => Clock ?? _session.Elapsed;

    /// <summary>
    /// Points the view at another session over the same level. The obstacles, pads and wells are
    /// the level's own objects and the views are keyed by them, so nothing on screen changes; only
    /// whose clock and whose keys are asked from here on.
    /// </summary>
    public void Rebind(GameSession session) => _session = session;

    private float _lastDt;

    public void UpdateView(Vector3d origin, float dt)
    {
        _time += dt;
        _lastDt = dt;
        double s = ShipS ?? _session.Ship.Position.S;

        // Warning signs pulse. Nothing else on a wall does, so movement is what separates a sign
        // from an obstacle at the distance where it still matters which one you are looking at.
        _plateMaterial.EmissionEnergyMultiplier = 2.0f + 0.9f * Mathf.Sin(_time * 5f);

        // A gate keeps its view the whole time and slides into the wall instead, so the eye can
        // follow it. The burst flag stays tied to being destroyed alone - otherwise every cycle
        // would set off a fake explosion. Each gate also leaves a socket on the wall, so where one
        // lives can be read on the approach even while it is withdrawn.
        foreach (var o in _session.Obstacles)
        {
            if (o.Aperture is not null)
            {
                UpdateBlade(o, s, origin);
                continue;
            }
            // A door whose keys have been shot is no longer solid, so it must stop being drawn -
            // otherwise it stands there looking like a wall and the ship sails through it, and the
            // whole point of shooting the key is lost. It bursts as it goes, so the shot that opened
            // it has something to show for itself.
            bool open = o.LockedBy is not null && _session.IsUnlocked(o);
            bool there = !o.Destroyed && !open && InView(o.S, s);
            Sync(_views, o, there, o.Destroyed || open, _createObstacle, origin);
            // A collar has no socket: it is a band round the whole wall, with nowhere to withdraw to.
            if (o.Period > 0f && !o.Full) Sync(_socketViews, o, there, burst: false, _createSocket, origin);
        }
        foreach (var ring in _apertureRings) Sync(_lipViews, ring, InView(ring.S, s), burst: false, _createLip, origin);
        foreach (var p in _session.Pickups)
        {
            Sync(_pickupViews, p, !p.Collected && InView(p.S, s), p.Collected, _createPickup, origin);
            // A locked pad switches on the moment its keys are down. Nothing else about it changes,
            // so the pad the player flew past dark is recognisably the one now lit.
            if (_pickupViews.TryGetValue(p, out var pad)) ShowPadPower(p, pad);
        }
        foreach (var mark in _jumpMarks)
        {
            Sync(_jumpMarkViews, mark, InView(mark.S, s), burst: false, _createJumpMark, origin);
        }
        foreach (var w in _session.Warps)
        {
            // The well itself is the tube wall extruded into it by TrackRenderer; the dust and the
            // signs are here.
            Sync(_dustViews, w, InView(w.S, s), burst: false, _createDust, origin);
            for (int i = 0; i < SignsInRing; i++)
            {
                Sync(_signViews, (w, i), InView(w.S - SignRing, s), burst: false, _createSign, origin);
            }
        }
        foreach (var (o, view) in _views)
        {
            if (o.Hits > 0 && view.HitsShown != o.HitsTaken) ShowDamage(o, view);
            if (o.Kind == ObstacleKind.Target && o.Group is not null) ShowTurn(o, view);
        }

        UpdateShots(origin);
        UpdateRings(origin);
        UpdateBursts(origin);
    }

    private bool InView(double at, double s) => at > s - ViewBehind && at < s + _viewAhead;

    // Creates, places, or removes the view for one item. Items removed because they were broken or
    // collected burst apart.
    private void Sync<T>(Dictionary<T, View> views, T item, bool wanted, bool burst, Func<T, View> create, Vector3d origin)
        where T : notnull
    {
        if (views.TryGetValue(item, out var view))
        {
            if (wanted)
            {
                Place(view, origin);
                return;
            }
            if (burst) SpawnBurst(view.Center, view.BurstMaterial);
            view.Node.QueueFree();
            views.Remove(item);
        }
        else if (wanted)
        {
            view = create(item);
            views[item] = view;
            Place(view, origin);
        }
    }

    private View CreateObstacleView(Obstacle o)
    {
        if (o.Aperture is not null) return CreateBladeView(o);
        if (o.Full) return CreateCollarView(o);

        var (center, forward, up) = Pose(o.S, o.Branch, o.Surface, o.XAt(Now), o.Height / 2f);
        bool target = o.Kind == ObstacleKind.Target;
        float size = Mathf.Min(o.Width, o.Height);
        Mesh mesh = target
            // A four-sided "sphere" with one ring is a diamond.
            ? new SphereMesh { Radius = size / 2f, Height = size, RadialSegments = 4, Rings = 1 }
            : new BoxMesh { Size = new Vector3(o.Width, o.Height, o.Length) };
        // Breakable blocks get their own material so each can darken as it takes hits. A plate has
        // to be unmistakable: it is the one obstacle nothing answers, so a player who meets one while
        // unstoppable has to read it as a rule rather than a bug.
        // A target that opens something, and the thing it opens, are drawn in their pair's colour.
        // Note a target can be in a group without being a key - ordered groups use groups too - so
        // this asks whether anything is actually locked by it.
        var material = o.Kind switch
        {
            ObstacleKind.Target when o.Group is not null && _keyGroups.ContainsKey(o.Group) => KeyMaterial(o.Group),
            ObstacleKind.Target => _targetMaterial,
            ObstacleKind.Plate => _hazardMaterial,
            _ when o.LockedBy is not null => DoorMaterial(o.LockedBy),
            _ => o.Period > 0f ? _gateMaterial : o.Hits > 0 ? Solid(_breakable) : _blockMaterial,
        };
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
        AddChild(node);
        return new View(node, center, forward, up, Spins: target, material)
        {
            Obstacle = o,
            Slides = o.Period > 0f,
        };
    }

    /// <summary>
    /// A block or plate that goes the whole way round its wall, drawn as a band following that wall
    /// rather than as a slab across it. Placed from the centreline with the section's own axes, like
    /// a blade, since it is a thing that goes around the section rather than one standing on a spot.
    /// </summary>
    private View CreateCollarView(Obstacle o)
    {
        var material = o.Kind == ObstacleKind.Plate ? _hazardMaterial
            : o.LockedBy is not null ? DoorMaterial(o.LockedBy)
            : o.Period > 0f ? _gateMaterial
            : o.Hits > 0 ? Solid(_breakable)
            : _blockMaterial;
        var shape = _shapes.Get(_session.Track.SectionAt(o.S, o.Branch));
        var frame = _session.Track.FrameAt(o.S, o.Branch);
        var node = new MeshInstance3D
        {
            Mesh = BuildBand(shape, o.Surface, o.Length * 0.5f, o.Height),
            MaterialOverride = material,
        };
        AddChild(node);
        return new View(node, frame.Position, frame.Forward.ToGodot(), frame.Up.ToGodot(), Spins: false, material)
        {
            Obstacle = o,
        };
    }

    /// <summary>
    /// A band round one wall of a section: from the wall out to <paramref name="depth"/> off it,
    /// <paramref name="halfLength"/> either way along the track. Two annular faces and the edge
    /// between them, which is the face the ship meets. In a hollow tube the floor's perimeter is the
    /// whole tube, so a band on it rings the tube; on a ring, each wall rings itself.
    /// </summary>
    private static Mesh BuildBand(ProfileShape shape, Surface surface, float halfLength, float depth)
    {
        const int segments = 64;
        float around = shape.PerimeterOf(surface);

        var wall = new Vector3[segments + 1];
        var edge = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            var (on, x) = shape.Wrap(surface, around * i / segments);
            var point = shape.PointAt(on, x);
            var normal = shape.NormalAt(on, x);
            wall[i] = new Vector3(point.X, point.Y, 0f);
            edge[i] = new Vector3(point.X + normal.X * depth, point.Y + normal.Y * depth, 0f);
        }

        // Which way the faces wind depends on which way the wall's normal points relative to the
        // way round it: into the section from a tube's wall, out of the core from a ring's. Wound
        // the same for both, the core's band shows only its unlit backs - a dim hairline round the
        // core from any distance, and a collar nobody sees until it costs them.
        var run = wall[1] - wall[0];
        var rise = edge[0] - wall[0];
        bool inverted = run.X * rise.Y - run.Y * rise.X < 0f;

        var front = new Vector3(0f, 0f, -halfLength);
        var back = new Vector3(0f, 0f, halfLength);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if (inverted) (b, d) = (d, b);
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
            st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
        }
        for (int i = 0; i < segments; i++)
        {
            Quad(wall[i] + front, edge[i] + front, edge[i + 1] + front, wall[i + 1] + front);
            Quad(wall[i] + back, edge[i] + back, edge[i + 1] + back, wall[i + 1] + back);
            Quad(edge[i] + front, edge[i + 1] + front, edge[i + 1] + back, edge[i] + back);
        }
        st.GenerateNormals();
        return st.Commit();
    }

    /// <summary>
    /// One blade of an aperture. Unlike everything else on a wall a blade is drawn from the middle
    /// of the tube outwards: it is a wedge reaching from the wall almost to the centre, and the ring
    /// of them seals the tube. So the node sits on the centreline with the section's own axes, and
    /// the shape itself is rebuilt as the blade swings - which is also what lets one aperture hug an
    /// oval or a boxy section as readily as a round one.
    /// </summary>
    private View CreateBladeView(Obstacle o)
    {
        var frame = _session.Track.FrameAt(o.S, o.Branch);
        var material = BladeMaterial(o.LockedBy!);
        var node = new MeshInstance3D { Mesh = BuildBlade(o, open: 0f), MaterialOverride = material };
        AddChild(node);
        return new View(node, frame.Position, frame.Forward.ToGodot(), frame.Up.ToGodot(), Spins: false, material)
        {
            Obstacle = o,
        };
    }

    /// <summary>
    /// Keeps one blade's view in step with its key. A blade does not blink out when its group falls:
    /// it turns round its own sector and opens outwards over <see cref="IrisTime"/>, so the ring
    /// reads as machinery working rather than as blocks being deleted, and the player can see which
    /// way round is now clear while they are still far enough back to steer for it.
    /// </summary>
    private void UpdateBlade(Obstacle o, double s, Vector3d origin)
    {
        bool open = _session.IsUnlocked(o);
        _views.TryGetValue(o, out var view);
        if (open && view is not null) view.OpenedAt ??= _time;

        float progress = view?.OpenedAt is float began ? (_time - began) / IrisTime : 0f;
        // A blade whose key fell before the ring came into view has no swing to show and is simply
        // never made; one already swinging keeps its view until it has finished getting out of the way.
        bool gone = o.Destroyed || (open && (view is null || progress >= 1f));
        bool there = !gone && InView(o.S, s);
        // A blade that is smashed is not deleted, it is knocked off: it keeps its shape and spins
        // away ahead of the ship, the way a leaf torn out of a shutter would.
        if (o.Destroyed && view is not null && _views.Remove(o)) Fling(o, view);
        Sync(_views, o, there, burst: o.Destroyed, _createObstacle, origin);

        if (there && _views.TryGetValue(o, out view) && view.OpenedAt is not null)
        {
            // Eased, so the blade leans into the swing and settles out of it rather than jerking
            // away at a constant rate - the difference between machinery and a slide.
            ((MeshInstance3D)view.Node).Mesh = BuildBlade(o, Mathf.SmoothStep(0f, 1f, Mathf.Clamp(progress, 0f, 1f)));
        }
    }

    /// <summary>
    /// The blade as a wedge in the section's own plane, extruded along the track. <paramref name="open"/>
    /// runs 0 (shut, reaching almost to the middle) to 1 (gone, folded back into the wall), and turns
    /// the blade round the tube as it goes, which is what makes the ring read as an iris rather than
    /// as a set of shutters.
    /// </summary>
    private Mesh BuildBlade(Obstacle o, float open)
    {
        const int steps = 10;
        var shape = _shapes.Get(_session.Track.SectionAt(o.S, o.Branch));
        float center = shape.Loop(o.Surface, o.X) + open * BladeSwirl * o.Width;
        float half = o.Width * 0.5f * BladeOverlap;
        float inner = IrisShut + (1f - IrisShut) * open;
        float z = o.Length * 0.5f;

        var outer = new Vector3[steps + 1];
        var hole = new Vector3[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            var (surface, x) = shape.Wrap(Surface.Floor, center + half * (2f * i / steps - 1f));
            var p = shape.PointAt(surface, x);
            outer[i] = new Vector3(p.X, p.Y, 0f);
            hole[i] = new Vector3(p.X * inner, p.Y * inner, 0f);
        }

        // Blades are stacked a little along the track in the order they go round, so they overlap
        // the way real leaves do instead of meeting edge to edge in one plane.
        float lean = o.Blade * 0.06f;
        var front = new Vector3(0f, 0f, -z + lean);
        var back = new Vector3(0f, 0f, z + lean);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Shaded across its own width, bright at the leading edge and falling away to the trailing
        // one. A ring of blades all the same colour reads as one flat disc from any distance worth
        // reacting at; the gradient is what makes it a count of blades instead.
        Color Shade(int i) { float v = 0.95f - 0.42f * i / steps; return new Color(v, v, v); }
        void Vertex(Vector3 p, Color c)
        {
            st.SetColor(c);
            st.AddVertex(p);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color first, Color second)
        {
            Vertex(a, first); Vertex(b, first); Vertex(c, second);
            Vertex(a, first); Vertex(c, second); Vertex(d, second);
        }
        for (int i = 0; i < steps; i++)
        {
            Color near = Shade(i), far = Shade(i + 1);
            Quad(hole[i] + front, outer[i] + front, outer[i + 1] + front, hole[i + 1] + front, near, far);
            Quad(hole[i] + back, outer[i] + back, outer[i + 1] + back, hole[i + 1] + back, near, far);
            // The edge facing the hole: the only one the ship ever sees head on, and the one that
            // gives the blade thickness at the moment that matters. Lit brightest, so the shut ring
            // has a hard rim around the middle rather than fading into itself.
            Quad(hole[i] + front, hole[i + 1] + front, hole[i + 1] + back, hole[i] + back,
                new Color(1f, 1f, 1f), new Color(1f, 1f, 1f));
        }
        st.GenerateNormals();
        return st.Commit();
    }

    /// <summary>
    /// The lip an aperture is set into: a shallow ring left in the wall whether the blades are shut,
    /// part open or long gone. Same reasoning as a gate's socket - where the machinery lives should
    /// be readable on the approach, and a ring that has already been opened should still say so.
    /// </summary>
    private View CreateApertureLipView(Obstacle o)
    {
        const int segments = 64;
        const float depth = 0.5f;
        var shape = _shapes.Get(_session.Track.SectionAt(o.S, o.Branch));
        float z = o.Length * 0.5f + 0.08f;

        var wall = new Vector3[segments + 1];
        var edge = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            var (surface, x) = shape.Wrap(Surface.Floor, shape.Perimeter * i / segments);
            var point = shape.PointAt(surface, x);
            var normal = shape.NormalAt(surface, x);
            wall[i] = new Vector3(point.X, point.Y, 0f);
            edge[i] = new Vector3(point.X + normal.X * depth, point.Y + normal.Y * depth, 0f);
        }

        var front = new Vector3(0f, 0f, -z);
        var back = new Vector3(0f, 0f, z);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
            st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
        }
        for (int i = 0; i < segments; i++)
        {
            Quad(wall[i] + front, edge[i] + front, edge[i + 1] + front, wall[i + 1] + front);
            Quad(wall[i] + back, edge[i] + back, edge[i + 1] + back, wall[i + 1] + back);
            Quad(edge[i] + front, edge[i + 1] + front, edge[i + 1] + back, edge[i] + back);
        }
        st.GenerateNormals();

        var frame = _session.Track.FrameAt(o.S, o.Branch);
        var node = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = _socketMaterial };
        AddChild(node);
        return new View(node, frame.Position, frame.Forward.ToGodot(), frame.Up.ToGodot(), Spins: false, _socketMaterial);
    }

    // A blade wears its pair's colour like a door does, but lit a little further up: a door is one
    // slab to notice, while an aperture is a ring the player has to count blades on.
    private StandardMaterial3D BladeMaterial(string group)
    {
        if (_bladeMaterials.TryGetValue(group, out var material)) return material;
        material = Glowing(PairColor(group).Darkened(0.3f), 0.35f + _themeGlow);
        material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        material.VertexColorUseAsAlbedo = true;
        _bladeMaterials[group] = material;
        return material;
    }

    /// <summary>
    /// Switches a locked pad on or off. While its keys stand the pad is dead and its label wears the
    /// key's colour, so what the player is looking at is "the thing k2 turns on" rather than a
    /// power-up that inexplicably did nothing when they flew over it.
    /// </summary>
    private void ShowPadPower(Pickup p, View view)
    {
        bool live = _session.IsLive(p);
        if (view.LiveShown == live) return;
        view.LiveShown = live;

        var (color, _) = PickupLooks[p.Kind];
        ((MeshInstance3D)view.Node).MaterialOverride = live ? _pickupMaterials[p.Kind] : _deadPadMaterial;
        if (view.Node.GetChildCount() > 0 && view.Node.GetChild(0) is Label3D label)
        {
            label.Modulate = live ? color : PairColor(p.LockedBy!).Darkened(0.3f);
        }
    }

    // Placeholder power-up: a glowing pad set into the surface with a floating label.
    private View CreatePickupView(Pickup p)
    {
        var (center, forward, up) = Pose(p.S, p.Branch, p.Surface, p.X, 0.06f);
        var (color, text) = PickupLooks[p.Kind];
        var material = _pickupMaterials[p.Kind];
        var pad = new MeshInstance3D { Mesh = _padMesh, MaterialOverride = material };
        pad.AddChild(new Label3D
        {
            Text = text,
            Position = new Vector3(0f, 1.4f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 72,
            PixelSize = 0.012f,
            OutlineSize = 16,
            Modulate = color,
        });
        AddChild(pad);
        return new View(pad, center, forward, up, Spins: true, material);
    }

    // A wireframe picture of a well, for the warning signs: rings down to a throat, plus meridians.
    private static Mesh BuildWellIcon()
    {
        const int rings = 4, segments = 16;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Lines);
        Vector3 At(int i, int a)
        {
            float t = i / (float)rings;
            float r = 0.18f + 0.82f * MathF.Cos(t * MathF.PI / 2f);
            float angle = 2f * MathF.PI * a / segments;
            return new Vector3(r * MathF.Cos(angle), -0.9f * MathF.Pow(t, 1.6f), r * MathF.Sin(angle));
        }
        for (int i = 0; i <= rings; i++)
        {
            for (int a = 0; a < segments; a++)
            {
                st.AddVertex(At(i, a));
                st.AddVertex(At(i, a + 1));
            }
        }
        for (int a = 0; a < segments; a += 2)
        {
            for (int i = 0; i < rings; i++)
            {
                st.AddVertex(At(i, a));
                st.AddVertex(At(i + 1, a));
            }
        }
        return st.Commit();
    }

    /// <summary>
    /// The funnel of a warp mouth, as rings of a surface of revolution squashed to the opening's
    /// shape. Place points +Y along the surface normal, so it hangs below the wall. The radius
    /// leaves the wall tangentially, which is what makes the lip a round-over rather than an edge,
    /// and the depth starts flat and accelerates, which is what makes it read as a well rather than
    /// a cone. The throat is left open, so it falls away into black.
    /// </summary>
    /// <param name="throat">Radius it narrows to, as a fraction of the mouth; 0 closes it to a point.</param>
    private static Mesh BuildFunnel(float halfWidth, float halfLength, float throat, float depthScale)
    {
        const int rings = 16;
        const int segments = 32;
        float depth = depthScale * MathF.Max(halfWidth, halfLength);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i <= rings; i++)
        {
            float t = i / (float)rings;
            float r = throat + (1f - throat) * MathF.Cos(t * MathF.PI / 2f);
            float d = depth * MathF.Pow(t, 1.6f);
            for (int a = 0; a <= segments; a++)
            {
                float angle = 2f * MathF.PI * a / segments;
                st.SetUV(new Vector2(a / (float)segments, t));
                st.AddVertex(new Vector3(r * halfWidth * MathF.Cos(angle), -d, r * halfLength * MathF.Sin(angle)));
            }
        }
        for (int i = 0; i < rings; i++)
        {
            for (int a = 0; a < segments; a++)
            {
                int p = i * (segments + 1) + a;
                st.AddIndex(p);
                st.AddIndex(p + segments + 1);
                st.AddIndex(p + 1);
                st.AddIndex(p + 1);
                st.AddIndex(p + segments + 1);
                st.AddIndex(p + segments + 2);
            }
        }
        st.GenerateNormals();
        return st.Commit();
    }

    // Dust drawn down into a well: spawned in a shell around the mouth, pulled inwards and given a
    // twist on the way, so it spirals in rather than falling straight. Local coordinates, or the
    // floating origin would leave the particles behind as the world shifts under them each frame.
    private View CreateWarpDustView(Warp w)
    {
        var (center, forward, up) = Pose(w.S, w.Branch, w.Surface, w.X, 0.5f);
        float width = w.WidthOn(_shapes.Get(_session.Track.SectionAt(w.S, w.Branch)));
        var particles = new CpuParticles3D
        {
            Amount = 70,
            Lifetime = 1.25f,
            LocalCoords = true,
            Mesh = _burstMesh,
            MaterialOverride = _dustMaterial,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = width * 0.85f,
            Spread = 0f,
            InitialVelocityMin = 0f,
            InitialVelocityMax = 2f,
            // Local +Y is the surface normal, so down it is into the wall.
            Gravity = new Vector3(0f, -16f, 0f),
            RadialAccelMin = -9f,
            RadialAccelMax = -4f,
            TangentialAccelMin = 7f,
            TangentialAccelMax = 13f,
            DampingMin = 0.2f,
            DampingMax = 0.6f,
            ScaleAmountMin = 0.12f,
            ScaleAmountMax = 0.36f,
        };
        AddChild(particles);
        particles.Emitting = true;
        return new View(particles, center, forward, up, Spins: false, _dustMaterial);
    }

    // One warning plate from the ring standing around the tube ahead of a well. A ring warns whatever
    // way round the player is flying, and sits clear of the well's own line so it hides nothing.
    private View CreateSignView((Warp Warp, int Index) sign)
    {
        var w = sign.Warp;
        double s = w.S - SignRing;
        var shape = _shapes.Get(_session.Track.SectionAt(s, w.Branch));
        var (surface, x) = shape.IsClosed
            ? shape.Wrap(Surface.Floor, shape.Perimeter * sign.Index / SignsInRing)
            : (w.Surface, w.X + (sign.Index - (SignsInRing - 1) / 2f) * 9f);

        var (center, forward, up) = Pose(s, w.Branch, surface, x, 1.9f);
        // A sign is built to look like signage rather than like something to dodge: a plate held off
        // the wall on a post, with a dark border, standing clear of the surface. Blocks sit flat on
        // the wall and are solid - the post and the gap under it are most of what tells them apart.
        var root = new Node3D();

        // Place points local +Y along the surface normal, so the post drops straight back to the wall.
        root.AddChild(new MeshInstance3D
        {
            Mesh = _signPost,
            MaterialOverride = _iconMaterial,
            Position = new Vector3(0f, -0.95f, 0f),
        });

        // A cylinder's axis is Y, so tipping each disc a quarter turn stands it up facing back at
        // the oncoming ship, point upwards.
        root.AddChild(new MeshInstance3D
        {
            Mesh = _signBorder,
            MaterialOverride = _iconMaterial,
            RotationDegrees = new Vector3(90f, 0f, 0f),
        });
        var plate = new MeshInstance3D
        {
            Mesh = _signMesh,
            MaterialOverride = _plateMaterial,
            RotationDegrees = new Vector3(90f, 0f, 0f),
        };
        // The disc stands proud of both faces. Local Y is the plate's thickness once the quarter turn
        // is applied, and the plate is thin, so a disc long enough to pass right through it shows
        // whichever side the player sees - no guessing which face ends up pointing back.
        plate.AddChild(new MeshInstance3D
        {
            Mesh = _wellIcon,
            MaterialOverride = _iconMaterial,
            Scale = new Vector3(1f, 5f, 1f),
        });
        root.AddChild(plate);

        AddChild(root);
        return new View(root, center, forward, up, Spins: false, _plateMaterial);
    }

    // In an ordered group, the one whose turn it is burns at full while the rest sit dim and wait.
    // The useful thing to know is not which group a target belongs to but whether shooting it now
    // will do anything - and unlike a fixed marker, this answers that again after every shot.
    private void ShowTurn(Obstacle o, View view)
    {
        bool ready = _session.CanBreak(o);
        if (view.ReadyShown == ready) return;
        view.ReadyShown = ready;
        if (view.Node is MeshInstance3D mesh) mesh.MaterialOverride = TurnMaterial(o, ready);
    }

    private StandardMaterial3D TurnMaterial(Obstacle o, bool ready)
    {
        string group = o.Group!;
        bool key = _keyGroups.ContainsKey(group);
        // A key keeps its pair colour while it waits, so the two signals stack rather than fight:
        // the colour says which door it opens, the brightness says whether it is next.
        if (ready) return key ? KeyMaterial(group) : _targetMaterial;

        string cacheKey = key ? group : "";
        if (_waitingMaterials.TryGetValue(cacheKey, out var material)) return material;
        var color = key ? PairColor(group) : _targetColor;
        material = Glowing(color.Darkened(0.55f), 0.5f + _themeGlow);
        _waitingMaterials[cacheKey] = material;
        return material;
    }

    // Breakable blocks darken with each hit.
    private void ShowDamage(Obstacle o, View view)
    {
        view.HitsShown = o.HitsTaken;
        ((StandardMaterial3D)view.BurstMaterial).AlbedoColor = _breakable.Darkened(0.6f * o.HitsTaken / o.Hits);
    }

    private void Place(View view, Vector3d origin)
    {
        // A mover's pose is recomputed every frame: its center is captured once at creation, so
        // without this its collision box slides around the tube while the thing you can see stays put.
        var center = view.Obstacle is { Sweep: not 0f } o
            ? Pose(o.S, o.Branch, o.Surface, o.XAt(Now), o.Height / 2f).Center
            : view.Center;
        var pos = center.RelativeTo(origin).ToGodot();

        // A gate withdraws into its socket rather than blinking out, so the cycle can be watched
        // rather than counted. The socket itself never moves.
        if (view.Slides && view.Obstacle is { } gate)
        {
            pos -= view.Up * (1f - gate.ExtensionAt(Now)) * (gate.Height + 0.4f);
        }

        // A collar that is a gate cannot withdraw sideways into a socket: it is a band round the
        // whole wall. So it rises out of the wall and sinks back into it, rebuilt at its present
        // height whenever that has changed enough to see - a few dozen vertices, and only while
        // it is on the move.
        if (view.Obstacle is { Full: true, Period: > 0f } collar && view.Node is MeshInstance3D band)
        {
            float extension = collar.ExtensionAt(Now);
            if (Mathf.Abs(extension - view.ShownExtension) > 0.02f)
            {
                view.ShownExtension = extension;
                var shape = _shapes.Get(_session.Track.SectionAt(collar.S, collar.Branch));
                band.Mesh = BuildBand(shape, collar.Surface, collar.Length * 0.5f, Mathf.Max(0.05f, collar.Height * extension));
            }
        }

        view.Node.LookAtFromPosition(pos, pos + view.Forward, view.Up);
        if (view.Spins) view.Node.RotateObjectLocal(Vector3.Up, _time * 2.5f);
    }

    // Colours cycle in the order the pairs are met, so consecutive pairs always differ and a key and
    // its door always match. With more simultaneous pairs than colours they would start repeating,
    // which the format guide warns about rather than the renderer trying to be clever.
    private Color PairColor(string group) =>
        KeyColors[(_keyGroups.TryGetValue(group, out int index) ? index : 0) % KeyColors.Length];

    private StandardMaterial3D KeyMaterial(string group)
    {
        if (_keyMaterials.TryGetValue(group, out var material)) return material;
        material = Glowing(PairColor(group), 2.1f + _themeGlow);
        _keyMaterials[group] = material;
        return material;
    }

    // The door sits in the same hue but dark and barely lit, so it reads as the shut version of the
    // bright thing that opens it.
    private StandardMaterial3D DoorMaterial(string group)
    {
        if (_doorMaterials.TryGetValue(group, out var material)) return material;
        material = Glowing(PairColor(group).Darkened(0.55f), 0.55f + _themeGlow);
        _doorMaterials[group] = material;
        return material;
    }

    // A line across the plane where the jump window opens or shuts. Wide enough to span the whole
    // strafe range, so it cannot be flown around and missed, and flush to the surface so it reads as
    // a marking rather than as something to dodge.
    private View CreateJumpMarkView((double S, bool Opens, Surface Surface) mark)
    {
        var (center, forward, up) = Pose(mark.S, -1, mark.Surface, 0f, 0.07f);
        var node = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(96f, 0.2f, 4f) },
            MaterialOverride = mark.Opens ? _jumpOpenMaterial : _jumpCloseMaterial,
        };
        AddChild(node);
        return new View(node, center, forward, up, Spins: false,
            mark.Opens ? _jumpOpenMaterial : _jumpCloseMaterial);
    }

    // The mark a gate leaves on the wall: flush, dark, and a little wider than the gate itself, so
    // the spot reads from a distance whether or not anything is standing in it.
    private View CreateSocketView(Obstacle o)
    {
        var (center, forward, up) = Pose(o.S, o.Branch, o.Surface, o.XAt(Now), 0.05f);
        var node = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(o.Width * 1.12f, 0.1f, o.Length * 1.8f) },
            MaterialOverride = _socketMaterial,
        };
        AddChild(node);
        return new View(node, center, forward, up, Spins: false, _socketMaterial) { Obstacle = o };
    }

    private void UpdateShots(Vector3d origin)
    {
        var shots = _session.Shots;
        Grow(_shotViews, shots.Count, _shotMesh, _shotMaterial);
        for (int i = 0; i < _shotViews.Count; i++)
        {
            var node = _shotViews[i];
            node.Visible = i < shots.Count;
            if (!node.Visible) continue;

            var p = shots[i].Position;
            var (center, forward, up) = Pose(p.S, p.Branch, p.Surface, p.X, RideHeight + shots[i].Height);
            var pos = center.RelativeTo(origin).ToGodot();
            node.LookAtFromPosition(pos, pos + forward, up);
            // The capsule's long axis is Y; lay it along the track.
            node.RotateObjectLocal(Vector3.Right, Mathf.Pi / 2f);
        }
    }

    // Placeholder ring shot: a glowing ring standing across the track, sized to the section.
    private void UpdateRings(Vector3d origin)
    {
        var rings = _session.Rings;
        Grow(_ringViews, rings.Count, _ringMesh, _ringMaterial);
        for (int i = 0; i < _ringViews.Count; i++)
        {
            var node = _ringViews[i];
            node.Visible = i < rings.Count;
            if (!node.Visible) continue;

            var p = rings[i].Position;
            var frame = _session.Track.FrameAt(p.S, p.Branch);
            var section = _session.Track.SectionAt(p.S, p.Branch);
            float halfWidth = section.IsClosed ? section.HalfWidth : _session.Settings.RingReach;
            // The torus lies in its XZ plane: map X to the track's right and Z to its up.
            var basis = new Basis(frame.Right.ToGodot() * halfWidth, frame.Forward.ToGodot(), frame.Up.ToGodot() * section.HalfHeight);
            node.Transform = new Transform3D(basis, frame.Position.RelativeTo(origin).ToGodot());
        }
    }

    private void Grow(List<MeshInstance3D> pool, int count, Mesh mesh, Material material)
    {
        while (pool.Count < count)
        {
            var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
            AddChild(node);
            pool.Add(node);
        }
    }

    /// <summary>
    /// Sends a smashed blade spinning off. It goes with the ship - a little faster, so it pulls
    /// ahead and can be seen going - and out towards the wall it stood on, tumbling, and shrinks
    /// away over the last of its flight rather than blinking out.
    /// </summary>
    private void Fling(Obstacle o, View view)
    {
        var shape = _shapes.Get(_session.Track.SectionAt(o.S, o.Branch));
        var frame = _session.Track.FrameAt(o.S, o.Branch);
        var onWall = shape.PointAt(o.Surface, o.X);
        var outward = frame.DirectionOnSection(System.Numerics.Vector2.Normalize(onWall)).ToGodot();
        var forward = view.Forward;
        float speed = _session.Ship.ForwardSpeed;
        var velocity = forward * (speed * 1.1f + 5f) + outward * 7f;

        // The blade's mesh is built about the tube's axis, since that is what it turns round as an
        // iris; a leaf knocked loose has to tumble about its own middle, so the mesh is hung under
        // a pivot there. Its middle is a little over halfway from the axis to the wall.
        var mesh = view.Node;
        var middle = onWall * (0.5f * (1f + IrisShut));
        var pivot = new Node3D();
        AddChild(pivot);
        pivot.GlobalTransform = mesh.GlobalTransform;
        mesh.Reparent(pivot, keepGlobalTransform: false);
        mesh.Position = new Vector3(-middle.X, -middle.Y, 0f);
        var centre = view.Center + frame.DirectionOnSection(middle);

        // Tumbling about an axis across the blade, so it is seen edge-on and face-on by turns.
        var axis = (outward.Cross(forward) + outward * 0.35f).Normalized();
        _flung.Add(new Flung(pivot, axis, 9f + o.Blade * 1.7f, _time + FlungSeconds)
        {
            Center = centre,
            Velocity = velocity,
        });
        SpawnBurst(centre, view.BurstMaterial);
    }

    private const float FlungSeconds = 0.9f;

    private void UpdateFlung(Vector3d origin, float dt)
    {
        for (int i = _flung.Count - 1; i >= 0; i--)
        {
            var flung = _flung[i];
            float left = flung.Expires - _time;
            if (left <= 0f)
            {
                flung.Node.QueueFree();
                _flung.RemoveAt(i);
                continue;
            }
            var step = flung.Velocity * dt;
            flung.Center += new Vector3d(step.X, step.Y, step.Z);
            flung.Node.Position = flung.Center.RelativeTo(origin).ToGodot();
            flung.Node.RotateObjectLocal(axis: flung.Node.GlobalBasis.Inverse() * flung.Axis, angle: flung.Rate * dt);
            // Shrinking the whole way, quickest at the end: a leaf spinning off is a thing that
            // gets smaller, not a thing that hangs in the air and then goes.
            float shrink = left / FlungSeconds;
            flung.Node.Scale = Vector3.One * Mathf.Max(0.02f, 0.25f + 0.75f * shrink * shrink);
        }
    }

    private void SpawnBurst(Vector3d center, Material material)
    {
        var particles = new CpuParticles3D
        {
            OneShot = true,
            Explosiveness = 1f,
            Amount = 32,
            Lifetime = BurstSeconds * 0.75f,
            LocalCoords = true,
            Mesh = _burstMesh,
            MaterialOverride = material,
            Spread = 180f,
            InitialVelocityMin = 6f,
            InitialVelocityMax = 14f,
            Gravity = Vector3.Zero,
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.2f,
        };
        AddChild(particles);
        particles.Emitting = true;
        _bursts.Add(new Burst(particles, center, _time + BurstSeconds));
    }

    private void UpdateBursts(Vector3d origin)
    {
        UpdateFlung(origin, _lastDt);
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            var burst = _bursts[i];
            if (_time > burst.Expires)
            {
                burst.Node.QueueFree();
                _bursts.RemoveAt(i);
                continue;
            }
            burst.Node.Position = burst.Center.RelativeTo(origin).ToGodot();
        }
    }

    // World center, forward, and up for something `height` off a track surface.
    private (Vector3d Center, Vector3 Forward, Vector3 Up) Pose(double s, int branch, Surface surface, float x, float height)
    {
        var frame = _session.Track.FrameAt(s, branch);
        var shape = _shapes.Get(_session.Track.SectionAt(s, branch));
        var normal = shape.NormalAt(surface, x);
        var point = shape.PointAt(surface, x) + normal * height;
        return (frame.PointOnSection(point), frame.Forward.ToGodot(), frame.DirectionOnSection(normal).ToGodot());
    }

    private StandardMaterial3D Solid(Color color) => Faded(new()
    {
        AlbedoColor = color,
        Roughness = 0.5f,
        EmissionEnabled = _glow > 0f,
        Emission = color,
        EmissionEnergyMultiplier = 0.5f * _glow,
    });

    private StandardMaterial3D Glowing(Color color, float energy) => Faded(new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
    });

    // Dithered away with distance in the see-through style; left alone in a solid level. Dither
    // rather than alpha, so nothing has to be sorted and the glow keeps working on what is left.
    // Godot's distance fade hides what is nearer than the minimum and shows what is past the
    // maximum - it is made for hiding things the camera is inside - and fades the other way round
    // when the minimum is the larger, which is the way round wanted here.
    private StandardMaterial3D Faded(StandardMaterial3D material)
    {
        if (_wireFade is not { } fade) return material;
        material.DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelDither;
        material.DistanceFadeMinDistance = fade.To;
        material.DistanceFadeMaxDistance = fade.From;
        return material;
    }

    // A smashed blade in flight.
    private sealed record Flung(Node3D Node, Vector3 Axis, float Rate, float Expires)
    {
        public Vector3d Center { get; set; }
        public Vector3 Velocity { get; set; }
    }

    private sealed record View(Node3D Node, Vector3d Center, Vector3 Forward, Vector3 Up, bool Spins, Material BurstMaterial)
    {
        public int HitsShown { get; set; }

        /// <summary>Whether this was last drawn as ready to break; null before it has been decided.</summary>
        public bool? ReadyShown { get; set; }

        /// <summary>The obstacle this shows, when it is one that moves; null for anything fixed.</summary>
        public Obstacle? Obstacle { get; init; }

        /// <summary>Whether this withdraws into the wall on a gate's cycle. Sockets never do.</summary>
        public bool Slides { get; init; }

        /// <summary>For a collar that is a gate: how far out its band was last built, from 0 to 1.</summary>
        public float ShownExtension { get; set; } = 1f;

        /// <summary>When an aperture blade's key fell, so its swing can be timed; null while it is shut.</summary>
        public float? OpenedAt { get; set; }

        /// <summary>Whether a pad was last drawn switched on; null before it has been decided.</summary>
        public bool? LiveShown { get; set; }
    }

    private sealed record Burst(CpuParticles3D Node, Vector3d Center, float Expires);
}

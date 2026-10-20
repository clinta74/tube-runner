using System;
using System.Collections.Generic;
using Godot;
using TubeRunner.Core;
using Theme = TubeRunner.Core.Theme;

namespace TubeRunner.Game;

/// <summary>
/// Streams one track's mesh chunks. Each chunk is one wall-pattern segment long and holds a floor
/// and a ceiling strip per tube: one tube on the main track, one per branch inside a split. Where a
/// split forks and merges, a cap wall with openings for the branches closes off the chamber.
/// Everything is re-placed relative to a floating origin every frame, so coordinates near the
/// camera stay small.
///
/// A view has its own copy of the wall material, with its level's theme on it, so two views can be
/// on screen at once in different colours: <see cref="TrackRenderer"/> keeps one for the level being
/// flown and one for the level after it.
/// </summary>
public partial class TrackView : Node3D
{
    private const int RingsPerChunk = 40;
    // How far into a well the wall keeps going before the throat is left open. Lower means a wider
    // hole at the bottom: it costs nothing in play, since a well's reach is set by its width and
    // length, and the hole is most of what makes one read as a hole.
    private const float ThroatFraction = 0.62f;
    private const int SurfaceSegments = 48;
    private const int CapSegments = 96;
    // Wing vertices as fractions of the wing length; they collapse onto the edge when there's no wing.
    private static readonly float[] WingSteps = { 0.02f, 0.1f, 0.35f, 1f };
    private static readonly int StripVertices = SurfaceSegments + 1 + 2 * WingSteps.Length;

    // A ring's wall closes on itself, so one strip covers the whole way round instead of half of it.
    // Giving it the count a half-tube strip uses would draw it at half the angular resolution, and a
    // wide bore drawn coarsely does not look coarse - it aliases, because the checker is interpolated
    // across facets far bigger than the cells on them.
    private static readonly int RingStripVertices = 2 * SurfaceSegments + 1;

    // Fine enough that no piece is stepped over; a core's ends sit on piece boundaries.
    private const double CoreScanStep = 1.0;

    // How far inside a span its end rings read their section from; see AddTube.
    private const double SectionInset = 0.001;

    // How far inside a core's last piece its closing cap is built, so the section it is built from
    // is one that still has the core.
    private const double CoreCapInset = 0.01;
    private static readonly Surface[] Surfaces = { Surface.Floor, Surface.Ceiling };

    // A fork or merge is a funnel rather than a wall with holes: the chamber's cross-section, sunk
    // towards each branch opening so the wall bends into the branch tube. This is how deep the
    // throats go, at most - shallower on a short split, so both ends fit - and how finely the
    // surface is meshed: strips round each opening, and steps along each strip from rim to wall.
    private const float FunnelDepthCap = 12f;
    private const int FunnelColumns = 128;
    private const int FunnelSteps = 10;
    private bool _funnels;

    private readonly Dictionary<long, Placed> _chunks = new();
    private readonly Dictionary<int, Placed> _caps = new();
    private readonly List<CapSpec> _capSpecs = new();
    private readonly List<long> _staleChunks = new();
    private readonly List<int> _staleCaps = new();
    private readonly ProfileShapeCache _shapes = new();
    private readonly float[] _xs = new float[Math.Max(StripVertices, RingStripVertices)];

    // Where a core starts and where it stops. Spans are split here, the way they are split at forks,
    // so a span is drawn either entirely as a ring or entirely as a tube: a strip that has to carry
    // a core across rings that do not have one puts those rings on the centreline and stitches them
    // to the first ring that does - a cone from the axis, which from the front is a disc across the
    // bore. Each end also gets a flat cap, streamed like a fork wall and drawn in the same material,
    // because an end face that wears the wall's own checker reads as more wall.
    private readonly List<double> _coreBounds = new();
    private readonly List<WarpCut> _warpCuts = new();
    private Track _track = null!;
    private ShaderMaterial _material = null!;
    private double _drawTo;
    private ShaderMaterial _capMaterial = null!;
    private Vector3 _capColor;
    private Vector3 _endWallColor;
    private Vector3 _endRimColor;
    private Vector3 _coreFaceColor;
    private float _chunkLength;

    [Export] public float ViewBehind { get; set; } = 30f;

    /// <summary>How far ahead chunks are built in the solid style, where the fade hides the rest.</summary>
    [Export] public float ViewAhead { get; set; } = 450f;

    // How far ahead this level actually builds. A wireframe level sees much further than a solid one
    // - that is the point of it - and there is no sense fading lines out over a distance the mesh
    // never reaches, which is what happened: the tube simply stopped, well inside the fade.
    private float _viewAhead;

    /// <summary>Frees the level's meshes, ready for <see cref="Init"/> with another.</summary>
    public void Reset()
    {
        foreach (var placed in _chunks.Values) placed.Node.QueueFree();
        foreach (var placed in _caps.Values) placed.Node.QueueFree();
        _chunks.Clear();
        _caps.Clear();
        _capSpecs.Clear();
    }

    public Track Track => _track;

    /// <summary>Added to every chunk's segment index; see <see cref="SegmentJoin"/>.</summary>
    public int SegmentOffset
    {
        get => _segmentOffset;
        set
        {
            _segmentOffset = value;
            _material.SetShaderParameter("segment_offset", value);
        }
    }

    private int _segmentOffset;

    /// <summary>
    /// Where this view stops drawing the track. The whole of it, unless the next level is being
    /// drawn over its end: the end of a level is a copy of the next level's start, and it is the
    /// next level's own version of that stretch, in its own colours, that is wanted there.
    /// </summary>
    public double DrawTo
    {
        get => _drawTo;
        set
        {
            _drawTo = value;
            // A chunk already built across the new limit was built full length; UpdateView frees
            // the ones wholly beyond it, but this one it would keep.
            long straddling = (long)Math.Floor(value / _chunkLength);
            if (_chunks.Remove(straddling, out var placed)) placed.Node.QueueFree();
        }
    }

    /// <param name="baseMaterial">The wall material; this view works from its own copy, with <paramref name="theme"/> applied.</param>
    /// <param name="warps">Warp mouths, whose openings are cut out of the wall as it is built.</param>
    /// <param name="closeTheEnd">
    /// Whether to wall off the end of the track. Only the last level's is: every other level runs
    /// straight on into the next, whose opening is drawn over its end, so a wall there would stand
    /// across a tube that carries on. Until the next level has been read there is no wall and no
    /// continuation, and the tube ends open - which is never in sight, since a level is thousands
    /// of units long and the reading takes a fraction of a second.
    /// </param>
    public void Init(Track track, ShaderMaterial baseMaterial, float chunkLength, Theme theme,
        IReadOnlyList<Warp>? warps, bool closeTheEnd)
    {
        _track = track;
        _material = (ShaderMaterial)baseMaterial.Duplicate();
        ThemeView.Apply(theme, _material);
        _material.SetShaderParameter("segment_length", chunkLength);
        _material.SetShaderParameter("segment_offset", 0);
        _segmentOffset = 0;
        _chunkLength = chunkLength;
        _drawTo = track.Length;
        // Build out to where the style stops showing anything: the solid fade reaches far_color at
        // FadeEnd, while the wire style only dims to a quarter by twice that.
        _viewAhead = theme.Wire ? Math.Max(ViewAhead, theme.FadeEnd * 2f) : ViewAhead;

        // Where a core begins and where it ends. A core does not blend in: it takes the whole of the
        // piece whose section carries it, so both ends fall on a piece boundary and each is closed
        // off with a flat cap. Found by walking rather than by reading the pieces, so a core that
        // spans several of them is one run with two ends rather than a cap at every join.
        _coreBounds.Clear();
        bool had = track.SectionAt(0).IsAnnulus;
        for (double s = CoreScanStep; s <= track.Length; s += CoreScanStep)
        {
            bool has = track.SectionAt(s).IsAnnulus;
            if (has != had) _coreBounds.Add(s);
            had = has;
        }

        // Each mouth is measured once into distance along the track and distance around the tube,
        // so the cut works the same on either surface and across the seam between them.
        _warpCuts.Clear();
        foreach (var w in warps ?? Array.Empty<Warp>())
        {
            var shape = _shapes.Get(track.SectionAt(w.S, w.Branch));
            // Roughly a hemisphere: as deep as the mouth is wide across, so it reads as a hole in a
            // surface. It used to bore three times its half-width, which was nine units when a mouth
            // was small and became nineteen once the mouth scaled with the tube - deeper than the
            // tube's own radius, so the player was looking down a cave at its lit far end.
            float halfWidth = w.WidthOn(shape) / 2f;
            _warpCuts.Add(new WarpCut(w.S, shape.Loop(w.Surface, w.X), shape.Perimeter,
                w.Length / 2f, halfWidth, 1.1f * halfWidth, w.Branch));
        }

        // A wireframe level has no caps, so its branch tubes run the whole split; a solid one has
        // funnels, and the branches start where the funnel's throat ends.
        _funnels = !theme.Wire;

        // Walls where each split forks and merges, plus one closing off the end of the track so a
        // finished level never looks out into the void.
        //
        // A wireframe level leaves them all out. A disc across the chamber would hide the branches
        // diverging behind it, and seeing that is most of why the style exists; and with the wall
        // already see-through there is nothing for a wall at the end of the track to protect.
        if (!theme.Wire)
        {
            foreach (var split in track.Splits)
            {
                _capSpecs.Add(new CapSpec(split.StartS, split, 0f));
                _capSpecs.Add(new CapSpec(split.EndS, split, split.Length));
            }
            if (closeTheEnd && track.SectionAt(track.Length).IsClosed) _capSpecs.Add(new CapSpec(track.Length, null, 0f));

            // A core's ends. The cap is built from the core's own wall, so it has to be built from a
            // section that has one: at a start the boundary itself does, since a piece carries its
            // core from its first unit, and at a stop the last hair of the piece before does.
            foreach (double bound in _coreBounds)
            {
                bool starts = track.SectionAt(bound).IsAnnulus;
                _capSpecs.Add(new CapSpec(starts ? bound : bound - CoreCapInset, null, 0f, Core: true));
            }
        }

        // Fork and merge walls sit in shadow around their openings. The wall closing the end of the
        // track is different: on the last level it is the thing you fly at, so it takes a color you
        // can see, but everywhere else the run carries straight on into another level and the end
        // should not read as an end at all. Painting it the distance color, rim included, leaves the
        // tube receding into the same haze everything far away fades into. Leaving it out entirely
        // was worse than the white wall it replaced - it opened onto empty space, so the level
        // finished in a black hole.
        // The end wall, when there is one, is the thing the run finishes at: a wall the player
        // can see, in the block colour with a lit rim.
        bool endsTheRun = closeTheEnd;
        _capColor = theme.SeamDark.ToVector3();
        // A fork wall sits in the shadow of its chamber and takes the seam's dark; the front of a
        // core stands in the open bore and would read as a hole in that. It takes a tone between the
        // wall's two cell colours instead - a lit flat end, neither a light cell nor a dark one.
        _coreFaceColor = theme.Darks[0].ToVector3().Lerp(theme.Lights[0].ToVector3(), 0.55f);
        _endWallColor = endsTheRun ? theme.Block.ToVector3() : theme.Far.ToVector3();
        _endRimColor = endsTheRun ? theme.SeamLight.ToVector3() : theme.Far.ToVector3();

        _capMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/track_cap.gdshader") };
        _capMaterial.SetShaderParameter("cap_color", _capColor);
        _capMaterial.SetShaderParameter("rim_color", theme.SeamLight.ToVector3());
        _capMaterial.SetShaderParameter("far_color", theme.Far.ToVector3());
        _capMaterial.SetShaderParameter("fade_start", theme.FadeStart);
        _capMaterial.SetShaderParameter("fade_end", theme.FadeEnd);
        _capMaterial.SetShaderParameter("glow", theme.Glow);
    }

    /// <summary>
    /// Builds chunks and caps around <paramref name="s"/>, frees ones out of view, and places them
    /// relative to <paramref name="origin"/>.
    /// </summary>
    public void UpdateView(double s, Vector3d origin)
    {
        double from = s - ViewBehind, to = s + _viewAhead;
        long lastInTrack = (long)Math.Ceiling(_drawTo / _chunkLength) - 1;
        long first = (long)Math.Floor(from / _chunkLength);
        long last = Math.Min((long)Math.Floor(to / _chunkLength), lastInTrack);

        _staleChunks.Clear();
        foreach (long k in _chunks.Keys)
        {
            if (k < first || k > last) _staleChunks.Add(k);
        }
        foreach (long k in _staleChunks)
        {
            _chunks[k].Node.QueueFree();
            _chunks.Remove(k);
        }
        for (long k = Math.Max(first, 0); k <= last; k++)
        {
            if (!_chunks.ContainsKey(k)) _chunks[k] = BuildChunk(k);
        }

        UpdateCaps(from, to);

        foreach (var placed in _chunks.Values) placed.Node.Position = placed.Origin.RelativeTo(origin).ToGodot();
        foreach (var placed in _caps.Values) placed.Node.Position = placed.Origin.RelativeTo(origin).ToGodot();
    }

    private Placed BuildChunk(long k)
    {
        double s0 = k * (double)_chunkLength;
        double s1 = Math.Min(s0 + _chunkLength, _drawTo);
        var origin = _track.FrameAt(s0).Position;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int start = 0;
        foreach (var (from, to, split, branch) in Spans(s0, s1))
        {
            start = AddTube(st, start, from, to, split, branch, s0, origin);
        }

        var node = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = _material };
        AddChild(node);
        node.SetInstanceShaderParameter("segment_index", (int)k);
        return new Placed(node, origin);
    }

    // Pieces of [s0, s1): the main track outside splits, and every branch inside them. The main
    // track is cut again wherever a core starts or stops, so no span straddles one (see _coreBounds).
    private IEnumerable<(double From, double To, TrackSplit? Split, int Branch)> Spans(double s0, double s1)
    {
        double s = s0;
        foreach (var split in _track.Splits)
        {
            if (split.EndS <= s || split.StartS >= s1) continue;
            foreach (var span in AtCoreBounds(s, split.StartS)) yield return span;

            // The branch tubes start where the fork's throats end and stop where the merge's begin;
            // between those the funnel is the wall.
            float throat = _funnels ? FunnelDepth(split) : 0f;
            double from = Math.Max(s, split.StartS + throat), to = Math.Min(split.EndS - throat, s1);
            if (from < to)
            {
                for (int b = 0; b < split.BranchCount; b++) yield return (from, to, split, b);
            }
            s = Math.Min(split.EndS, s1);
        }
        foreach (var span in AtCoreBounds(s, s1)) yield return span;
    }

    // [from, to) on the main track, cut at every core boundary that falls inside it.
    private IEnumerable<(double From, double To, TrackSplit? Split, int Branch)> AtCoreBounds(double from, double to)
    {
        double s = from;
        foreach (double bound in _coreBounds)
        {
            if (bound <= s || bound >= to) continue;
            yield return (s, bound, null, -1);
            s = bound;
        }
        if (s < to) yield return (s, to, null, -1);
    }

    // One tube (floor and ceiling strips) from `from` to `to`, on the main track or a split's branch.
    private int AddTube(SurfaceTool st, int start, double from, double to, TrackSplit? split, int branch, double s0, Vector3d origin)
    {
        int rings = Math.Max(2, (int)Math.Ceiling((to - from) / _chunkLength * RingsPerChunk));

        // Whether this span is drawn as a ring is decided once, for the whole span, rather than ring
        // by ring. The two layouts describe the same wall in different halves - a hollow tube splits
        // it between the floor and ceiling strips, a ring gives the whole of it to the floor and
        // hands the ceiling the core instead - so a strip that changed its mind part way along would
        // stitch the upper half of the wall to the core and sheet the bore across. Spans are cut at
        // every core boundary, so the middle of one says what the whole of it is.
        bool ring = SectionOf((from + to) * 0.5, split, branch).IsAnnulus;

        foreach (var surface in Surfaces)
        {
            for (int r = 0; r <= rings; r++)
            {
                double s = from + (to - from) * r / rings;
                // Use the split directly so the branch's last ring, at the merge, stays on the branch.
                var frame = split is null ? _track.FrameAt(s) : split.BranchFrame(_track, s, branch);
                // The section is read a hair inside the span. A span ends exactly where a core starts
                // or stops, and which side of that line the line itself falls on is the piece
                // lookup's business, not this strip's: a ring span's end ring read from the far side
                // has no core, collapses to the centreline, and is stitched to the ring beside it as
                // a cone - which from the front is a striped disc across the bore. The frame is not
                // moved; only the shape is, and by less than the wall's own vertex spacing.
                double inside = Math.Clamp(s, Math.Min(from + SectionInset, to), Math.Max(to - SectionInset, from));
                var shape = _shapes.Get(split is null ? _track.SectionAt(inside) : split.Section(branch));
                int across = FillStrip(shape, surface, ring);
                for (int i = 0; i < across; i++)
                {
                    float x = _xs[i];
                    // The wall itself is drawn down into any well here, so the tube extrudes into it
                    // as one surface: no separate funnel, and no seam to mismatch at the lip. How far
                    // in each point lies rides along in UV2, and the shader takes the inside to black
                    // - unlit wall alone gives the lip nothing to read against.
                    float loop = shape.Loop(surface, x);
                    var p = shape.PointAt(surface, x);
                    float well = 0f;
                    if (_warpCuts.Count > 0)
                    {
                        well = WellDepth(s, loop, branch, out float sink);
                        if (well > 0f) p -= shape.NormalAt(surface, x) * sink;
                    }
                    // A core blending in from nothing has a ring of no size at its tip, and dividing
                    // the way round by that is how the checker went to NaN and tore into a starburst.
                    float around = shape.PerimeterOf(surface);
                    st.SetUV(new Vector2(around > 0.01f ? 0.75f + loop / around : 0.75f, (float)(s - s0)));
                    st.SetUV2(new Vector2(well, 0f));
                    st.AddVertex(frame.PointOnSection(p).RelativeTo(origin).ToGodot());
                }
            }
            // Cut against the section in the middle of the span; it barely changes across a chunk.
            var midShape = _shapes.Get(split is null ? _track.SectionAt((from + to) * 0.5) : split.Section(branch));
            int stride = FillStrip(midShape, surface, ring);
            AddGridIndices(st, start, rings, stride, from, to, surface, branch, midShape);
            start += (rings + 1) * stride;
        }
        return start;
    }

    // How far into a warp well a point on the wall lies, from 0 at the rim to 1 at the centre.
    // Measured around the tube rather than across one strip, so a well can straddle the seam
    // between the floor and ceiling halves without tearing either of them.
    private float WellDepth(double s, float loop, int branch, out float sink)
    {
        float deepest = 0f;
        sink = 0f;
        foreach (var c in _warpCuts)
        {
            if (c.Branch != branch) continue;

            double along = s - c.S;
            if (Math.Abs(along) > c.HalfLength) continue;

            float around = MathF.Abs(loop - c.Loop);
            around = MathF.Min(around, c.Perimeter - around);   // the shorter way round
            if (around > c.HalfWidth) continue;

            double a = along / c.HalfLength, b = around / c.HalfWidth;
            double q = Math.Sqrt(a * a + b * b);
            if (q >= 1.0) continue;

            float into = 1f - (float)q;
            if (into <= deepest) continue;
            deepest = into;
            // Tangent to the wall at the rim, so the well blends in with no visible lip.
            sink = c.Depth * MathF.Pow(MathF.Cos((float)q * MathF.PI / 2f), 1.2f);
        }
        return deepest;
    }

    private void UpdateCaps(double from, double to)
    {
        _staleCaps.Clear();
        foreach (int i in _caps.Keys)
        {
            if (!InRange(_capSpecs[i].S, from, to)) _staleCaps.Add(i);
        }
        foreach (int i in _staleCaps)
        {
            _caps[i].Node.QueueFree();
            _caps.Remove(i);
        }
        for (int i = 0; i < _capSpecs.Count; i++)
        {
            if (!_caps.ContainsKey(i) && InRange(_capSpecs[i].S, from, to)) _caps[i] = BuildCap(_capSpecs[i]);
        }
    }

    private static float FunnelDepth(TrackSplit split) => Math.Min(FunnelDepthCap, split.Length * 0.25f);

    /// <summary>
    /// The wall where a split's branches begin or end, as a funnel: the chamber's cross-section,
    /// sunk along the track towards each branch opening until the surface meets the branch tube
    /// square on. It used to be a flat disc with the openings cut out by the shader, which is a
    /// wall with holes in it, and flying at a wall with holes is not flying into diverging tubes.
    ///
    /// Built opening by opening. Round each opening, strips run out from its rim to wherever that
    /// opening's share of the chamber ends: the chamber wall, or the line halfway to the next
    /// opening. Every point of the chamber that is not an opening belongs to the opening nearest
    /// it, so the strips tile the funnel between them. The rim end of each strip is the branch
    /// tube's own first ring, where it really is - drifted out along its offsets, tilted and turned
    /// with the bend by a throat's depth - so tube and funnel share an edge.
    ///
    /// It was a polar grid from the chamber's centre before, with vertices snapped onto the rims.
    /// The levels put openings right against the chamber wall - tangent at the sides, half a unit
    /// off the floor - and a radial grid has no triangles in a sliver that thin: they were dropped,
    /// and the void showed through in wedges and slivers either side of every throat. Strips from
    /// the rim cover a sliver of any width, and an opening that pokes past the chamber wall by a
    /// throat's depth just gets a skirt from the wall out to its ring.
    ///
    /// Each vertex is sunk by how far it is from the nearest opening against how far it is from the
    /// chamber wall, so the chamber wall is not sunk at all, the rims lie a full throat deep, and
    /// the crotch between two openings settles somewhere between. It wears the wall's own material,
    /// its checker running off the chamber wall and down into the throats.
    /// </summary>
    private Placed BuildFunnel(CapSpec spec)
    {
        var split = spec.Split!;
        double s = spec.S;
        var frame = _track.FrameAt(s);
        var chamber = _track.SectionAt(s);
        var outline = _shapes.Get(chamber);
        var origin = frame.Position;
        float depth = FunnelDepth(split);
        // A fork sinks forward into the split, a merge back into it.
        bool merge = spec.Along > 0f;
        var sink = frame.Forward * (merge ? -depth : depth);
        // The segment this wall belongs to, and how far into it the wall stands: the funnel's
        // texture runs on from there, forward into the split or back into it.
        long segment = (long)Math.Floor(s / _chunkLength);
        float into = (float)(s - segment * _chunkLength);

        int holes = split.BranchCount;
        var centres = new System.Numerics.Vector2[holes];
        var sections = new CrossSection[holes];
        // Each branch tube's first ring, a throat's depth along the branch.
        double ringS = merge ? split.EndS - depth : split.StartS + depth;
        var rings = new TrackFrame[holes];
        for (int b = 0; b < holes; b++)
        {
            centres[b] = split.OffsetAt(b, spec.Along);
            sections[b] = split.Section(b);
            rings[b] = split.BranchFrame(_track, ringS, b);
        }

        // Signed distance from an opening's rim: negative inside it.
        float FromRim(System.Numerics.Vector2 p, int h)
        {
            var q = p - centres[h];
            float along = q.Length();
            var dir = along > 1e-5f ? q / along : new System.Numerics.Vector2(1f, 0f);
            return along - Radius(sections[h], dir);
        }
        float ToWall(System.Numerics.Vector2 p)
        {
            float fromCentre = p.Length();
            var outward = fromCentre > 1e-5f ? p / fromCentre : new System.Numerics.Vector2(1f, 0f);
            return Radius(chamber, outward) - fromCentre;
        }
        // Whether p is in opening h's share of the chamber: inside the chamber, and no nearer
        // any other opening's rim.
        bool Owns(int h, System.Numerics.Vector2 p)
        {
            if (ToWall(p) < 0f) return false;
            float mine = FromRim(p, h);
            for (int g = 0; g < holes; g++)
            {
                if (g != h && FromRim(p, g) < mine) return false;
            }
            return true;
        }

        // How far a point is sunk: 1 at a rim, 0 at the chamber wall, eased at both ends so the
        // surface leaves the wall and meets the tube without a crease. Where the measure has no
        // meaning - outside the chamber, on a skirt - the strip's own fraction stands in, which is
        // what the measure tends to anyway as a rim closes on the wall.
        float Sunk(System.Numerics.Vector2 p, float u)
        {
            float toHole = float.MaxValue;
            for (int h = 0; h < holes; h++) toHole = Math.Min(toHole, FromRim(p, h));
            float toWall = ToWall(p);
            float n = toHole >= 0f && toWall >= 0f && toHole + toWall > 1e-3f ? toHole / (toHole + toWall) : u;
            return 1f - n * n * (3f - 2f * n);
        }

        // How far a point is carried off the straight sink onto the real rings: each opening's
        // correction in the direction of the point, weighted to the nearest rim, so a point at a
        // rim lands on its ring and the crotch between two is a blend with no crease.
        System.Numerics.Vector3 Shift(System.Numerics.Vector2 p)
        {
            var total = System.Numerics.Vector3.Zero;
            float weights = 0f;
            for (int h = 0; h < holes; h++)
            {
                var q = p - centres[h];
                float along = q.Length();
                var dir = along > 1e-5f ? q / along : new System.Numerics.Vector2(1f, 0f);
                var rim = dir * Radius(sections[h], dir);
                var guess = frame.PointOnSection(centres[h] + rim) + sink;
                var shift = (rings[h].PointOnSection(rim) - guess).ToVector3();
                float d = Math.Max(0f, FromRim(p, h));
                float w = 1f / (d * d + 1e-4f);
                total += shift * w;
                weights += w;
            }
            return total / weights;
        }

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int vertex = 0;
        for (int h = 0; h < holes; h++)
        {
            // Every column is emitted, including a closing one that repeats the first with the
            // texture carried on round rather than wrapped back, so the checker does not smear
            // across the last quad.
            float lastRim = float.NaN, previous = 0f;
            for (int k = 0; k <= FunnelColumns; k++)
            {
                float angle = MathF.Tau * k / FunnelColumns;
                var dir = new System.Numerics.Vector2(MathF.Cos(angle), MathF.Sin(angle));
                float radius = Radius(sections[h], dir);
                var rimPoint = centres[h] + dir * radius;

                // Where this opening's share ends along this direction. Bisected along the ray
                // from the opening's centre: a share is star-shaped about its centre, so the ray
                // leaves it once and does not come back.
                float lo = 0f, hi = 4f * Math.Max(chamber.HalfWidth, chamber.HalfHeight);
                for (int step = 0; step < 32; step++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (Owns(h, centres[h] + dir * mid)) lo = mid;
                    else hi = mid;
                }
                var endPoint = centres[h] + dir * lo;
                // Ending halfway to another opening, run a hair past the line: the strip beyond
                // lies exactly on the neighbour's, the same surface sampled twice, and the overlap
                // closes the pinholes two differently sampled edges of one curve would leave.
                if (ToWall(endPoint) > 0.01f) endPoint += dir * 0.05f;

                for (int i = 0; i <= FunnelSteps; i++)
                {
                    float u = (float)i / FunnelSteps;
                    var p = System.Numerics.Vector2.Lerp(rimPoint, endPoint, u);
                    float sunk = i == 0 ? 1f : Sunk(p, u);
                    var position = i == 0
                        ? rings[h].PointOnSection(dir * radius)
                        : frame.PointOnSection(p) + sink * sunk + Shift(p) * sunk;

                    // The checker repeats every whole turn, so of all the copies of this point's
                    // place round the wall, take the one nearest its neighbour's: down the column
                    // from its rim, and rim to rim round the opening.
                    float around = outline.ParameterAt(MathF.Atan2(p.Y, p.X));
                    float reference = i == 0 ? (float.IsNaN(lastRim) ? around : lastRim) : previous;
                    float x = around + MathF.Round(reference - around);
                    if (i == 0) lastRim = x;
                    previous = x;

                    st.SetUV(new Vector2(x, into + (merge ? -depth : depth) * sunk));
                    st.SetUV2(new Vector2(0f, sunk));
                    st.AddVertex(position.RelativeTo(origin).ToGodot());
                }
            }

            int stride = FunnelSteps + 1;
            for (int k = 0; k < FunnelColumns; k++)
            {
                for (int i = 0; i < FunnelSteps; i++)
                {
                    int a = vertex + k * stride + i, b = a + 1, c = a + stride + 1, d = a + stride;
                    st.AddIndex(a); st.AddIndex(b); st.AddIndex(c);
                    st.AddIndex(a); st.AddIndex(c); st.AddIndex(d);
                }
            }
            vertex += (FunnelColumns + 1) * stride;
        }

        var node = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = _material };
        AddChild(node);
        node.SetInstanceShaderParameter("segment_index", (int)segment);
        return new Placed(node, origin);
    }

    // How far the outline of a section lies from its centre in direction <paramref name="dir"/>:
    // the superellipse's radius, the same sum ProfileShape samples its curve with.
    private static float Radius(CrossSection c, System.Numerics.Vector2 dir)
    {
        float n = c.Exponent;
        float a = MathF.Abs(dir.X) / c.HalfWidth, b = MathF.Abs(dir.Y) / c.HalfHeight;
        float m = MathF.Max(a, b);
        if (m <= 0f) return c.HalfWidth;
        return 1f / (m * MathF.Pow(MathF.Pow(a / m, n) + MathF.Pow(b / m, n), 1f / n));
    }

    private ShaderMaterial CapMaterial(TrackSplit? split, float along, bool core)
    {
        var holes = new Vector4[Track.MaxBranches];
        for (int b = 0; b < (split?.BranchCount ?? 0); b++)
        {
            var c = split!.OffsetAt(b, along);
            var section = split.Section(b);
            holes[b] = new Vector4(c.X, c.Y, section.HalfWidth, section.HalfHeight);
        }
        var material = (ShaderMaterial)_capMaterial.Duplicate();
        // A core's face is a flat end that is not wall and must not read as the end of the level,
        // nor - standing in the open bore - as a hole.
        material.SetShaderParameter("cap_color", core ? _coreFaceColor : split is null ? _endWallColor : _capColor);
        // The rim is a bright ring round a fork opening, which is right there and wrong at the end of
        // a level that carries on: it draws a hard edge exactly where there should be no edge.
        if (split is null && !core) material.SetShaderParameter("rim_color", _endRimColor);
        material.SetShaderParameter("holes", holes);
        material.SetShaderParameter("hole_count", split?.BranchCount ?? 0);
        // The shader cuts every hole with one exponent, so branches that differ in squareness all
        // take the first branch's outline. Sizes are still per branch.
        material.SetShaderParameter("hole_exponent", split?.Section(0).Exponent ?? 2f);
        return material;
    }

    // A disc filling the section, or a core's end; a split's wall is a funnel instead.
    private Placed BuildCap(CapSpec spec)
    {
        if (spec.Split is not null) return BuildFunnel(spec);

        double s = spec.S;
        var frame = _track.FrameAt(s);
        var chamber = new ProfileShape(_track.SectionAt(s));
        var origin = frame.Position;

        // A core's face is the core's outline, which is the ceiling of a ring; a wall's is the whole
        // section's, which is the floor of a tube going all the way round.
        var rim = spec.Core ? Surface.Ceiling : Surface.Floor;
        float around = chamber.PerimeterOf(rim);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetUV(Vector2.Zero);
        st.AddVertex(Vector3.Zero);
        for (int i = 0; i <= CapSegments; i++)
        {
            var p = chamber.PointAt(rim, around * i / CapSegments);
            st.SetUV(new Vector2(p.X, p.Y));
            st.AddVertex(frame.PointOnSection(p).RelativeTo(origin).ToGodot());
        }
        for (int i = 1; i <= CapSegments; i++)
        {
            st.AddIndex(0);
            st.AddIndex(i);
            st.AddIndex(i + 1);
        }

        var material = CapMaterial(null, 0f, spec.Core);
        var node = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material };
        AddChild(node);
        return new Placed(node, origin);
    }

    /// <summary>
    /// Surface X positions across one strip - left wing, the surface itself, right wing - and how
    /// many of them there are. <paramref name="ring"/> is the span's own layout, not this section's:
    /// see AddTube.
    /// </summary>
    private int FillStrip(ProfileShape shape, Surface surface, bool ring)
    {
        // A wall of a ring closes on itself rather than meeting the other one, so its strip is the
        // whole way round it and has no wings - and takes twice the vertices to keep the same
        // spacing around the wall as a tube's two strips give between them.
        if (ring)
        {
            float half = shape.PerimeterOf(surface) * 0.5f;
            for (int i = 0; i < RingStripVertices; i++)
            {
                _xs[i] = -half + 2f * half * i / (RingStripVertices - 1);
            }
            return RingStripVertices;
        }

        float q = shape.Quarter, w = shape.WingLength;
        int n = WingSteps.Length;
        for (int i = 0; i < n; i++)
        {
            float wing = q + w * WingSteps[n - 1 - i];
            _xs[i] = -wing;
            _xs[StripVertices - 1 - i] = wing;
        }
        for (int i = 0; i <= SurfaceSegments; i++)
        {
            _xs[n + i] = -q + 2f * q * i / SurfaceSegments;
        }
        return StripVertices;
    }

    private void AddGridIndices(SurfaceTool st, int start, int rings, int stride,
        double from, double to, Surface surface, int branch, ProfileShape shape)
    {
        bool cutting = _warpCuts.Count > 0;
        for (int r = 0; r < rings; r++)
        {
            double s = from + (to - from) * (r + 0.5) / rings;
            for (int a = 0; a < stride - 1; a++)
            {
                // Only the throat at the very bottom of a well is left open; the rest of it is wall
                // drawn sinking inwards, which is what makes the tube extrude into the well.
                if (cutting)
                {
                    float loop = 0.5f * (shape.Loop(surface, _xs[a]) + shape.Loop(surface, _xs[a + 1]));
                    if (WellDepth(s, loop, branch, out _) > ThroatFraction) continue;
                }

                int i = start + r * stride + a;
                st.AddIndex(i);
                st.AddIndex(i + stride);
                st.AddIndex(i + 1);
                st.AddIndex(i + 1);
                st.AddIndex(i + stride);
                st.AddIndex(i + stride + 1);
            }
        }
    }

    private CrossSection SectionOf(double s, TrackSplit? split, int branch) =>
        split is null ? _track.SectionAt(s) : split.Section(branch);

    private static bool InRange(double s, double from, double to) => s >= from && s <= to;

    // A wall across the track: a split's fork or merge, or the end of the level.
    private readonly record struct CapSpec(double S, TrackSplit? Split, float Along, bool Core = false);

    // A warp well, reduced to what shaping the wall around it needs.
    private readonly record struct WarpCut(
        double S, float Loop, float Perimeter, float HalfLength, float HalfWidth, float Depth, int Branch);

    private readonly record struct Placed(MeshInstance3D Node, Vector3d Origin);
}

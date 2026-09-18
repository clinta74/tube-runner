using System;
using System.Collections.Generic;
using Godot;
using TubeRunner.Core;
using Theme = TubeRunner.Core.Theme;

namespace TubeRunner.Game;

/// <summary>
/// Streams track mesh chunks. Each chunk is one wall-pattern segment long and holds a floor and a
/// ceiling strip per tube: one tube on the main track, one per branch inside a split. Where a split
/// forks and merges, a cap wall with openings for the branches closes off the chamber. Everything
/// is re-placed relative to a floating origin every frame, so coordinates near the camera stay small.
/// </summary>
public partial class TrackRenderer : Node3D
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

    private const int RingCapSegments = 48;

    // Fine enough that no piece is stepped over; a core's ends sit on piece boundaries.
    private const double CoreScanStep = 1.0;
    private static readonly Surface[] Surfaces = { Surface.Floor, Surface.Ceiling };

    private readonly Dictionary<long, Placed> _chunks = new();
    private readonly Dictionary<int, Placed> _caps = new();
    private readonly List<CapSpec> _capSpecs = new();
    private readonly List<long> _staleChunks = new();
    private readonly List<int> _staleCaps = new();
    private readonly ProfileShapeCache _shapes = new();
    private readonly float[] _xs = new float[Math.Max(StripVertices, RingStripVertices)];

    // Where a core starts and where it stops, so each end can be closed with a flat cap. A core does
    // not taper in: it is there or it is not, and an open pipe end would be a hole to see down.
    private readonly List<double> _coreEnds = new();
    private readonly List<WarpCut> _warpCuts = new();
    private Track _track = null!;
    private Material _material = null!;
    private ShaderMaterial _capMaterial = null!;
    private Vector3 _capColor;
    private Vector3 _endWallColor;
    private Vector3 _endRimColor;
    private float _chunkLength;

    [Export] public float ViewBehind { get; set; } = 30f;

    /// <summary>How far ahead chunks are built in the solid style, where the fade hides the rest.</summary>
    [Export] public float ViewAhead { get; set; } = 450f;

    // How far ahead this level actually builds. A wireframe level sees much further than a solid one
    // - that is the point of it - and there is no sense fading lines out over a distance the mesh
    // never reaches, which is what happened: the tube simply stopped, well inside the fade.
    private float _viewAhead;

    /// <summary>Frees the current level's meshes, ready for <see cref="Init"/> with the next one.</summary>
    public void Reset()
    {
        foreach (var placed in _chunks.Values) placed.Node.QueueFree();
        foreach (var placed in _caps.Values) placed.Node.QueueFree();
        _chunks.Clear();
        _caps.Clear();
        _capSpecs.Clear();
    }

    /// <param name="warps">Warp mouths, whose openings are cut out of the wall as it is built.</param>
    /// <param name="endsTheRun">
    /// Whether this is the last level. Only then is the end of the track walled off. Levels run
    /// into one another with no pause, so a wall across the finish of every one of them contradicts
    /// that: the tube should look like it carries straight on into the next stretch, which is what
    /// the run actually does.
    /// </param>
    public void Init(Track track, Material material, float chunkLength, Theme theme,
        IReadOnlyList<Warp>? warps = null, bool endsTheRun = true)
    {
        _track = track;
        _material = material;
        _chunkLength = chunkLength;
        // Build out to where the style stops showing anything: the solid fade reaches far_color at
        // FadeEnd, while the wire style only dims to a quarter by twice that.
        _viewAhead = theme.Wire ? Math.Max(ViewAhead, theme.FadeEnd * 2f) : ViewAhead;

        // Where a core begins and where it ends. A core does not blend in: it takes the whole of the
        // piece whose section carries it, so both ends fall on a piece boundary and each is closed
        // off with a flat cap. Found by walking rather than by reading the pieces, so a core that
        // spans several of them is one run with two ends rather than a cap at every join.
        _coreEnds.Clear();
        bool had = track.SectionAt(0).IsAnnulus;
        for (double s = CoreScanStep; s <= track.Length; s += CoreScanStep)
        {
            bool has = track.SectionAt(s).IsAnnulus;
            if (has != had) _coreEnds.Add(s - (has ? CoreScanStep : 0));
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
            if (track.SectionAt(track.Length).IsClosed) _capSpecs.Add(new CapSpec(track.Length, null, 0f));
        }

        // Fork and merge walls sit in shadow around their openings. The wall closing the end of the
        // track is different: on the last level it is the thing you fly at, so it takes a color you
        // can see, but everywhere else the run carries straight on into another level and the end
        // should not read as an end at all. Painting it the distance color, rim included, leaves the
        // tube receding into the same haze everything far away fades into. Leaving it out entirely
        // was worse than the white wall it replaced - it opened onto empty space, so the level
        // finished in a black hole.
        _capColor = theme.SeamDark.ToVector3();
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
        long lastInTrack = (long)Math.Ceiling(_track.Length / _chunkLength) - 1;
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
        double s1 = Math.Min(s0 + _chunkLength, _track.Length);
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

    // Pieces of [s0, s1): the main track outside splits, and every branch inside them.
    private IEnumerable<(double From, double To, TrackSplit? Split, int Branch)> Spans(double s0, double s1)
    {
        double s = s0;
        foreach (var split in _track.Splits)
        {
            if (split.EndS <= s || split.StartS >= s1) continue;
            if (split.StartS > s) yield return (s, split.StartS, null, -1);

            double from = Math.Max(s, split.StartS), to = Math.Min(split.EndS, s1);
            for (int b = 0; b < split.BranchCount; b++) yield return (from, to, split, b);
            s = to;
        }
        if (s < s1) yield return (s, s1, null, -1);
    }

    // One tube (floor and ceiling strips) from `from` to `to`, on the main track or a split's branch.
    private int AddTube(SurfaceTool st, int start, double from, double to, TrackSplit? split, int branch, double s0, Vector3d origin)
    {
        int rings = Math.Max(2, (int)Math.Ceiling((to - from) / _chunkLength * RingsPerChunk));

        // Whether this span is drawn as a ring is decided once, for the whole span, rather than ring
        // by ring. The two layouts describe the same wall in different halves - a hollow tube splits
        // it between the floor and ceiling strips, a ring gives the whole of it to the floor and
        // hands the ceiling the core instead - so a strip that changed its mind part way along would
        // stitch the upper half of the wall to the core and sheet the bore across. A span that is a
        // ring anywhere is a ring throughout; where the core has no size yet the strip closes to a
        // point and the cone it makes is the core arriving, which is what it looks like anyway.
        bool ring = SectionOf(from, split, branch).IsAnnulus || SectionOf(to, split, branch).IsAnnulus;

        foreach (var surface in Surfaces)
        {
            for (int r = 0; r <= rings; r++)
            {
                double s = from + (to - from) * r / rings;
                // Use the split directly so the branch's last ring, at the merge, stays on the branch.
                var frame = split is null ? _track.FrameAt(s) : split.BranchFrame(_track, s, branch);
                var shape = _shapes.Get(split is null ? _track.SectionAt(s) : split.Section(branch));
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

            // A core's ends are closed off, so what arrives is a cylinder with a flat face rather
            // than a pipe open to look down. After the strip's own indices, not before: the cursor
            // this moves is the one those indices are counted from.
            if (surface == Surface.Ceiling && ring)
            {
                foreach (double end in _coreEnds)
                {
                    if (end > from && end <= to) start = AddCoreCap(st, start, end, split, branch, s0, origin);
                }
            }
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

    // A disc filling the section; the cap shader cuts any branch openings out of it.
    private Placed BuildCap(CapSpec spec)
    {
        double s = spec.S;
        var frame = _track.FrameAt(s);
        var chamber = new ProfileShape(_track.SectionAt(s));
        var origin = frame.Position;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetUV(Vector2.Zero);
        st.AddVertex(Vector3.Zero);
        for (int i = 0; i <= CapSegments; i++)
        {
            var p = chamber.PointAt(Surface.Floor, chamber.Perimeter * i / CapSegments);
            st.SetUV(new Vector2(p.X, p.Y));
            st.AddVertex(frame.PointOnSection(p).RelativeTo(origin).ToGodot());
        }
        for (int i = 1; i <= CapSegments; i++)
        {
            st.AddIndex(0);
            st.AddIndex(i);
            st.AddIndex(i + 1);
        }

        var holes = new Vector4[Track.MaxBranches];
        var split = spec.Split;
        for (int b = 0; b < (split?.BranchCount ?? 0); b++)
        {
            var c = split!.OffsetAt(b, spec.Along);
            var section = split.Section(b);
            holes[b] = new Vector4(c.X, c.Y, section.HalfWidth, section.HalfHeight);
        }
        var material = (ShaderMaterial)_capMaterial.Duplicate();
        material.SetShaderParameter("cap_color", split is null ? _endWallColor : _capColor);
        // The rim is a bright ring round a fork opening, which is right there and wrong at the end of
        // a level that carries on: it draws a hard edge exactly where there should be no edge.
        if (split is null) material.SetShaderParameter("rim_color", _endRimColor);
        material.SetShaderParameter("holes", holes);
        material.SetShaderParameter("hole_count", split?.BranchCount ?? 0);
        // The shader cuts every hole with one exponent, so branches that differ in squareness all
        // take the first branch's outline. Sizes are still per branch.
        material.SetShaderParameter("hole_exponent", split?.Section(0).Exponent ?? 2f);

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

    /// <summary>
    /// The flat face closing one end of a core: a fan from the centreline out to the core's wall.
    /// Takes the wall's own material and shading, so it reads as the end of the thing rather than as
    /// a lid put on it.
    /// </summary>
    private int AddCoreCap(SurfaceTool st, int start, double at, TrackSplit? split, int branch, double s0, Vector3d origin)
    {
        const int segments = RingCapSegments;
        var frame = split is null ? _track.FrameAt(at) : split.BranchFrame(_track, at, branch);
        var shape = _shapes.Get(SectionOf(at, split, branch));
        float around = shape.PerimeterOf(Surface.Ceiling);
        if (around <= 0.01f) return start;

        // The wall's seam is drawn wherever a point sits near either end of a segment, which means
        // the distance written here has to stay inside one - a face is flat across the track, so
        // every point on it shares whatever distance it is given, and one outside the segment came
        // out as a single blown white disc. It is given a distance mid-segment instead, moved a
        // little between the middle of the face and its rim so the checker has something to cut.
        // One distance for the whole face, not one per ring of it. The wall widens its seam test by
        // the screen derivative of this number, and a face seen almost edge-on has a derivative big
        // enough to swallow the test whole - which drew the disc as one blown seam.
        float middle = _chunkLength * 0.45f;
        float rim = middle;

        st.SetUV(new Vector2(0.75f, middle));
        st.SetUV2(Vector2.Zero);
        st.AddVertex(frame.Position.RelativeTo(origin).ToGodot());
        for (int i = 0; i <= segments; i++)
        {
            var (surface, x) = shape.Wrap(Surface.Ceiling, around * i / segments);
            var p = shape.PointAt(surface, x);
            st.SetUV(new Vector2(0.75f + (float)i / segments, rim));
            st.SetUV2(Vector2.Zero);
            st.AddVertex(frame.PointOnSection(p).RelativeTo(origin).ToGodot());
        }
        for (int i = 0; i < segments; i++)
        {
            st.AddIndex(start);
            st.AddIndex(start + 1 + i);
            st.AddIndex(start + 2 + i);
        }
        return start + segments + 2;
    }

    private CrossSection SectionOf(double s, TrackSplit? split, int branch) =>
        split is null ? _track.SectionAt(s) : split.Section(branch);

    private static bool InRange(double s, double from, double to) => s >= from && s <= to;

    // A wall across the track: a split's fork or merge, or the end of the level.
    private readonly record struct CapSpec(double S, TrackSplit? Split, float Along);

    // A warp well, reduced to what shaping the wall around it needs.
    private readonly record struct WarpCut(
        double S, float Loop, float Perimeter, float HalfLength, float HalfWidth, float Depth, int Branch);

    private readonly record struct Placed(MeshInstance3D Node, Vector3d Origin);
}

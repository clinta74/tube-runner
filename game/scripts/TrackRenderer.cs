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
    // Rings across a fork or merge wall, so the divider between the branches can stand off the plane
    // rather than the wall being flat, and how far it stands off at its nose.
    private const int CapRings = 10;
    private const float FunnelReach = 20f;
    // Wing vertices as fractions of the wing length; they collapse onto the edge when there's no wing.
    private static readonly float[] WingSteps = { 0.02f, 0.1f, 0.35f, 1f };
    private static readonly int StripVertices = SurfaceSegments + 1 + 2 * WingSteps.Length;
    private static readonly Surface[] Surfaces = { Surface.Floor, Surface.Ceiling };

    private readonly Dictionary<long, Placed> _chunks = new();
    private readonly Dictionary<int, Placed> _caps = new();
    private readonly List<CapSpec> _capSpecs = new();
    private readonly List<long> _staleChunks = new();
    private readonly List<int> _staleCaps = new();
    private readonly ProfileShapeCache _shapes = new();
    private readonly float[] _xs = new float[StripVertices];
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
        foreach (var surface in Surfaces)
        {
            for (int r = 0; r <= rings; r++)
            {
                double s = from + (to - from) * r / rings;
                // Use the split directly so the branch's last ring, at the merge, stays on the branch.
                var frame = split is null ? _track.FrameAt(s) : split.BranchFrame(_track, s, branch);
                var shape = _shapes.Get(split is null ? _track.SectionAt(s) : split.Section(branch));
                FillStrip(shape);
                foreach (float x in _xs)
                {
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
                    st.SetUV(new Vector2(0.75f + loop / shape.Perimeter, (float)(s - s0)));
                    st.SetUV2(new Vector2(well, 0f));
                    st.AddVertex(frame.PointOnSection(p).RelativeTo(origin).ToGodot());
                }
            }
            // Cut against the section in the middle of the span; it barely changes across a chunk.
            var midShape = _shapes.Get(split is null ? _track.SectionAt((from + to) * 0.5) : split.Section(branch));
            FillStrip(midShape);
            AddGridIndices(st, start, rings, StripVertices, from, to, surface, branch, midShape);
            start += (rings + 1) * StripVertices;
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

        var split = spec.Split;
        // The divider stands towards whoever is looking at it: back up the track at a fork, where the
        // ship is arriving, and on down it at a merge, where the ship arrives from the branches.
        float lean = spec.Along <= 0f ? -1f : 1f;

        // A grid rather than a fan, so the surface between the branch mouths can stand off the plane.
        // Flat, this was a disc with holes punched through it and the player flew at a wall.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int stride = CapSegments + 1;
        for (int r = 0; r <= CapRings; r++)
        {
            float t = (float)r / CapRings;
            for (int i = 0; i <= CapSegments; i++)
            {
                var p = chamber.PointAt(Surface.Floor, chamber.Perimeter * i / CapSegments) * t;
                double at = s + lean * FunnelDepth(split, spec.Along, p, t);
                // The UV stays the flat section position: that is what the shader cuts the holes
                // against, and it must not move with the vertex.
                st.SetUV(new Vector2(p.X, p.Y));
                st.AddVertex(_track.FrameAt(at).PointOnSection(p).RelativeTo(origin).ToGodot());
            }
        }
        for (int r = 0; r < CapRings; r++)
        {
            for (int i = 0; i < CapSegments; i++)
            {
                int a = r * stride + i;
                st.AddIndex(a);
                st.AddIndex(a + stride);
                st.AddIndex(a + 1);
                st.AddIndex(a + 1);
                st.AddIndex(a + stride);
                st.AddIndex(a + stride + 1);
            }
        }

        var holes = new Vector4[Track.MaxBranches];
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
    /// How far the chamber wall stands off the split plane at this point in the section, which is
    /// what turns a flat wall into a junction.
    ///
    /// It is the divider between the branches that moves, not the mouths. At a real fork the branch
    /// openings sit in the plane and the wedge between them noses back towards the oncoming ship,
    /// with the chamber flowing into each tube around it. Pushing the surface the other way - into
    /// the branches - put it inside the first stretch of tube the branches already draw, so two
    /// surfaces fought over the same space, and it lifted the outer edge off the plane so the cap no
    /// longer met the chamber wall at all.
    ///
    /// Zero at a branch rim and zero at the chamber wall, so it seams with both.
    /// </summary>
    /// <param name="t">How far out in the section this point is: 0 at the centre, 1 at the wall.</param>
    private static float FunnelDepth(TrackSplit? split, float along, System.Numerics.Vector2 p, float t)
    {
        if (split is null) return 0f;

        float nearest = float.MaxValue;
        for (int b = 0; b < split.BranchCount; b++)
        {
            var c = split.OffsetAt(b, along);
            var section = split.Section(b);
            float qx = (p.X - c.X) / section.HalfWidth;
            float qy = (p.Y - c.Y) / section.HalfHeight;
            nearest = MathF.Min(nearest, MathF.Sqrt(qx * qx + qy * qy));
        }

        // Nothing at a rim, rising as the surface leaves one behind...
        float away = Math.Clamp((nearest - 1f) / 0.5f, 0f, 1f);
        // ...and nothing at the chamber wall, so the junction still meets the tube around it.
        return FunnelReach * away * (1f - t * t);
    }

    // Surface X positions across one strip: left wing, the surface itself, right wing.
    private void FillStrip(ProfileShape shape)
    {
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

    private static bool InRange(double s, double from, double to) => s >= from && s <= to;

    // A wall across the track: a split's fork or merge, or the end of the level.
    private readonly record struct CapSpec(double S, TrackSplit? Split, float Along);

    // A warp well, reduced to what shaping the wall around it needs.
    private readonly record struct WarpCut(
        double S, float Loop, float Perimeter, float HalfLength, float HalfWidth, float Depth, int Branch);

    private readonly record struct Placed(MeshInstance3D Node, Vector3d Origin);
}

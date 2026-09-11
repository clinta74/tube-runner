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
    private const int SurfaceSegments = 32;
    private const int CapSegments = 96;
    // Wing vertices as fractions of the wing length; they collapse onto the edge when there's no wing.
    private static readonly float[] WingSteps = { 0.02f, 0.1f, 0.35f, 1f };
    private static readonly int StripVertices = SurfaceSegments + 1 + 2 * WingSteps.Length;
    private static readonly Surface[] Surfaces = { Surface.Floor, Surface.Ceiling };

    private readonly Dictionary<long, Placed> _chunks = new();
    private readonly Dictionary<(TrackSplit Split, bool AtStart), Placed> _caps = new();
    private readonly List<long> _staleChunks = new();
    private readonly List<(TrackSplit, bool)> _staleCaps = new();
    private readonly ProfileShapeCache _shapes = new();
    private readonly float[] _xs = new float[StripVertices];
    private Track _track = null!;
    private Material _material = null!;
    private ShaderMaterial _capMaterial = null!;
    private float _chunkLength;

    [Export] public float ViewBehind { get; set; } = 30f;
    [Export] public float ViewAhead { get; set; } = 450f;

    public void Init(Track track, Material material, float chunkLength, Theme theme)
    {
        _track = track;
        _material = material;
        _chunkLength = chunkLength;

        _capMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/track_cap.gdshader") };
        _capMaterial.SetShaderParameter("cap_color", theme.SeamDark.ToVector3());
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
        double from = s - ViewBehind, to = s + ViewAhead;
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
                var shape = _shapes.Get(split is null ? _track.SectionAt(s) : split.Section);
                FillStrip(shape);
                foreach (float x in _xs)
                {
                    st.SetUV(new Vector2(0.75f + shape.Loop(surface, x) / shape.Perimeter, (float)(s - s0)));
                    st.AddVertex(frame.PointOnSection(shape.PointAt(surface, x)).RelativeTo(origin).ToGodot());
                }
            }
            AddGridIndices(st, start, rings, StripVertices);
            start += (rings + 1) * StripVertices;
        }
        return start;
    }

    private void UpdateCaps(double from, double to)
    {
        _staleCaps.Clear();
        foreach (var key in _caps.Keys)
        {
            if (!InRange(CapS(key), from, to)) _staleCaps.Add(key);
        }
        foreach (var key in _staleCaps)
        {
            _caps[key].Node.QueueFree();
            _caps.Remove(key);
        }
        foreach (var split in _track.Splits)
        {
            AddCapIfInView(split, atStart: true, from, to);
            AddCapIfInView(split, atStart: false, from, to);
        }
    }

    private void AddCapIfInView(TrackSplit split, bool atStart, double from, double to)
    {
        var key = (split, atStart);
        if (!_caps.ContainsKey(key) && InRange(CapS(key), from, to)) _caps[key] = BuildCap(split, atStart);
    }

    // A disc filling the chamber's cross-section; the cap shader cuts the branch openings out of it.
    private Placed BuildCap(TrackSplit split, bool atStart)
    {
        double s = atStart ? split.StartS : split.EndS;
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
        float along = atStart ? 0f : split.Length;
        for (int b = 0; b < split.BranchCount; b++)
        {
            var c = split.OffsetAt(b, along);
            holes[b] = new Vector4(c.X, c.Y, split.Section.HalfWidth, split.Section.HalfHeight);
        }
        var material = (ShaderMaterial)_capMaterial.Duplicate();
        material.SetShaderParameter("holes", holes);
        material.SetShaderParameter("hole_count", split.BranchCount);
        material.SetShaderParameter("hole_exponent", split.Section.Exponent);

        var node = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material };
        AddChild(node);
        return new Placed(node, origin);
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

    private static void AddGridIndices(SurfaceTool st, int start, int rings, int stride)
    {
        for (int r = 0; r < rings; r++)
        {
            for (int a = 0; a < stride - 1; a++)
            {
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

    private static double CapS((TrackSplit Split, bool AtStart) key) => key.AtStart ? key.Split.StartS : key.Split.EndS;

    private static bool InRange(double s, double from, double to) => s >= from && s <= to;

    private readonly record struct Placed(MeshInstance3D Node, Vector3d Origin);
}

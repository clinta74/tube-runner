using System;
using System.Collections.Generic;
using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// Streams track mesh chunks. Each chunk is one wall-pattern segment long and holds a floor strip
/// and a ceiling strip, which meet to form a closed tube or separate into open planes. Chunks are
/// re-placed relative to a floating origin every frame, so coordinates near the camera stay small.
/// </summary>
public partial class TrackRenderer : Node3D
{
    private const int RingsPerChunk = 40;
    private const int SurfaceSegments = 32;
    // Wing vertices as fractions of the wing length; they collapse onto the edge when there's no wing.
    private static readonly float[] WingSteps = { 0.02f, 0.1f, 0.35f, 1f };
    private static readonly int StripVertices = SurfaceSegments + 1 + 2 * WingSteps.Length;
    private static readonly Surface[] Surfaces = { Surface.Floor, Surface.Ceiling };

    private readonly Dictionary<long, Chunk> _chunks = new();
    private readonly List<long> _stale = new();
    private readonly ProfileShapeCache _shapes = new();
    private readonly float[] _xs = new float[StripVertices];
    private Track _track = null!;
    private Material _material = null!;
    private float _chunkLength;

    [Export] public float ViewBehind { get; set; } = 30f;
    [Export] public float ViewAhead { get; set; } = 450f;

    public void Init(Track track, Material material, float chunkLength)
    {
        _track = track;
        _material = material;
        _chunkLength = chunkLength;
    }

    /// <summary>
    /// Builds chunks around <paramref name="s"/>, frees ones out of view, and places every chunk
    /// relative to <paramref name="origin"/>.
    /// </summary>
    public void UpdateView(double s, Vector3d origin)
    {
        long first = (long)Math.Floor((s - ViewBehind) / _chunkLength);
        long last = (long)Math.Floor((s + ViewAhead) / _chunkLength);
        _track.EnsureLength((last + 1) * (double)_chunkLength + Track.SampleSpacing);

        _stale.Clear();
        foreach (long k in _chunks.Keys)
        {
            if (k < first || k > last) _stale.Add(k);
        }
        foreach (long k in _stale)
        {
            _chunks[k].Node.QueueFree();
            _chunks.Remove(k);
        }
        if (_stale.Count > 0) _track.TrimBefore(first * (double)_chunkLength);

        for (long k = Math.Max(first, 0); k <= last; k++)
        {
            if (!_chunks.ContainsKey(k)) _chunks[k] = BuildChunk(k);
        }

        foreach (var chunk in _chunks.Values)
        {
            chunk.Node.Position = chunk.Origin.RelativeTo(origin).ToGodot();
        }
    }

    private Chunk BuildChunk(long k)
    {
        double s0 = k * (double)_chunkLength;
        var origin = _track.FrameAt(s0).Position;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        int start = 0;
        foreach (var surface in Surfaces)
        {
            for (int r = 0; r <= RingsPerChunk; r++)
            {
                float local = _chunkLength * r / RingsPerChunk;
                var frame = _track.FrameAt(s0 + local);
                var shape = _shapes.Get(_track.SectionAt(s0 + local));
                FillStrip(shape);
                foreach (float x in _xs)
                {
                    st.SetUV(new Vector2(0.75f + shape.Loop(surface, x) / shape.Perimeter, local));
                    st.AddVertex(frame.PointOnSection(shape.PointAt(surface, x)).RelativeTo(origin).ToGodot());
                }
            }
            AddGridIndices(st, start, RingsPerChunk, StripVertices);
            start += (RingsPerChunk + 1) * StripVertices;
        }

        var node = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = _material };
        AddChild(node);
        node.SetInstanceShaderParameter("segment_index", (int)k);
        return new Chunk(node, origin);
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

    private readonly record struct Chunk(MeshInstance3D Node, Vector3d Origin);
}

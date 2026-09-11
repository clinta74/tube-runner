using System;
using System.Collections.Generic;
using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// Streams tube mesh chunks along the track. Each chunk is one wall-pattern segment long and is
/// re-placed relative to a floating origin every frame, so coordinates near the camera stay small.
/// </summary>
public partial class TrackRenderer : Node3D
{
    private const int RingsPerChunk = 40;
    private const int RadialSegments = 64;

    private readonly Dictionary<long, Chunk> _chunks = new();
    private readonly List<long> _stale = new();
    private readonly ProfileShapeCache _shapes = new();
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

        for (int r = 0; r <= RingsPerChunk; r++)
        {
            float local = _chunkLength * r / RingsPerChunk;
            var frame = _track.FrameAt(s0 + local);
            var shape = _shapes.Get(_track.SectionAt(s0 + local));
            for (int a = 0; a <= RadialSegments; a++)
            {
                float u = a / (float)RadialSegments;
                var p = shape.PointAt(u);
                st.SetUV(new Vector2(u, local));
                // The shader discards where this is negative (open side walls).
                st.SetUV2(new Vector2(shape.OpenMargin(p), 0f));
                st.AddVertex(frame.PointOnSection(p).RelativeTo(origin).ToGodot());
            }
        }

        int stride = RadialSegments + 1;
        for (int r = 0; r < RingsPerChunk; r++)
        {
            for (int a = 0; a < RadialSegments; a++)
            {
                int i = r * stride + a;
                st.AddIndex(i);
                st.AddIndex(i + stride);
                st.AddIndex(i + 1);
                st.AddIndex(i + 1);
                st.AddIndex(i + stride);
                st.AddIndex(i + stride + 1);
            }
        }

        var node = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = _material };
        AddChild(node);
        node.SetInstanceShaderParameter("segment_index", (int)k);
        return new Chunk(node, origin);
    }

    private readonly record struct Chunk(MeshInstance3D Node, Vector3d Origin);
}

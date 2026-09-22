using System;
using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// The track on screen: the level being flown, and behind its end, the level after it.
///
/// A level's last stretch is a copy of the next level's opening (see <see cref="LevelJoin"/>), so
/// once the next level has been read its own opening can stand in for that copy - the same walls,
/// drawn in the next level's colours and on its own segment grid. The change of colour then sits at
/// a wall seam ahead of the ship, where every seam already changes the pattern, and when the next
/// level takes over its view is simply kept: the same chunks, the same material, the same indices,
/// so nothing on screen changes. Both views live in this node; the next one is turned to lie along
/// the end of the current track.
/// </summary>
public partial class TrackRenderer : Node3D
{
    private TrackView? _current;
    private TrackView? _next;
    private Level? _nextLevel;
    private RigidMap _map;
    private double _tailFrom;
    private float _viewBehind = 30f;

    /// <summary>How much track is kept built behind the ship.</summary>
    [Export]
    public float ViewBehind
    {
        get => _viewBehind;
        set
        {
            _viewBehind = value;
            if (_current is not null) _current.ViewBehind = value;
            if (_next is not null) _next.ViewBehind = value;
        }
    }

    /// <summary>The level drawn over the end of the current one, if it has been set.</summary>
    public Level? Next => _nextLevel;

    /// <summary>The move that lays the next level's track over the end of the current one.</summary>
    public RigidMap Map => _map;

    /// <summary>Where along the current track the next level's colours begin.</summary>
    public double TailFrom => _tailFrom;

    /// <summary>Frees everything, both levels.</summary>
    public void Reset()
    {
        Drop(ref _current);
        Drop(ref _next);
        _nextLevel = null;
    }

    /// <summary>
    /// Starts drawing <paramref name="level"/>. If it is the level already being drawn ahead as the
    /// next one, that view is kept as it stands; otherwise a fresh one is built and any pending
    /// next view is dropped, to be set again if it still applies.
    /// </summary>
    public void Init(Level level, ShaderMaterial baseMaterial, int segmentOffset)
    {
        if (_next is not null && ReferenceEquals(_nextLevel, level))
        {
            Drop(ref _current);
            _current = _next;
            _next = null;
            _nextLevel = null;
            // Its own world now: no longer turned to fit the end of the level before.
            _current.Transform = Transform3D.Identity;
        }
        else
        {
            Drop(ref _current);
            Drop(ref _next);
            _nextLevel = null;
            _current = Build(level, baseMaterial);
            _current.SegmentOffset = segmentOffset;
        }
    }

    /// <summary>The shift on the current level's segment indices; see <see cref="SegmentJoin"/>.</summary>
    public int SegmentOffset
    {
        get => _current?.SegmentOffset ?? 0;
        set
        {
            if (_current is not null) _current.SegmentOffset = value;
        }
    }

    /// <summary>
    /// Draws <paramref name="next"/>'s opening over the end of the current level. Its view is built
    /// now, on its own material, and kept when that level takes over.
    /// </summary>
    public void SetNext(Level next, ShaderMaterial baseMaterial)
    {
        Drop(ref _next);
        _nextLevel = next;
        _next = Build(next, baseMaterial);
        Attach();
    }

    private void Attach()
    {
        if (_current is null || _next is null) return;
        _tailFrom = _current.Track.Length - LevelJoin.Handover;
        _map = LevelJoin.Alignment(_current.Track, _next.Track);
        _current.DrawTo = _tailFrom;
        // Everything in the next view is placed in its own track's world, then this basis turns the
        // whole of it to lie along the end of the current track.
        _next.Transform = new Transform3D(new Basis(_map.X.ToGodot(), _map.Y.ToGodot(), _map.Z.ToGodot()), Vector3.Zero);
    }

    /// <summary>Builds and frees chunks around <paramref name="s"/>, on both levels, relative to <paramref name="origin"/>.</summary>
    public void UpdateView(double s, Vector3d origin)
    {
        _current?.UpdateView(s, origin);
        // The next view measures from the same ship, in its own track's terms: the ship is
        // somewhere before its start, and the origin is where the ship is in its world.
        _next?.UpdateView(s - _tailFrom, _map.Unmap(origin));
    }

    private TrackView Build(Level level, ShaderMaterial baseMaterial)
    {
        var view = new TrackView { ViewBehind = _viewBehind };
        AddChild(view);
        view.Init(level.Track, baseMaterial, level.SegmentLength, level.Theme, level.Warps, closeTheEnd: level.Next is null);
        return view;
    }

    private static void Drop(ref TrackView? view)
    {
        if (view is null) return;
        view.Reset();
        view.QueueFree();
        view = null;
    }
}

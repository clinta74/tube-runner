using System.Collections.Generic;
using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// Everything on the walls: the level being flown, and behind its end, the level after it, drawn
/// the same way <see cref="TrackRenderer"/> draws its walls - in its own world, turned to lie along
/// the end of the current track. A next level's things are drawn from a session that is never
/// stepped, so nothing moves or fires there; when the level takes over, its view is kept and simply
/// pointed at the real session, whose obstacles are the same objects.
/// </summary>
public partial class ObstacleRenderer : Node3D
{
    private ObstacleView? _current;
    private ObstacleView? _next;
    private Level? _nextLevel;

    public void Reset()
    {
        Drop(ref _current);
        Drop(ref _next);
        _nextLevel = null;
    }

    /// <summary>
    /// Starts drawing <paramref name="level"/> for <paramref name="session"/>. If it is the level
    /// already drawn ahead as the next one, that view is kept and re-pointed; otherwise a fresh one
    /// is built and any pending next view is dropped, to be set again if it still applies.
    /// </summary>
    public void Init(GameSession session, Level level, IReadOnlyList<JumpWindow> jumpWindows)
    {
        if (_next is not null && ReferenceEquals(_nextLevel, level))
        {
            Drop(ref _current);
            _current = _next;
            _next = null;
            _nextLevel = null;
            _current.Transform = Transform3D.Identity;
            _current.ShipS = null;
            _current.Rebind(session);
            return;
        }

        Drop(ref _current);
        Drop(ref _next);
        _nextLevel = null;
        _current = new ObstacleView();
        AddChild(_current);
        _current.Init(session, level.Theme, jumpWindows);
    }

    /// <summary>Draws <paramref name="next"/>'s things over the end of the current level, placed by <paramref name="map"/>.</summary>
    public void SetNext(Level next, GameSession preview, IReadOnlyList<JumpWindow> jumpWindows, RigidMap map)
    {
        Drop(ref _next);
        _nextLevel = next;
        _next = new ObstacleView
        {
            Transform = new Transform3D(new Basis(map.X.ToGodot(), map.Y.ToGodot(), map.Z.ToGodot()), Vector3.Zero),
        };
        AddChild(_next);
        _next.Init(preview, next.Theme, jumpWindows);
    }

    /// <param name="s">Where the ship is along the current track.</param>
    /// <param name="tailFrom">Where along the current track the next level begins.</param>
    /// <param name="secondsToLine">How long until the next level starts, at the ship's present speed.</param>
    public void UpdateView(Vector3d origin, float dt, double s, double tailFrom, RigidMap map, float secondsToLine)
    {
        _current?.UpdateView(origin, dt);
        if (_next is null) return;
        _next.ShipS = s - tailFrom;
        _next.Clock = -secondsToLine;
        _next.UpdateView(map.Unmap(origin), dt);
    }

    private static void Drop(ref ObstacleView? view)
    {
        if (view is null) return;
        view.Reset();
        view.QueueFree();
        view = null;
    }
}

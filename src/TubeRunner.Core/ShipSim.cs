namespace TubeRunner.Core;

/// <param name="ForwardSpeed">Units per second along the track.</param>
/// <param name="SteerSpeed">Units per second across the wall at full steer, whatever the cross-section.</param>
public sealed record ShipSettings(float ForwardSpeed, float SteerSpeed);

/// <summary>
/// Engine-independent ship simulation. Advances the ship in track space and keeps it on the wall
/// (out of the open gaps in flat-plane sections).
/// </summary>
public sealed class ShipSim
{
    private readonly ShipSettings _settings;
    private readonly Track _track;
    private readonly ProfileShapeCache _shapes = new();

    public ShipSim(ShipSettings settings, Track track, TrackPosition start = default)
    {
        _settings = settings;
        _track = track;
        Shape = _shapes.Get(track.SectionAt(start.S));
        Position = start with { U = Shape.ClampToSurface(start.U) };
    }

    public TrackPosition Position { get; private set; }

    /// <summary>Cross-section shape at the ship's position.</summary>
    public ProfileShape Shape { get; private set; }

    /// <param name="dt">Seconds to advance.</param>
    /// <param name="steer">Steering input in [-1, 1]; positive increases U.</param>
    public void Step(float dt, float steer)
    {
        steer = Math.Clamp(steer, -1f, 1f);
        double s = Position.S + _settings.ForwardSpeed * dt;
        Shape = _shapes.Get(_track.SectionAt(s));
        float u = Position.U + steer * _settings.SteerSpeed * dt / Shape.Perimeter;
        Position = Position with { S = s, U = Shape.ClampToSurface(u) };
    }
}

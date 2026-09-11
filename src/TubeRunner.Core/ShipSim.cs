namespace TubeRunner.Core;

/// <param name="ForwardSpeed">Units per second along the track.</param>
/// <param name="SteerRate">Revolutions around the tube per second at full steer.</param>
public sealed record ShipSettings(float ForwardSpeed, float SteerRate);

/// <summary>
/// Engine-independent ship simulation. Advances the ship in track space.
/// </summary>
public sealed class ShipSim
{
    private readonly ShipSettings _settings;

    public ShipSim(ShipSettings settings, TrackPosition start = default)
    {
        _settings = settings;
        Position = start with { U = Wrap01(start.U) };
    }

    public TrackPosition Position { get; private set; }

    /// <param name="dt">Seconds to advance.</param>
    /// <param name="steer">Steering input in [-1, 1]; positive increases U.</param>
    public void Step(float dt, float steer)
    {
        steer = Math.Clamp(steer, -1f, 1f);
        Position = Position with
        {
            S = Position.S + _settings.ForwardSpeed * dt,
            U = Wrap01(Position.U + steer * _settings.SteerRate * dt),
        };
    }

    internal static float Wrap01(float v)
    {
        v -= MathF.Floor(v);
        // Tiny negative inputs can round up to exactly 1.
        return v >= 1f ? 0f : v;
    }
}

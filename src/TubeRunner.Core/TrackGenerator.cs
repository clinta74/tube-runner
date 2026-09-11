namespace TubeRunner.Core;

/// <summary>
/// Endless seeded track: straights and gentle curves in a round tube, with occasional oval
/// stretches and flat-plane sections (circle → oval → box → open planes → and back).
/// Every planned sequence starts and ends on the round tube.
/// </summary>
public sealed class TrackGenerator : ITrackSource
{
    private readonly Random _rng;
    private readonly Queue<TrackPiece> _pending = new();
    private bool _started;

    public TrackGenerator(int seed, float tubeRadius = 6f)
    {
        _rng = new Random(seed);
        Circle = CrossSection.Circle(tubeRadius);
        Oval = new CrossSection(tubeRadius * 1.5f, tubeRadius * 0.75f);
        Box = new CrossSection(tubeRadius * 1.7f, tubeRadius * 0.7f, Squareness: 1f);
        Planes = Box with { Opening = 1f };
    }

    public CrossSection Circle { get; }
    public CrossSection Oval { get; }
    public CrossSection Box { get; }
    public CrossSection Planes { get; }

    public TrackPiece Next()
    {
        if (_pending.Count == 0) Plan();
        return _pending.Dequeue();
    }

    private void Plan()
    {
        if (!_started)
        {
            _started = true;
            _pending.Enqueue(new TrackPiece(100f, Circle));
            return;
        }

        double roll = _rng.NextDouble();
        if (roll < 0.2)
        {
            _pending.Enqueue(new TrackPiece(Range(60f, 120f), Circle));
        }
        else if (roll < 0.65)
        {
            _pending.Enqueue(Curve(Circle, Range(80f, 160f)));
        }
        else if (roll < 0.85)
        {
            _pending.Enqueue(Curve(Oval, 60f, maxYaw: 0.006f));
            _pending.Enqueue(Curve(Oval, Range(100f, 200f)));
            _pending.Enqueue(Curve(Circle, 60f, maxYaw: 0.006f));
        }
        else
        {
            _pending.Enqueue(new TrackPiece(60f, Oval));
            _pending.Enqueue(new TrackPiece(50f, Box));
            _pending.Enqueue(new TrackPiece(40f, Planes));
            _pending.Enqueue(Curve(Planes, Range(150f, 250f), maxYaw: 0.006f, maxPitch: 0.002f));
            _pending.Enqueue(new TrackPiece(40f, Box));
            _pending.Enqueue(new TrackPiece(50f, Oval));
            _pending.Enqueue(new TrackPiece(60f, Circle));
        }
    }

    private TrackPiece Curve(CrossSection end, float length, float maxYaw = 0.012f, float maxPitch = 0.006f) =>
        new(length, end, YawRate: Signed(0.003f, maxYaw), PitchRate: Signed(0f, maxPitch));

    private float Range(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

    private float Signed(float min, float max) => Range(min, max) * (_rng.Next(2) == 0 ? -1f : 1f);
}

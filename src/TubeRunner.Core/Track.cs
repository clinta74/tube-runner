using System.Numerics;

namespace TubeRunner.Core;

/// <summary>Position and orientation of the track centerline at some distance along it.</summary>
public readonly record struct TrackFrame(Vector3d Position, Vector3 Forward, Vector3 Up, Vector3 Right)
{
    /// <summary>World position of a cross-section point (x along Right, y along Up).</summary>
    public Vector3d PointOnSection(Vector2 p) => Position + DirectionOnSection(p);

    /// <summary>World direction of a cross-section vector.</summary>
    public Vector3 DirectionOnSection(Vector2 d) => Right * d.X + Up * d.Y;
}

/// <summary>A stretch of track: curves at constant rates while blending to <paramref name="EndSection"/>.</summary>
/// <param name="YawRate">Radians per unit around the frame's Up; positive turns left.</param>
/// <param name="PitchRate">Radians per unit around the frame's Right; positive pitches up.</param>
/// <param name="EndSpeed">Forward speed to reach by the end of the piece; null keeps the current speed.</param>
public readonly record struct TrackPiece(
    float Length,
    CrossSection EndSection,
    float YawRate = 0f,
    float PitchRate = 0f,
    float? EndSpeed = null);

/// <summary>
/// The track centerline and cross-sections, built from appended pieces. Centerline frames are
/// sampled every <see cref="SampleSpacing"/> units and interpolated between.
/// </summary>
public sealed class Track
{
    public const float SampleSpacing = 1f;

    private readonly List<TrackFrame> _frames = new();
    private readonly List<PlacedPiece> _pieces = new();
    private readonly CrossSection _startSection;
    private readonly float _startSpeed;
    private CrossSection _endSection;
    private float _endSpeed;

    /// <param name="startSpeed">Forward speed at the start, in units per second.</param>
    public Track(CrossSection startSection, float startSpeed = 80f)
    {
        _startSection = _endSection = startSection;
        _startSpeed = _endSpeed = startSpeed;
        // Starts at the origin heading -Z with +Y up (Godot's conventions).
        _frames.Add(new TrackFrame(default, -Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX));
    }

    /// <summary>Total length appended so far.</summary>
    public double Length { get; private set; }

    private double LastFrameS => (_frames.Count - 1) * (double)SampleSpacing;

    public void Append(TrackPiece piece)
    {
        // Open planes extend toward the horizon; bending them would fold them through themselves.
        bool open = !_endSection.IsClosed || !piece.EndSection.IsClosed;
        if (open && (piece.YawRate != 0f || piece.PitchRate != 0f))
        {
            throw new ArgumentException("Pieces that open into flat planes must be straight.");
        }

        _pieces.Add(new PlacedPiece(Length, piece, _endSection, _endSpeed));
        _endSection = piece.EndSection;
        _endSpeed = piece.EndSpeed ?? _endSpeed;
        Length += piece.Length;

        while (LastFrameS + SampleSpacing <= Length)
        {
            var rates = _pieces[FindPiece(LastFrameS + SampleSpacing * 0.5)].Piece;
            _frames.Add(Advance(_frames[^1], rates.YawRate, rates.PitchRate, SampleSpacing));
        }
    }

    /// <summary>Centerline frame at distance <paramref name="s"/>, clamped to the track.</summary>
    public TrackFrame FrameAt(double s)
    {
        double k = Math.Clamp(s / SampleSpacing, 0, _frames.Count - 1);
        int i = (int)Math.Floor(k);
        if (i >= _frames.Count - 1) return _frames[^1];

        float t = (float)(k - i);
        var a = _frames[i];
        var b = _frames[i + 1];
        var forward = Vector3.Normalize(Vector3.Lerp(a.Forward, b.Forward, t));
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.Lerp(a.Up, b.Up, t)));
        var up = Vector3.Cross(right, forward);
        return new TrackFrame(Vector3d.Lerp(a.Position, b.Position, t), forward, up, right);
    }

    /// <summary>Cross-section at distance <paramref name="s"/>; blends smoothly across each piece.</summary>
    public CrossSection SectionAt(double s)
    {
        int i = FindPiece(s);
        if (i < 0) return _startSection;
        if (s >= Length) return _endSection;

        var p = _pieces[i];
        float t = (float)((s - p.StartS) / p.Piece.Length);
        return CrossSection.Lerp(p.StartSection, p.Piece.EndSection, MathUtil.SmoothStep(t));
    }

    /// <summary>Forward speed at distance <paramref name="s"/>; blends smoothly across pieces that change it.</summary>
    public float SpeedAt(double s)
    {
        int i = FindPiece(s);
        if (i < 0) return _startSpeed;
        if (s >= Length) return _endSpeed;

        var p = _pieces[i];
        float end = p.Piece.EndSpeed ?? p.StartSpeed;
        float t = MathUtil.SmoothStep((float)((s - p.StartS) / p.Piece.Length));
        return p.StartSpeed + (end - p.StartSpeed) * t;
    }

    private static TrackFrame Advance(TrackFrame f, float yawRate, float pitchRate, float ds)
    {
        var q = Quaternion.CreateFromAxisAngle(f.Up, yawRate * ds) *
                Quaternion.CreateFromAxisAngle(f.Right, pitchRate * ds);
        var forward = Vector3.Normalize(Vector3.Transform(f.Forward, q));
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.Transform(f.Up, q)));
        var up = Vector3.Cross(right, forward);
        // Step along the average heading for a better arc.
        var heading = Vector3.Normalize(f.Forward + forward);
        return new TrackFrame(f.Position + heading * ds, forward, up, right);
    }

    // Index of the last piece starting at or before s, or -1.
    private int FindPiece(double s)
    {
        int lo = 0, hi = _pieces.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_pieces[mid].StartS <= s) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }

    private readonly record struct PlacedPiece(double StartS, TrackPiece Piece, CrossSection StartSection, float StartSpeed);
}

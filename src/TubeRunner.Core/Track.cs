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
    public const int MaxBranches = 4;

    /// <summary>
    /// Half-width of the funnel flat planes open out through and close back through. Far enough out
    /// that the planes part and seal beyond the distance fade, so the void past them is never seen.
    /// Keep a theme's fadeEnd below this.
    /// </summary>
    public const float FunnelHalfWidth = 420f;

    // Fraction of a funnel piece spent parting or sealing the planes, at the funnel's full width.
    private const float SealFraction = 0.15f;

    private readonly List<TrackFrame> _frames = new();
    private readonly List<PlacedPiece> _pieces = new();
    private readonly List<TrackSplit> _splits = new();
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

    /// <summary>Splits in track order.</summary>
    public IReadOnlyList<TrackSplit> Splits => _splits;

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

    /// <summary>
    /// Appends a piece where the track forks into branch tubes that merge again at its end. The piece
    /// keeps the current section, which is the chamber the branches open out of and back into.
    /// </summary>
    /// <param name="branchSection">Cross-section for every branch that doesn't give its own.</param>
    /// <param name="branches">Per branch, its offsets from the centerline, from 0 to the piece length.</param>
    /// <param name="branchSections">Per branch, its own cross-section; defaults to <paramref name="branchSection"/>.</param>
    public TrackSplit AppendSplit(TrackPiece piece, CrossSection branchSection,
        IReadOnlyList<IReadOnlyList<OffsetKey>> branches, IReadOnlyList<CrossSection>? branchSections = null)
    {
        var chamber = _endSection;
        if (piece.EndSection != chamber)
        {
            throw new ArgumentException("A split keeps the current section; change it before or after the split.");
        }
        if (!chamber.IsClosed || !branchSection.IsClosed)
        {
            throw new ArgumentException("Splits need closed tubes, not flat planes.");
        }
        // A fork is a wall with an opening per branch, worked out against one wall closing round the
        // middle. A core sits in that middle, so there is nothing sensible to cut the openings from.
        if (chamber.IsAnnulus || branchSection.IsAnnulus)
        {
            throw new ArgumentException("Splits cannot be built in a ring; the core is in the way of the fork.");
        }
        if (branches.Count < 2 || branches.Count > MaxBranches)
        {
            throw new ArgumentException($"A split needs 2 to {MaxBranches} branches.");
        }
        foreach (var keys in branches)
        {
            if (keys.Count < 2 || keys[0].Along != 0f || MathF.Abs(keys[^1].Along - piece.Length) > 1e-3f)
            {
                throw new ArgumentException("Each branch's offsets must start at 0 and end at the split's length.");
            }
            for (int i = 1; i < keys.Count; i++)
            {
                if (keys[i].Along <= keys[i - 1].Along) throw new ArgumentException("Branch offsets must increase along the split.");
            }
        }
        IReadOnlyList<CrossSection> sections = branchSections ?? Enumerable.Repeat(branchSection, branches.Count).ToArray();
        if (sections.Count != branches.Count) throw new ArgumentException("A split needs one section per branch.");
        for (int b = 0; b < sections.Count; b++)
        {
            if (!sections[b].IsClosed) throw new ArgumentException($"Branch {b} must be a closed tube, not flat planes.");
        }

        CheckOpenings(chamber, sections, branches.Select(k => k[0].Offset).ToList(), "fork");
        CheckOpenings(chamber, sections, branches.Select(k => k[^1].Offset).ToList(), "merge");

        var split = new TrackSplit(Length, piece.Length, sections, branches);
        Append(piece);
        // The branches follow centerline frames, so they can only be measured once the piece is in.
        split.Measure(this);
        _splits.Add(split);
        return split;
    }

    /// <summary>The split containing <paramref name="s"/>, if any.</summary>
    public TrackSplit? SplitAt(double s)
    {
        foreach (var split in _splits)
        {
            if (split.Contains(s)) return split;
        }
        return null;
    }

    /// <summary>
    /// Moves a surface position along the track to <paramref name="s"/>. At a fork it enters the
    /// branch whose opening it is in front of; at a merge it comes back out onto the main track.
    /// </summary>
    public TrackPosition MoveTo(TrackPosition p, double s)
    {
        var from = p.Branch >= 0 ? SplitAt(p.S) : null;
        var to = SplitAt(s);
        if (from is not null && from != to) p = Merge(p, from);
        if (p.Branch < 0 && to is not null) p = Fork(p, to);
        return p with { S = s };
    }

    /// <summary>Frame at <paramref name="s"/> on a branch inside a split, or on the centerline.</summary>
    public TrackFrame FrameAt(double s, int branch) =>
        branch >= 0 && SplitAt(s) is { } split ? split.BranchFrame(this, s, branch) : FrameAt(s);

    /// <summary>
    /// Forward speed at <paramref name="s"/> on a branch. The ship flies at the same speed through
    /// space whichever branch it takes, but a branch that cuts the corner has less ground to cover,
    /// so it crosses the split's span sooner. That is what makes a branch the quick way round.
    /// </summary>
    public float SpeedAt(double s, int branch) =>
        SpeedAt(s) / (branch >= 0 && SplitAt(s) is { } split ? split.PathScale(branch) : 1f);

    /// <summary>Cross-section at <paramref name="s"/> on a branch inside a split, or on the main track.</summary>
    public CrossSection SectionAt(double s, int branch) =>
        branch >= 0 && SplitAt(s) is { } split ? split.Section(branch) : SectionAt(s);

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
        if (IsRejoining(p)) return Funnel(p.StartSection, p.Piece.EndSection, t);
        // Opening into flat planes is the same funnel, run backwards.
        if (IsOpening(p)) return Funnel(p.Piece.EndSection, p.StartSection, 1f - t);
        return CrossSection.Lerp(p.StartSection, p.Piece.EndSection, MathUtil.SmoothStep(t));
    }

    /// <summary>Whether <paramref name="s"/> is in a piece where flat planes close back into a tube.</summary>
    public bool IsRejoining(double s)
    {
        int i = FindPiece(s);
        return i >= 0 && s < Length && IsRejoining(_pieces[i]);
    }

    private static bool IsRejoining(PlacedPiece p) => !p.StartSection.IsClosed && p.Piece.EndSection.IsClosed;

    private static bool IsOpening(PlacedPiece p) => p.StartSection.IsClosed && !p.Piece.EndSection.IsClosed;

    // Planes rejoining a tube: first the far edges of the floor and ceiling roll up into walls out
    // beyond the fade, sealing off the void; then the funnel narrows to the tube, quickly while its
    // walls are far away and easing off as they close in. Opening runs the same shape backwards.
    private static CrossSection Funnel(CrossSection open, CrossSection tube, float t)
    {
        if (t < SealFraction)
        {
            // Widen to the funnel's full size first, which doesn't show while the planes are open,
            // and only then seal them — out at that width, well beyond the fade.
            float u = t / SealFraction;
            float widen = MathUtil.SmoothStep(Math.Min(1f, u * 2f));
            float seal = MathUtil.SmoothStep(Math.Max(0f, u * 2f - 1f));
            return new CrossSection(
                open.HalfWidth + (FunnelHalfWidth - open.HalfWidth) * widen,
                open.HalfHeight,
                open.Squareness + (1f - open.Squareness) * widen,
                open.Opening * (1f - seal));
        }

        float v = (t - SealFraction) / (1f - SealFraction);
        float k = (1f - v) * (1f - v) * (1f - v);
        return new CrossSection(
            tube.HalfWidth + (FunnelHalfWidth - tube.HalfWidth) * k,
            tube.HalfHeight + (open.HalfHeight - tube.HalfHeight) * k,
            1f + (tube.Squareness - 1f) * MathUtil.SmoothStep(v));
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

    private TrackPosition Fork(TrackPosition p, TrackSplit split)
    {
        var point = new ProfileShape(SectionAt(split.StartS)).PointAt(p.Surface, p.X);
        int best = 0;
        for (int b = 1; b < split.BranchCount; b++)
        {
            if (Vector2.Distance(point, split.OffsetAt(b, 0f)) < Vector2.Distance(point, split.OffsetAt(best, 0f))) best = b;
        }
        var (surface, x) = new ProfileShape(split.Section(best)).Nearest(point - split.OffsetAt(best, 0f));
        return new TrackPosition(p.S, surface, x, best);
    }

    private TrackPosition Merge(TrackPosition p, TrackSplit split)
    {
        var point = new ProfileShape(split.Section(p.Branch)).PointAt(p.Surface, p.X) + split.OffsetAt(p.Branch, split.Length);
        var (surface, x) = new ProfileShape(SectionAt(split.EndS)).Nearest(point);
        return new TrackPosition(p.S, surface, x);
    }

    // Branch openings must fit inside the chamber without overlapping each other.
    private static void CheckOpenings(CrossSection chamber, IReadOnlyList<CrossSection> sections, List<Vector2> centers, string where)
    {
        const int samples = 64;
        for (int i = 0; i < centers.Count; i++)
        {
            var outline = new ProfileShape(sections[i]);
            for (int k = 0; k < samples; k++)
            {
                var p = outline.PointAt(Surface.Floor, outline.Perimeter * k / samples) + centers[i];
                if (!chamber.Contains(p))
                {
                    throw new ArgumentException($"Branch {i}'s opening at the {where} doesn't fit inside the section it splits from.");
                }
                for (int j = 0; j < centers.Count; j++)
                {
                    if (j != i && sections[j].Contains(p - centers[j], tolerance: -1e-3f))
                    {
                        throw new ArgumentException($"Branch openings at the {where} overlap.");
                    }
                }
            }
        }
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

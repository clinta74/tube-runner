using System.Numerics;

namespace TubeRunner.Core;

/// <summary>A branch's offset from the track centerline at some distance into its split.</summary>
/// <param name="Along">Distance from the start of the split.</param>
/// <param name="Offset">Offset in the centerline's cross-section plane (x right, y up).</param>
public readonly record struct OffsetKey(float Along, Vector2 Offset);

/// <summary>
/// A stretch where the track forks into branch tubes that merge again at the end. The centerline
/// continues through the split, and each branch follows it at an offset that blends between keys.
/// The track's own cross-section there is the chamber the branches fork out of and merge into.
/// </summary>
public sealed class TrackSplit
{
    private readonly IReadOnlyList<IReadOnlyList<OffsetKey>> _branches;
    private readonly IReadOnlyList<float> _speeds;

    internal TrackSplit(double startS, float length, CrossSection section,
        IReadOnlyList<IReadOnlyList<OffsetKey>> branches, IReadOnlyList<float> speeds)
    {
        StartS = startS;
        Length = length;
        Section = section;
        _branches = branches;
        _speeds = speeds;
    }

    public double StartS { get; }
    public float Length { get; }
    public double EndS => StartS + Length;

    /// <summary>Cross-section of every branch tube.</summary>
    public CrossSection Section { get; }

    public int BranchCount => _branches.Count;

    /// <summary>
    /// How fast a branch runs compared to the track's speed: 1 is the same, 1.2 is a fifth faster.
    /// A quicker branch is how a split offers a risky shortcut against a calmer route.
    /// </summary>
    public float SpeedFactor(int branch) => _speeds[branch];

    public bool Contains(double s) => s >= StartS && s < EndS;

    /// <summary>
    /// Offset of a branch at <paramref name="along"/> into the split, blending smoothly between keys.
    /// Before the first key and after the last it holds that key's offset.
    /// </summary>
    public Vector2 OffsetAt(int branch, float along)
    {
        var keys = _branches[branch];
        if (along <= keys[0].Along) return keys[0].Offset;
        for (int i = 1; i < keys.Count; i++)
        {
            if (along > keys[i].Along) continue;
            var a = keys[i - 1];
            var b = keys[i];
            float t = MathUtil.SmoothStep((along - a.Along) / (b.Along - a.Along));
            return Vector2.Lerp(a.Offset, b.Offset, t);
        }
        return keys[^1].Offset;
    }

    /// <summary>
    /// Frame of a branch's centerline. Outside the split it runs parallel to the track at the end
    /// offset, so a camera following the branch past the fork or merge moves smoothly.
    /// </summary>
    public TrackFrame BranchFrame(Track track, double s, int branch)
    {
        const double h = 0.5;
        var center = BranchPoint(track, s, branch);
        var forward = Vector3.Normalize((BranchPoint(track, s + h, branch) - BranchPoint(track, s - h, branch)).ToVector3());
        var right = Vector3.Normalize(Vector3.Cross(forward, track.FrameAt(s).Up));
        var up = Vector3.Cross(right, forward);
        return new TrackFrame(center, forward, up, right);
    }

    private Vector3d BranchPoint(Track track, double s, int branch) =>
        track.FrameAt(s).PointOnSection(OffsetAt(branch, (float)(s - StartS)));
}

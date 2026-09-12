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
///
/// Branches differ in two ways. Each has its own cross-section, so one can be the tighter tube. And
/// each covers its own real distance between the fork and the merge: a branch that cuts the inside
/// of a turn travels less ground and so arrives sooner, at the same speed through space. That, and
/// not a speed bonus, is what makes one route the quick way round.
/// </summary>
public sealed class TrackSplit
{
    // Enough samples that a branch's measured length settles well inside a unit.
    private const int MeasureSamples = 256;

    private readonly IReadOnlyList<IReadOnlyList<OffsetKey>> _branches;
    private readonly IReadOnlyList<CrossSection> _sections;
    private readonly float[] _pathLengths;

    internal TrackSplit(double startS, float length, IReadOnlyList<CrossSection> sections,
        IReadOnlyList<IReadOnlyList<OffsetKey>> branches)
    {
        StartS = startS;
        Length = length;
        _sections = sections;
        _branches = branches;
        // Until measured, every branch is assumed to run the length of the split.
        _pathLengths = new float[branches.Count];
        Array.Fill(_pathLengths, length);
    }

    public double StartS { get; }
    public float Length { get; }
    public double EndS => StartS + Length;

    public int BranchCount => _branches.Count;

    /// <summary>Cross-section of one branch's tube; branches need not match.</summary>
    public CrossSection Section(int branch) => _sections[branch];

    /// <summary>How far a branch really travels between the fork and the merge.</summary>
    public float PathLength(int branch) => _pathLengths[branch];

    /// <summary>
    /// A branch's real path length as a fraction of the split's span. Below 1 means it cuts the
    /// corner, so it crosses the split in less time without flying any faster through space.
    /// </summary>
    public float PathScale(int branch) => _pathLengths[branch] / Length;

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

    /// <summary>
    /// Measures each branch's real path. Called once the split's piece has been appended, since a
    /// branch follows centerline frames that do not exist until then.
    /// </summary>
    internal void Measure(Track track)
    {
        for (int b = 0; b < BranchCount; b++)
        {
            double total = 0;
            var previous = BranchPoint(track, StartS, b);
            for (int i = 1; i <= MeasureSamples; i++)
            {
                var point = BranchPoint(track, StartS + Length * ((double)i / MeasureSamples), b);
                total += (point - previous).ToVector3().Length();
                previous = point;
            }
            _pathLengths[b] = (float)total;
        }
    }

    private Vector3d BranchPoint(Track track, double s, int branch) =>
        track.FrameAt(s).PointOnSection(OffsetAt(branch, (float)(s - StartS)));
}

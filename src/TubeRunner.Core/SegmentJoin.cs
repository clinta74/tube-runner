namespace TubeRunner.Core;

/// <summary>
/// The sums behind handing the ship from one track to another without anything on screen changing.
///
/// A wall is drawn a segment at a time, and each segment picks its colours, its checker size and its
/// twist from its own index along the track. Two tracks with the same section and theme therefore
/// still look different wherever their indices differ, and swapping one for the other mid-flight
/// repaints every wall in sight. Two things make the swap invisible instead: moving the ship a whole
/// number of segments, so the seams stay where they were, and shifting the indices the walls are
/// drawn with by the same number, so each segment picks what the one it replaces had picked.
///
/// The title screen leans on this twice. Its tube is the first level with a straight lead-in, and
/// the ship holds station in that lead-in by being put back a segment every time it has flown one,
/// which nobody can see. Start stops putting it back; when it reaches the level it is handed to the
/// level proper, a whole number of segments behind where it was, which nobody can see either.
/// </summary>
public static class SegmentJoin
{
    /// <summary>
    /// How many segments to shift the new position's indices by, so that the segment holding
    /// <paramref name="toS"/> looks like the one holding <paramref name="fromS"/> did.
    /// </summary>
    public static int IndexOffset(double fromS, double toS, double segmentLength) =>
        (int)Math.Floor(fromS / segmentLength) - (int)Math.Floor(toS / segmentLength);

    /// <summary>
    /// How long a lead-in has to be for a ship holding station in it to see nothing of the level
    /// beyond. The ship is kept between <paramref name="hold"/> and one segment past it, and being
    /// put back a segment moves everything ahead a segment further off - which is invisible only if
    /// everything that is not plain straight tube is already out of sight. A whole number of
    /// segments, so that the level's own seams fall where the lead-in's would have.
    /// </summary>
    /// <param name="view">How far ahead a wall can be seen.</param>
    /// <param name="clear">How far into the level the tube stays straight, empty and unchanged.</param>
    public static double LeadIn(double segmentLength, double hold, double view, double clear)
    {
        double needed = Math.Max(0.0, hold + segmentLength + view - clear);
        return Math.Ceiling(needed / segmentLength) * segmentLength;
    }
}

using System.Numerics;

namespace TubeRunner.Core;

/// <summary>
/// The sums behind one level running into the next with nothing on screen changing.
///
/// A level's last <see cref="Handover"/> units are a copy of the next level's first: the same
/// sections, the same bends, nothing in them. The run finishes 16 units into that copy and the
/// next level takes over there, which is why the ship can be put down in it without the world
/// snapping. The copy is also what lets the next level be drawn ahead of time - its own opening
/// standing in for the copy, in its own colours - because the two are the same shape, and one rigid
/// move maps every point of the one onto the other.
/// </summary>
public static class LevelJoin
{
    /// <summary>
    /// How much of the end of a level is a copy of the next level's start: the 16 units the next
    /// level puts the ship in at, plus the 260 of run-out after the finish.
    /// </summary>
    public const double Handover = 276.0;

    /// <summary>
    /// The move that lays <paramref name="starting"/>'s opening over the end of
    /// <paramref name="ending"/>: what to do to a point on the next level's track to find where it
    /// sits in the world of the level being flown.
    /// </summary>
    public static RigidMap Alignment(Track ending, Track starting) =>
        RigidMap.Between(starting.FrameAt(0), ending.FrameAt(ending.Length - Handover));
}

/// <summary>
/// A rotation and a translation: the one that carries the frame it was built <c>from</c> onto the
/// frame it was built <c>to</c>. Held as the three columns of the rotation, which are where the
/// unit axes land.
/// </summary>
public readonly record struct RigidMap(Vector3 X, Vector3 Y, Vector3 Z, Vector3d From, Vector3d To)
{
    public static RigidMap Between(TrackFrame from, TrackFrame to)
    {
        // R = A Bᵀ, with the frames' axes as the columns of A and B, so that R maps each of B's
        // axes onto the matching axis of A.
        Vector3 Column(float rx, float ux, float fx) => to.Right * rx + to.Up * ux + to.Forward * fx;
        return new RigidMap(
            Column(from.Right.X, from.Up.X, from.Forward.X),
            Column(from.Right.Y, from.Up.Y, from.Forward.Y),
            Column(from.Right.Z, from.Up.Z, from.Forward.Z),
            from.Position, to.Position);
    }

    public Vector3 MapDirection(Vector3 v) => X * v.X + Y * v.Y + Z * v.Z;

    public Vector3 UnmapDirection(Vector3 v) => new(Vector3.Dot(X, v), Vector3.Dot(Y, v), Vector3.Dot(Z, v));

    /// <summary>A point in the space the map is from, in the space it is to. Kept in doubles the whole way.</summary>
    public Vector3d Map(Vector3d p)
    {
        var d = p - From;
        return To + new Vector3d(
            X.X * d.X + Y.X * d.Y + Z.X * d.Z,
            X.Y * d.X + Y.Y * d.Y + Z.Y * d.Z,
            X.Z * d.X + Y.Z * d.Y + Z.Z * d.Z);
    }

    /// <summary>The inverse of <see cref="Map"/>.</summary>
    public Vector3d Unmap(Vector3d p)
    {
        var d = p - To;
        return From + new Vector3d(
            X.X * d.X + X.Y * d.Y + X.Z * d.Z,
            Y.X * d.X + Y.Y * d.Y + Y.Z * d.Z,
            Z.X * d.X + Z.Y * d.Y + Z.Z * d.Z);
    }
}

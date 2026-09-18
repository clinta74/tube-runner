namespace TubeRunner.Core;

/// <summary>A stretch of track where the ship can cross between floor and ceiling.</summary>
public readonly record struct JumpWindow(double From, double To);

/// <summary>
/// Finds where on a track jumping becomes possible and where it stops. A jump needs somewhere to
/// jump to: a fully unrolled section, or a ring, where the core overhead is the other surface, and a track eases into and out of one over a whole piece, so the boundary is
/// somewhere in the middle of a blend rather than at a piece edge. The renderer marks these on the
/// wall so the player can see the window coming instead of testing the button against it.
/// </summary>
public static class JumpWindows
{
    // Coarse enough to be cheap on a 6000-unit track, fine enough that no piece is stepped over.
    private const double Step = 2.0;
    private const int Refinements = 14;

    /// <summary>Whether a section has a surface overhead to cross to.</summary>
    public static bool Jumpable(ProfileShape shape) => shape.Unroll >= 1f || shape.IsAnnulus;

    public static List<JumpWindow> Find(Track track)
    {
        var shapes = new ProfileShapeCache();
        bool Open(double s) => Jumpable(shapes.Get(track.SectionAt(s)));

        var windows = new List<JumpWindow>();
        bool was = Open(0);
        double from = was ? 0 : -1;

        for (double s = Step; s <= track.Length; s += Step)
        {
            bool now = Open(s);
            if (now == was) continue;

            double edge = Crossing(s - Step, s, Open);
            if (now)
            {
                from = edge;
            }
            else
            {
                windows.Add(new JumpWindow(from, edge));
                from = -1;
            }
            was = now;
        }

        if (was && from >= 0) windows.Add(new JumpWindow(from, track.Length));
        return windows;
    }

    // The boundary sits inside a blend, so it is found by halving rather than reported at the
    // sample that noticed it - a mark two units off is a mark in the wrong place.
    private static double Crossing(double lo, double hi, Func<double, bool> open)
    {
        bool atLo = open(lo);
        for (int i = 0; i < Refinements; i++)
        {
            double mid = (lo + hi) * 0.5;
            if (open(mid) == atLo) lo = mid;
            else hi = mid;
        }
        return (lo + hi) * 0.5;
    }
}

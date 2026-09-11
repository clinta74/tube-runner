namespace TubeRunner.Core;

internal static class MathUtil
{
    /// <summary>Wraps a value into [0, 1).</summary>
    public static float Wrap01(float v)
    {
        v -= MathF.Floor(v);
        // Tiny negative inputs can round up to exactly 1.
        return v >= 1f ? 0f : v;
    }

    public static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

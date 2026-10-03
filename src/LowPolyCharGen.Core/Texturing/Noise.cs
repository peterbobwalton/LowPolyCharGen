using System.Numerics;

namespace LowPolyCharGen.Texturing;

/// <summary>Deterministic value noise in three dimensions. Sampling by model position keeps patterns seamless across UV charts.</summary>
internal static class Noise
{
    private static float Hash(int x, int y, int z)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + z * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    /// <summary>Smooth noise in 0..1 with features about one unit across.</summary>
    public static float Value(Vector3 p)
    {
        var xi = (int)MathF.Floor(p.X);
        var yi = (int)MathF.Floor(p.Y);
        var zi = (int)MathF.Floor(p.Z);
        var fx = p.X - xi;
        var fy = p.Y - yi;
        var fz = p.Z - zi;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        fz = fz * fz * (3 - 2 * fz);

        float Lerp(float a, float b, float t) => a + (b - a) * t;
        var x00 = Lerp(Hash(xi, yi, zi), Hash(xi + 1, yi, zi), fx);
        var x10 = Lerp(Hash(xi, yi + 1, zi), Hash(xi + 1, yi + 1, zi), fx);
        var x01 = Lerp(Hash(xi, yi, zi + 1), Hash(xi + 1, yi, zi + 1), fx);
        var x11 = Lerp(Hash(xi, yi + 1, zi + 1), Hash(xi + 1, yi + 1, zi + 1), fx);
        return Lerp(Lerp(x00, x10, fy), Lerp(x01, x11, fy), fz);
    }

    /// <summary>Fractal noise in 0..1: several octaves of <see cref="Value"/>.</summary>
    public static float Fbm(Vector3 p, int octaves = 3)
    {
        float sum = 0, amplitude = 0.5f, total = 0;
        for (var i = 0; i < octaves; i++)
        {
            sum += Value(p) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            p = p * 2.03f + new Vector3(17.1f, 9.2f, 31.7f);
        }
        return sum / total;
    }

    /// <summary>One random value in 0..1 per unit cell.</summary>
    public static float Cell(Vector3 p) => Hash((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
}

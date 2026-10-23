using System.Numerics;

namespace LowPolyCharGen.Kit;

/// <summary>
/// Reshapes the pack's face for gender and head shape. Every pack head shares the same face (only
/// the headgear differs), so the change is a smooth field over position, measured on that face: it
/// moves helmets' chin straps and collars along with the jaw and leaves the skull (and the helmets
/// sitting on it) alone. Positions are on the pack skeleton (cm, -Y = front).
/// </summary>
public static class HeadMorph
{
    private static float S(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    public static bool Changes(Gender gender, HeadShape shape) => gender == Gender.Female || shape != HeadShape.Standard;

    public static Vector3 Apply(Vector3 p, Gender gender, HeadShape shape)
    {
        if (p.Z < 145f) return p;
        float x = p.X, y = p.Y, z = p.Z;
        var face = S((-y - 6f) / 5f);                                   // the front of the head
        var jaw = S((156f - z) / 5f) * S((z - 147.5f) / 2f) * face;     // jaw and chin, not the neck below
        var cheeks = face * S((z - 150f) / 4f) * S((172f - z) / 4f);
        var nose = S((3.6f - MathF.Abs(x)) / 1.2f) * S((z - 158.3f) / 0.8f) * S((169.5f - z) / 1f) * S((-y - 15.2f) / 1f);
        var chin = S((153.5f - z) / 3f) * S((z - 147.5f) / 2f);
        var brow = S((z - 166.5f) / 1f) * S((171f - z) / 1.5f) * S((-y - 13.5f) / 2f);

        float jawWidth = 0, faceWidth = 0, chinUp = 0, chinBack = 0, noseNarrow = 0, noseBack = 0, browBack = 0;
        switch (shape)
        {
            case HeadShape.Square: jawWidth += 0.1f; chinBack -= 0.4f; break;
            case HeadShape.Slim: faceWidth -= 0.08f; jawWidth -= 0.08f; break;
            case HeadShape.Round: faceWidth += 0.06f; jawWidth -= 0.06f; chinUp += 0.5f; break;
            case HeadShape.Heavy: jawWidth += 0.14f; faceWidth += 0.06f; chinUp -= 0.6f; chinBack -= 0.3f; break;
        }
        if (gender == Gender.Female)
        {
            jawWidth -= 0.26f; faceWidth -= 0.05f; chinUp += 1.1f; chinBack += 0.9f;
            noseNarrow += 0.3f; noseBack += 1f; browBack += 0.6f;
        }

        x *= 1 + jawWidth * jaw;
        x *= 1 + faceWidth * cheeks;
        x -= p.X * noseNarrow * nose;
        z += chinUp * chin * face;
        y += chinBack * chin * S((-y - 12f) / 4f);
        y += noseBack * nose * S((-p.Y - 16.8f) / 1.5f);
        y += browBack * brow;
        return new Vector3(x, y, z);
    }
}

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

    public static NoseShape NoseFor(CharacterSpec spec) =>
        spec.Nose != NoseShape.Auto ? spec.Nose : spec.Gender == Gender.Female ? NoseShape.Small : NoseShape.Pack;

    public static bool Changes(CharacterSpec spec) =>
        spec.Gender == Gender.Female || spec.HeadShape != HeadShape.Standard || NoseFor(spec) != NoseShape.Pack;

    // The pack face's nose vertices (one side, x >= 0): brow centre, bridge side, tip, wing, wing
    // base, underside. Every pack head has them at exactly these places.
    private static readonly Vector3[] NosePoints =
    [
        new(0f, -17.44f, 168.85f), new(1.9f, -16.79f, 168.21f), new(0f, -19.4f, 160.88f),
        new(2.23f, -18.12f, 160.86f), new(3.12f, -16.04f, 160.36f), new(0f, -17.14f, 159.98f),
    ];

    /// <summary>Where each nose point goes (same order; x as the new |x|).</summary>
    private static Vector3[]? NoseTargets(NoseShape nose) => nose switch
    {
        NoseShape.Small =>
        [
            new(0f, -17.0f, 168.7f), new(1.3f, -16.6f, 168.2f), new(0f, -18.5f, 161.4f),
            new(1.45f, -17.6f, 161.3f), new(2.3f, -16.04f, 160.8f), new(0f, -17.0f, 160.5f),
        ],
        NoseShape.Button =>
        [
            new(0f, -17.0f, 168.6f), new(1.3f, -16.6f, 168.2f), new(0f, -18.2f, 161.9f),
            new(1.6f, -17.6f, 161.4f), new(2.4f, -16.04f, 160.9f), new(0f, -17.1f, 160.9f),
        ],
        NoseShape.Broad =>
        [
            new(0f, -17.44f, 168.85f), new(2.2f, -16.79f, 168.21f), new(0f, -19.6f, 160.7f),
            new(2.9f, -18.2f, 160.7f), new(3.7f, -16.04f, 160.2f), new(0f, -17.3f, 159.9f),
        ],
        NoseShape.Roman =>
        [
            new(0f, -17.9f, 168.85f), new(1.8f, -17.2f, 168.21f), new(0f, -19.7f, 160.4f),
            new(2.1f, -18.2f, 160.6f), new(3.0f, -16.04f, 160.3f), new(0f, -17.2f, 159.7f),
        ],
        _ => null,
    };

    public static Vector3 Apply(Vector3 p, CharacterSpec spec)
    {
        if (p.Z < 145f) return p;
        var gender = spec.Gender;
        var shape = spec.HeadShape;
        if (NoseTargets(NoseFor(spec)) is { } targets)
        {
            var side = p.X < 0 ? -1f : 1f;
            var q = p with { X = MathF.Abs(p.X) };
            for (var i = 0; i < NosePoints.Length; i++)
                if (Vector3.DistanceSquared(q, NosePoints[i]) < 0.0025f)
                {
                    p = targets[i] with { X = targets[i].X * side };
                    break;
                }
        }
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
            browBack += 0.6f;
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

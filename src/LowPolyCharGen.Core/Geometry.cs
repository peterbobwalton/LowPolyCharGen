using System.Numerics;

namespace LowPolyCharGen;

/// <summary>
/// What a face is made of. The texture painter decides the colour of every texel from the slot and
/// the texel's position on the character (see <c>Texturing.Painter</c>).
/// </summary>
public enum Slot : byte
{
    // Body surfaces, painted by position: clothing edges, facial features, laces and so on.
    Head, Ear, Neck, Body, Arm, Hand, Leg, Foot,

    // Materials with one base colour and generic surface detail.
    Skin, Hair, Dark, Lens,
    Shirt, ShirtTrim, Trousers, TrousersTrim,
    Boots, Accent, Gear, GearTrim,
    Belt, Hat, HatTrim, Metal,
    Scarf,
}

/// <summary>Extra detail the painter draws on a primitive, in the primitive's own UV space.</summary>
public enum PaintDetail : byte
{
    None,
    /// <summary>Darkened border, as on a stitched panel.</summary>
    Panel,
    /// <summary>Pouch with a flap and a snap.</summary>
    Pouch,
    /// <summary>Rows of webbing.</summary>
    Webbing,
    /// <summary>Strap with stitched edges.</summary>
    Strap,
    /// <summary>Knitted ribbing.</summary>
    Ribbed,
}

/// <summary>Either one slot for a whole primitive or one slot per loft segment.</summary>
public readonly struct SlotSpec
{
    private readonly Slot _single;
    private readonly Slot[]? _perSegment;

    private SlotSpec(Slot single, Slot[]? perSegment) { _single = single; _perSegment = perSegment; }

    public Slot this[int segment] =>
        _perSegment is null ? _single : _perSegment[Math.Clamp(segment, 0, _perSegment.Length - 1)];

    public static implicit operator SlotSpec(Slot slot) => new(slot, null);
    public static implicit operator SlotSpec(Slot[] slots) => new(default, slots);
    public static implicit operator SlotSpec(List<Slot> slots) => new(default, slots.ToArray());
}

/// <summary>
/// A cross-section: a superellipse in the plane spanned by U and V. Exp = 2 is an ellipse, larger
/// values are boxier. The V radius can differ on the +V and -V side (e.g. chest vs. back).
/// </summary>
public readonly record struct Ring(Vector3 Center, Vector3 U, Vector3 V, float Ru, float RvPos, float RvNeg, float Exp)
{
    /// <summary>Ring with the same V radius on both sides.</summary>
    public Ring(Vector3 center, Vector3 u, Vector3 v, float ru, float rv, float exp)
        : this(center, u, v, ru, rv, rv, exp) { }

    public Vector3 Point(float angle)
    {
        var (s, c) = MathF.SinCos(angle);
        var pu = Geo.SuperPow(c, Exp) * Ru;
        var pv = Geo.SuperPow(s, Exp) * (s >= 0 ? RvPos : RvNeg);
        return Center + U * pu + V * pv;
    }

    public Vector3[] Points(int sides, float angleOffset)
    {
        var pts = new Vector3[sides];
        for (var i = 0; i < sides; i++)
            pts[i] = Point(angleOffset + MathF.Tau * i / sides);
        return pts;
    }
}

/// <summary>Coordinate helpers. Model space is +X = character left, -Y = front, +Z = up.</summary>
public static class Geo
{
    public static readonly Vector3 Left = Vector3.UnitX;
    public static readonly Vector3 Back = Vector3.UnitY;
    public static readonly Vector3 Front = -Vector3.UnitY;
    public static readonly Vector3 Up = Vector3.UnitZ;

    /// <summary>Point from semantic coordinates: distance to the left, to the front and up.</summary>
    public static Vector3 P(float left, float front, float up) => new(left, -front, up);

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public static float Saturate(float v) => Math.Clamp(v, 0f, 1f);

    public static float SmoothStep(float t)
    {
        t = Saturate(t);
        return t * t * (3 - 2 * t);
    }

    /// <summary>0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>, smooth in between.</summary>
    public static float SmoothStep(float edge0, float edge1, float x) => SmoothStep((x - edge0) / (edge1 - edge0));

    /// <summary>sign(v) * |v|^(2/exp): maps a circle coordinate onto a superellipse.</summary>
    public static float SuperPow(float v, float exp) =>
        exp == 2f ? v : MathF.Sign(v) * MathF.Pow(MathF.Abs(v), 2f / exp);

    public static float Deg(float degrees) => degrees * MathF.PI / 180f;

    /// <summary>Rotation that maps the local X, Y, Z axes onto the given orthonormal, right-handed basis.</summary>
    public static Quaternion Basis(Vector3 x, Vector3 y, Vector3 z)
    {
        var m = new Matrix4x4(
            x.X, x.Y, x.Z, 0,
            y.X, y.Y, y.Z, 0,
            z.X, z.Y, z.Z, 0,
            0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    /// <summary>Rotation whose local Z axis points along <paramref name="z"/> and whose X axis is as close to <paramref name="xHint"/> as possible.</summary>
    public static Quaternion LookAlong(Vector3 z, Vector3 xHint)
    {
        z = Vector3.Normalize(z);
        var y = Vector3.Cross(z, xHint);
        if (y.LengthSquared() < 1e-8f) y = Vector3.Cross(z, MathF.Abs(z.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX);
        y = Vector3.Normalize(y);
        var x = Vector3.Cross(y, z);
        return Basis(x, y, z);
    }
}

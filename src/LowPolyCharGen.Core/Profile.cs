using System.Numerics;

namespace LowPolyCharGen;

/// <summary>
/// A body volume described by horizontal superellipse sections stacked along Z (torso, hips, head).
/// Besides being lofted into a mesh it can be queried, so clothing and gear can sit on its surface.
/// </summary>
public sealed class Profile
{
    /// <param name="Z">Height of the section.</param>
    /// <param name="CenterY">Model-space Y of the section centre (negative = towards the front).</param>
    /// <param name="HalfWidth">Half extent along X.</param>
    /// <param name="Front">Extent from the centre towards the front (-Y).</param>
    /// <param name="Back">Extent from the centre towards the back (+Y).</param>
    public readonly record struct Section(float Z, float CenterY, float HalfWidth, float Front, float Back, float Exp);

    private readonly List<Section> _sections = [];

    public IReadOnlyList<Section> Sections => _sections;
    public float ZMin => _sections[0].Z;
    public float ZMax => _sections[^1].Z;

    /// <summary>Adds a section; <paramref name="centerFront"/> is how far forward of the origin its centre is.</summary>
    public Profile Add(float z, float centerFront, float halfWidth, float front, float back, float exp)
    {
        _sections.Add(new Section(z, -centerFront, halfWidth, front, back, exp));
        _sections.Sort((a, b) => a.Z.CompareTo(b.Z));
        return this;
    }

    public Section At(float z)
    {
        if (z <= ZMin) return _sections[0] with { Z = z };
        if (z >= ZMax) return _sections[^1] with { Z = z };
        for (var i = 0; i + 1 < _sections.Count; i++)
        {
            var a = _sections[i];
            var b = _sections[i + 1];
            if (z > b.Z) continue;
            var t = (z - a.Z) / (b.Z - a.Z);
            return new Section(z, Geo.Lerp(a.CenterY, b.CenterY, t), Geo.Lerp(a.HalfWidth, b.HalfWidth, t),
                Geo.Lerp(a.Front, b.Front, t), Geo.Lerp(a.Back, b.Back, t), Geo.Lerp(a.Exp, b.Exp, t));
        }
        return _sections[^1];
    }

    /// <summary>A copy grown (or shrunk) by <paramref name="distance"/> in every horizontal direction.</summary>
    public Profile Offset(float distance)
    {
        var p = new Profile();
        foreach (var s in _sections)
            p._sections.Add(s with
            {
                HalfWidth = MathF.Max(0.05f, s.HalfWidth + distance),
                Front = MathF.Max(0.05f, s.Front + distance),
                Back = MathF.Max(0.05f, s.Back + distance),
            });
        return p;
    }

    /// <summary>Cross-section at height z as a loft ring (U = left, V = back).</summary>
    public Ring RingAt(float z, float offset = 0)
    {
        var s = At(z);
        return new Ring(new Vector3(0, s.CenterY, z), Geo.Left, Geo.Back,
            s.HalfWidth + offset, s.Back + offset, s.Front + offset, s.Exp);
    }

    /// <summary>Surface point at height z; angle 0 = left, 90 degrees = back, 270 degrees = front.</summary>
    public Vector3 Surface(float z, float angle, float offset = 0) => RingAt(z, offset).Point(angle);

    private static float Depth(Section s, float x, float extent)
    {
        var u = MathF.Min(1f, MathF.Abs(x) / s.HalfWidth);
        return extent * MathF.Pow(MathF.Max(0f, 1f - MathF.Pow(u, s.Exp)), 1f / s.Exp);
    }

    /// <summary>Model-space Y of the front surface at height z and lateral position x.</summary>
    public float FrontY(float z, float x)
    {
        var s = At(z);
        return s.CenterY - Depth(s, x, s.Front);
    }

    /// <summary>Model-space Y of the back surface at height z and lateral position x.</summary>
    public float BackY(float z, float x)
    {
        var s = At(z);
        return s.CenterY + Depth(s, x, s.Back);
    }

    public bool Contains(Vector3 p)
    {
        if (p.Z < ZMin || p.Z > ZMax) return false;
        var s = At(p.Z);
        var dy = p.Y - s.CenterY;
        var depth = dy < 0 ? s.Front : s.Back;
        return MathF.Pow(MathF.Abs(p.X) / s.HalfWidth, s.Exp) + MathF.Pow(MathF.Abs(dy) / depth, s.Exp) < 1f;
    }

    /// <summary>
    /// Casts a ray from an interior point and returns where it leaves the volume, pushed
    /// <paramref name="offset"/> further along the ray. Used to wrap hair and soft hats around the head.
    /// </summary>
    public Vector3 Project(Vector3 origin, Vector3 direction, float offset = 0)
    {
        direction = Vector3.Normalize(direction);
        float lo = 0, hi = 200;
        for (var i = 0; i < 28; i++)
        {
            var mid = (lo + hi) / 2;
            if (Contains(origin + direction * mid)) lo = mid; else hi = mid;
        }
        return origin + direction * (lo + offset);
    }
}

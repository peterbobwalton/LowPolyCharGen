using System.Numerics;

namespace LowPolyCharGen.Parts;

/// <summary>
/// An arm or leg described by radius stations along one coordinate (x for arms, z for legs), plus
/// the thickness clothing adds on top. Rings for the body mesh are sampled from it, and gear asks it
/// where the surface is.
/// </summary>
internal sealed class Limb
{
    private readonly record struct Station(float T, float Ru, float RvPos, float RvNeg, float Exp);

    private readonly List<Station> _stations = [];

    public required Func<float, Vector3> Center { get; init; }
    public required Vector3 U { get; init; }
    public required Vector3 V { get; init; }
    public float Scale { get; init; } = 1f;

    /// <summary>Thickness of clothing over the skin at a coordinate.</summary>
    public Func<float, float> Clothing { get; set; } = _ => 0f;

    public Limb Add(float t, float ru, float rvPos, float rvNeg, float exp = 2.2f)
    {
        _stations.Add(new Station(t, ru * Scale, rvPos * Scale, rvNeg * Scale, exp));
        _stations.Sort((a, b) => a.T.CompareTo(b.T));
        return this;
    }

    public IEnumerable<float> Stations => _stations.Select(s => s.T);

    private Station Sample(float t)
    {
        if (t <= _stations[0].T) return _stations[0];
        if (t >= _stations[^1].T) return _stations[^1];
        for (var i = 0; i + 1 < _stations.Count; i++)
        {
            var a = _stations[i];
            var b = _stations[i + 1];
            if (t > b.T) continue;
            var k = (t - a.T) / (b.T - a.T);
            return new Station(t, Geo.Lerp(a.Ru, b.Ru, k), Geo.Lerp(a.RvPos, b.RvPos, k), Geo.Lerp(a.RvNeg, b.RvNeg, k), Geo.Lerp(a.Exp, b.Exp, k));
        }
        return _stations[^1];
    }

    /// <summary>Surface radius (including clothing) along U.</summary>
    public float RadiusU(float t) => Sample(t).Ru + Clothing(t);

    /// <summary>Surface radii (including clothing) along +V and -V.</summary>
    public (float Pos, float Neg) RadiusV(float t)
    {
        var s = Sample(t);
        var e = Clothing(t);
        return (s.RvPos + e, s.RvNeg + e);
    }

    /// <summary>Cross-section at <paramref name="t"/> with the clothing there, or with an explicit thickness.</summary>
    public Ring RingAt(float t, float? clothing = null)
    {
        var s = Sample(t);
        var e = clothing ?? Clothing(t);
        return new Ring(Center(t), U, V, s.Ru + e, s.RvPos + e, s.RvNeg + e, s.Exp);
    }
}

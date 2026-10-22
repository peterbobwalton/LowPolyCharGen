using System.Numerics;

namespace LowPolyCharGen;

/// <summary>
/// Body measurements taken from a generated mesh, and the adult reference sizes they are checked
/// against, so proportions can be tested rather than judged by eye. The references are typical
/// adult breadths and depths (survey averages, slightly leaner for a fit build) at the generator's
/// standard heights, and scale with the measured height.
/// </summary>
public static class Anthropometry
{
    /// <summary>A measurement: horizontal slice of the mesh at a fraction of the standing height.</summary>
    public readonly record struct Measure(string Name, float HeightFraction, bool Depth, bool OneLeg = false, float Tolerance = 0.08f);

    public static readonly Measure[] Measures =
    [
        new("neck breadth", 0.857f, false, Tolerance: 0.15f),
        new("chest breadth", 0.718f, false),
        new("chest depth", 0.718f, true),
        new("waist breadth", 0.613f, false),
        new("waist depth", 0.613f, true),
        new("hip breadth", 0.492f, false),
        new("hip depth", 0.492f, true),
        new("thigh breadth", 0.409f, false, OneLeg: true, Tolerance: 0.12f),
    ];

    /// <summary>Reference height (cm) and sizes for each measure, in the order of <see cref="Measures"/>.</summary>
    public static (float Height, float[] Sizes) Reference(Gender gender) => gender == Gender.Female
        ? (168f, [11.0f, 29.5f, 24.5f, 27.5f, 20.0f, 37.5f, 24.0f, 16.0f])
        : (181f, [13.0f, 33.5f, 24.5f, 30.5f, 22.0f, 35.5f, 24.0f, 16.5f]);

    /// <summary>Standing height and each of <see cref="Measures"/> for a mesh (model space, +Z up, cm).</summary>
    public static (float Height, float[] Sizes) Take(MeshData mesh)
    {
        var positions = mesh.Positions;
        var floor = positions.Min(p => p.Z);
        var height = positions.Max(p => p.Z) - floor;
        var sizes = new float[Measures.Length];
        for (var i = 0; i < Measures.Length; i++)
        {
            var m = Measures[i];
            var z = floor + m.HeightFraction * height;
            float lo = float.MaxValue, hi = float.MinValue;
            // Cut every edge of every face that crosses the slice, ignoring the arms (T-pose, so they
            // are far out to the sides) and, for one leg, everything right of the centre line.
            foreach (var face in mesh.Faces)
            {
                var v = face.Vertices;
                for (var k = 0; k < v.Length; k++)
                {
                    var a = positions[v[k]];
                    var b = positions[v[(k + 1) % v.Length]];
                    if ((a.Z - z) * (b.Z - z) > 0 || a.Z == b.Z) continue;
                    var p = Vector3.Lerp(a, b, (z - a.Z) / (b.Z - a.Z));
                    if (MathF.Abs(p.X) > 0.15f * height) continue;
                    if (m.OneLeg && p.X < 0) continue;
                    var value = m.Depth ? p.Y : p.X;
                    lo = MathF.Min(lo, value);
                    hi = MathF.Max(hi, value);
                }
            }
            sizes[i] = hi > lo ? hi - lo : 0f;
        }
        return (height, sizes);
    }

    /// <summary>Measures a mesh and compares it with the reference; the lines describe every measure.</summary>
    public static (bool Ok, List<string> Lines) Compare(MeshData mesh, Gender gender)
    {
        var (height, sizes) = Take(mesh);
        var (refHeight, refSizes) = Reference(gender);
        var scale = height / refHeight;
        var ok = true;
        var lines = new List<string> { $"height {height:F1} cm" };
        for (var i = 0; i < Measures.Length; i++)
        {
            var target = refSizes[i] * scale;
            var error = sizes[i] / target - 1f;
            var good = MathF.Abs(error) <= Measures[i].Tolerance;
            ok &= good;
            lines.Add($"{Measures[i].Name,-14} {sizes[i],5:F1} cm   target {target,5:F1}   {error * 100,4:+0;-0;0}%{(good ? "" : "   OUT OF RANGE")}");
        }
        return (ok, lines);
    }
}

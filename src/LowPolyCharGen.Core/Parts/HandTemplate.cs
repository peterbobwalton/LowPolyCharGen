using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace LowPolyCharGen.Parts;

/// <summary>
/// The realistic hand: a dense left hand (Assets/hand_l.obj, made by tools/make_hand.py and checked
/// in Blender) that is decimated to the character's triangle budget. Hand space, centimetres:
/// +X from the wrist to the fingers, -Y towards the thumb, -Z out of the palm, the wrist cut open at
/// x = 0 around the X axis.
/// </summary>
public sealed class HandTemplate
{
    /// <summary>A fingernail for the painter: tip centre, the last bone's direction, the way the nail faces, tip radius.</summary>
    public readonly record struct Nail(string Finger, Vector3 Tip, Vector3 Along, Vector3 Up, float Radius);

    /// <summary>The hand as it is used: positions, triangles (counter-clockwise from outside) and the
    /// open wrist as one loop of vertex indices.</summary>
    public sealed record Mesh(Vector3[] Positions, int[][] Triangles, int[] Wrist);

    public Vector3[] Positions { get; }
    public int[][] Triangles { get; }
    public IReadOnlyList<Nail> Nails { get; }

    private readonly ConcurrentDictionary<int, Mesh> _decimated = new();

    private HandTemplate(Vector3[] positions, int[][] triangles, List<Nail> nails)
    {
        Positions = positions;
        Triangles = triangles;
        Nails = nails;
    }

    private static readonly Lazy<HandTemplate> Default = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LowPolyCharGen.Assets.hand_l.obj")
            ?? throw new InvalidOperationException("The hand template resource is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader);
    });

    public static HandTemplate Left => Default.Value;

    public static HandTemplate Parse(TextReader reader)
    {
        var ci = CultureInfo.InvariantCulture;
        var positions = new List<Vector3>();
        var triangles = new List<int[]>();
        var nails = new List<Nail>();
        while (reader.ReadLine() is { } line)
        {
            var s = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (s.Length == 0) continue;
            float F(int i) => float.Parse(s[i], ci);
            switch (s[0])
            {
                case "v":
                    positions.Add(new Vector3(F(1), F(2), F(3)));
                    break;
                case "f":
                    var idx = s.Skip(1).Select(w => int.Parse(w.Split('/')[0], ci) - 1).ToArray();
                    for (var k = 1; k + 1 < idx.Length; k++) triangles.Add([idx[0], idx[k], idx[k + 1]]);
                    break;
                case "#nail":
                    nails.Add(new Nail(s[1], new Vector3(F(2), F(3), F(4)), new Vector3(F(5), F(6), F(7)), new Vector3(F(8), F(9), F(10)), F(11)));
                    break;
            }
        }
        return new HandTemplate(positions.ToArray(), triangles.ToArray(), nails);
    }

    /// <summary>The hand reduced to at most <paramref name="triangles"/> triangles (cached).</summary>
    public Mesh Decimated(int triangles) => _decimated.GetOrAdd(triangles, n =>
    {
        // Fingertips and the thumb keep their roundness best if their error counts a little more.
        var tips = Nails.Select(x => x.Tip).ToArray();
        float Importance(int v)
        {
            var p = Positions[v];
            var near = tips.Min(t => Vector3.Distance(p, t));
            return 1f + 1.5f * MathF.Max(0f, 1f - near / 2.5f);
        }
        var result = Decimator.Simplify(Positions, Triangles, new Decimator.Options { TargetTriangles = n, Importance = Importance });
        return new Mesh(result.Positions, result.Triangles, WristLoop(result.Positions, result.Triangles));
    });

    /// <summary>The open border, in order round the X axis (counter-clockwise looking down +X).</summary>
    private static int[] WristLoop(Vector3[] positions, int[][] triangles)
    {
        var next = new Dictionary<int, int>();
        var edges = new HashSet<(int, int)>();
        foreach (var t in triangles)
            for (var k = 0; k < 3; k++) edges.Add((t[k], t[(k + 1) % 3]));
        foreach (var (a, b) in edges)
            if (!edges.Contains((b, a))) next[b] = a;   // walk the border against the face winding
        if (next.Count == 0) throw new InvalidOperationException("The hand template has no open wrist.");
        var start = next.Keys.First();
        var loop = new List<int> { start };
        for (var v = next[start]; v != start && loop.Count <= next.Count; v = next[v]) loop.Add(v);
        if (loop.Count != next.Count) throw new InvalidOperationException("The hand template's wrist is not one loop.");
        // Counter-clockwise round +X: angle atan2(z, y) increasing.
        var area = 0f;
        for (var i = 0; i < loop.Count; i++)
        {
            var a = positions[loop[i]];
            var b = positions[loop[(i + 1) % loop.Count]];
            area += a.Y * b.Z - b.Y * a.Z;
        }
        if (area < 0) loop.Reverse();
        return loop.ToArray();
    }
}

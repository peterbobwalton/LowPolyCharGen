using System.Numerics;

namespace LowPolyCharGen.Kit;

/// <summary>
/// Hands in the pack's three painted styles, by moving a body's hand UVs onto another style's tile:
/// every pack body shares (nearly) the same hand mesh, painted bare, in tan gloves or in dark
/// fingerless gloves on three tiles of the texture.
/// </summary>
public static class KitHands
{
    /// <summary>The pack body that wears each style, the source of its UVs.</summary>
    private static string? Reference(GloveStyle style) => style switch
    {
        GloveStyle.Bare => "TSA_Body_H",
        GloveStyle.Tan => "TSA_Body_A",
        GloveStyle.Dark => "TSA_Body_G",
        _ => null,
    };

    public static GloveStyle StyleFor(CharacterSpec spec) =>
        spec.GloveStyle != GloveStyle.Auto ? spec.GloveStyle : spec.Gloves ? GloveStyle.Tan : GloveStyle.Auto;

    private static bool IsHandTriangle(PackPart part, int[] t) =>
        t.All(i =>
        {
            var uv = part.Uvs[i];
            float u = uv.X - MathF.Floor(uv.X), v = uv.Y - MathF.Floor(uv.Y);
            return u < 0.11f && v > 0.29f && v < 0.72f && part.Weights[i].Any(w => w.Bone.Contains("Hand", StringComparison.Ordinal));
        });

    /// <summary>The body with its hands in <paramref name="style"/> (the same part if Auto or not an Armies body).</summary>
    public static PackPart Restyle(PackPart body, GloveStyle style)
    {
        if (Reference(style) is not { } refName || KitBuilder.IsMilitia(body) || KitBuilder.IsUE5(body) || body.Name == refName) return body;
        var reference = PackLibrary.Part(refName);
        var refTris = reference.Triangles.Where(t => IsHandTriangle(reference, t)).ToList();
        if (refTris.Count == 0) return body;
        Vector3 Centroid(PackPart p, int[] t) => (p.Positions[t[0]] + p.Positions[t[1]] + p.Positions[t[2]]) / 3;

        var result = body.Clone();
        var assigned = new Dictionary<int, int>();   // body vertex -> new vertex carrying the new UV
        for (var k = 0; k < result.Triangles.Count; k++)
        {
            var t = result.Triangles[k];
            if (!IsHandTriangle(body, t)) continue;
            var c = Centroid(body, t);
            var best = refTris.MinBy(r => Vector3.DistanceSquared(Centroid(reference, r), c))!;
            if (Vector3.Distance(Centroid(reference, best), c) > 1.5f) continue;
            var corners = new int[3];
            for (var i = 0; i < 3; i++)
            {
                var p = body.Positions[t[i]];
                var match = best.MinBy(r => Vector3.DistanceSquared(reference.Positions[r], p));
                var uv = reference.Uvs[match];
                // Vertices are shared between triangles: give each (vertex, UV) pair its own copy.
                var key = t[i] * 4096 + match;
                if (!assigned.TryGetValue(key, out var nv))
                {
                    nv = result.Positions.Count;
                    result.Positions.Add(body.Positions[t[i]]);
                    result.Normals.Add(body.Normals[t[i]]);
                    result.Uvs.Add(uv);
                    result.Weights.Add(body.Weights[t[i]]);
                    assigned[key] = nv;
                }
                corners[i] = nv;
            }
            result.Triangles[k] = corners;
        }
        return result;
    }
}

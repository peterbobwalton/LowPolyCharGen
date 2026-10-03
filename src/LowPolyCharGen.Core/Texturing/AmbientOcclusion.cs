using System.Numerics;

namespace LowPolyCharGen.Texturing;

/// <summary>
/// Ambient occlusion by ray casting against the character itself. It is baked into the texture as
/// soft shading under the arms, chin, pouches and packs - the shading a painter would add.
/// </summary>
internal static class AmbientOcclusion
{
    /// <summary>
    /// Occlusion per face corner, parallel to <see cref="MeshData.Faces"/>: 1 = fully open, towards
    /// 0 = enclosed. Corners use their own shading normal, so the two sides of a hard edge (a jaw
    /// line, a box edge) are shaded independently.
    /// </summary>
    /// <param name="ignoreWithin">
    /// Hits closer than this are ignored, so layers lying directly on the surface (hair on the scalp,
    /// a vest on the shirt) do not black out what is under them.
    /// </param>
    public static float[][] PerCorner(MeshData mesh, int rays = 20, float reach = 24f, float ignoreWithin = 1.6f)
    {
        var positions = mesh.Positions;
        var triangles = new List<(Vector3 A, Vector3 E1, Vector3 E2)>(mesh.TriangleCount);
        foreach (var face in mesh.Faces)
        {
            var v = face.Vertices;
            for (var i = 1; i + 1 < v.Length; i++)
                triangles.Add((positions[v[0]], positions[v[i]] - positions[v[0]], positions[v[i + 1]] - positions[v[0]]));
        }
        var tris = triangles.ToArray();

        // Corners that share a vertex and (nearly) a normal share one sample.
        var lookup = new Dictionary<(int Vertex, int Nx, int Ny, int Nz), int>();
        var samples = new List<(Vector3 Position, Vector3 Normal)>();
        var cornerSample = new int[mesh.Faces.Length][];
        for (var f = 0; f < mesh.Faces.Length; f++)
        {
            var v = mesh.Faces[f].Vertices;
            cornerSample[f] = new int[v.Length];
            for (var i = 0; i < v.Length; i++)
            {
                var n = mesh.Normals[f][i];
                var key = (v[i], (int)MathF.Round(n.X * 8), (int)MathF.Round(n.Y * 8), (int)MathF.Round(n.Z * 8));
                if (!lookup.TryGetValue(key, out var index))
                {
                    index = samples.Count;
                    lookup.Add(key, index);
                    samples.Add((positions[v[i]], n));
                }
                cornerSample[f][i] = index;
            }
        }

        // A fixed set of directions over the hemisphere around +Z, weighted towards the pole.
        var directions = new Vector3[rays];
        for (var k = 0; k < rays; k++)
        {
            var u = (k + 0.5f) / rays;
            var phi = k * 2.399963f;   // golden angle
            var r = MathF.Sqrt(u);
            directions[k] = new Vector3(r * MathF.Cos(phi), r * MathF.Sin(phi), MathF.Sqrt(1 - u));
        }

        var result = new float[samples.Count];
        Parallel.For(0, samples.Count, si =>
        {
            var (position, n) = samples[si];
            var t1 = Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));
            var t2 = Vector3.Cross(n, t1);
            var origin = position + n * 0.2f;

            var occlusion = 0f;
            foreach (var s in directions)
            {
                var dir = t1 * s.X + t2 * s.Y + n * s.Z;
                var nearest = reach;
                foreach (var (a, e1, e2) in tris)
                {
                    // Moeller-Trumbore
                    var h = Vector3.Cross(dir, e2);
                    var det = Vector3.Dot(e1, h);
                    if (det > -1e-7f && det < 1e-7f) continue;
                    var inv = 1f / det;
                    var sVec = origin - a;
                    var u = Vector3.Dot(sVec, h) * inv;
                    if (u < 0f || u > 1f) continue;
                    var q = Vector3.Cross(sVec, e1);
                    var v = Vector3.Dot(dir, q) * inv;
                    if (v < 0f || u + v > 1f) continue;
                    var t = Vector3.Dot(e2, q) * inv;
                    if (t > ignoreWithin && t < nearest) nearest = t;
                }
                if (nearest < reach) occlusion += 1f - nearest / reach;
            }
            result[si] = 1f - occlusion / rays;
        });

        return Array.ConvertAll(cornerSample, corners => Array.ConvertAll(corners, i => result[i]));
    }
}

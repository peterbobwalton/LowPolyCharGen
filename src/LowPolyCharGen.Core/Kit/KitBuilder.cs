using System.Numerics;
using LowPolyCharGen.Texturing;

namespace LowPolyCharGen.Kit;

/// <summary>
/// Builds a character from the Toon Soldiers pack's own parts (head, body, legs, equipment) on the
/// pack skeleton, reshaped by <see cref="KitMorph"/> for the build and gender, with the pack's UVs
/// and texture. The look is the pack's; the variety comes from the reshaping and the parts.
/// </summary>
public static class KitBuilder
{
    public static CharacterModel Build(CharacterSpec spec)
    {
        var skeleton = Skeleton.CreateToonSoldier();
        var morph = new KitMorph(skeleton, spec.Gender, spec.Build);

        var names = new List<string> { spec.PackLegs, spec.PackBody, spec.PackHead };
        names.AddRange(spec.PackEquipment.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var packTexture = PackLibrary.Texture(spec.PackTexture);

        // The character's own face texture (material slot 1), painted from the head's face tile.
        var headPart = PackLibrary.Part(spec.PackHead);
        var face = headPart.Triangles.Any(t => t.All(i => FacePainter.IsFaceUv(headPart.Uvs[i])))
            ? new FacePainter(spec, headPart, packTexture) : null;
        var reshapeHead = HeadMorph.Changes(spec.Gender, spec.HeadShape);
        var parts = names.Where(n => n.Length > 0).Select(PackLibrary.Part).ToList();
        if (face is not null && HairPieces.For(spec) is { } hair) parts.Add(hair);

        var positions = new List<Vector3>();
        var source = new List<Vector3>();   // positions before the morph, for the normals
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var weights = new List<Influence[]>();
        var triangles = new List<int[]>();
        var triangleUvs = new List<Vector2[]>();
        var triangleMaterial = new List<byte>();
        foreach (var source0 in parts)
        {
            var part = source0;
            var name = part.Name;
            var isHead = ReferenceEquals(part, headPart);
            var isHair = part.Name is "Ponytail" or "Bun";
            if (morph.BodyInserts.Count > 0 && part.Uses("Clavicle"))
            {
                part = part.Clone();
                foreach (var at in morph.BodyInserts) Insert(part, at.X, at.Y);
            }
            var offset = positions.Count;
            for (var v = 0; v < part.Positions.Count; v++)
            {
                source.Add(part.Positions[v]);
                var p = reshapeHead ? HeadMorph.Apply(part.Positions[v], spec.Gender, spec.HeadShape) : part.Positions[v];
                positions.Add(morph.Apply(p));
                normals.Add(part.Normals[v]);
                uvs.Add(part.Uvs[v]);
                var list = new List<Influence>();
                foreach (var (bone, w) in part.Weights[v])
                {
                    var index = skeleton.IndexOf(bone);
                    if (index < 0) throw new InvalidDataException($"{name}: bone {bone} is not on the Toon Soldiers skeleton");
                    list.Add(new Influence(index, w));
                }
                list.Sort((a, b) => b.Weight.CompareTo(a.Weight));
                weights.Add(Skinning.Normalize(list));
            }
            foreach (var t in part.Triangles)
            {
                triangles.Add([t[0] + offset, t[1] + offset, t[2] + offset]);
                Vector2[] uv = [part.Uvs[t[0]], part.Uvs[t[1]], part.Uvs[t[2]]];
                byte material = 0;
                if (face is not null && (isHair || (isHead && uv.All(FacePainter.IsFaceUv))))
                {
                    material = 1;
                    if (isHead) uv = [face.Remap(uv[0]), face.Remap(uv[1]), face.Remap(uv[2])];
                }
                triangleUvs.Add(uv);
                triangleMaterial.Add(material);
            }
        }

        var shaded = morph.IsIdentity && !reshapeHead ? normals.ToArray() : Renormal(source, positions, normals, triangles);
        var faces = new Face[triangles.Count];
        var faceNormals = new Vector3[triangles.Count][];
        var faceUvs = new Vector2[triangles.Count][];
        for (var f = 0; f < faces.Length; f++)
        {
            var t = triangles[f];
            faces[f] = new Face(t, Slot.Body, PaintDetail.None, 0, [Vector2.Zero, Vector2.Zero, Vector2.Zero]) { Material = triangleMaterial[f] };
            faceNormals[f] = [shaded[t[0]], shaded[t[1]], shaded[t[2]]];
            faceUvs[f] = triangleUvs[f];
        }
        var mesh = new MeshData
        {
            Positions = positions.ToArray(),
            Weights = weights.ToArray(),
            Faces = faces,
            Charts = [new UvChart { Width = 1, Height = 1 }],
            Normals = faceNormals,
            Uvs = faceUvs,
        };
        return new CharacterModel
        {
            Spec = spec,
            Skeleton = skeleton,
            Mesh = mesh,
            DesignMesh = mesh,
            Paint = new PaintContext { Spec = spec, LegCenter = _ => Vector3.Zero },
            FixedTexture = packTexture,
            FaceTexture = face?.Texture,
        };
    }

    /// <summary>
    /// Shading normals after the reshape. The pack's vertices are split wherever its normals or UVs
    /// change, so each vertex is smoothed over the faces around its position whose pack normals agree
    /// with its own: hard edges stay hard, UV seams stay smooth, and the shading follows the new shape.
    /// </summary>
    private static Vector3[] Renormal(List<Vector3> before, List<Vector3> after, List<Vector3> packNormals, List<int[]> triangles)
    {
        var byPosition = new Dictionary<(int, int, int), List<int>>();
        static (int, int, int) Key(Vector3 p) => ((int)MathF.Round(p.X * 100), (int)MathF.Round(p.Y * 100), (int)MathF.Round(p.Z * 100));
        var faceNormal = new Vector3[triangles.Count];
        var facesAt = new List<int>[after.Count];
        for (var f = 0; f < triangles.Count; f++)
        {
            var t = triangles[f];
            faceNormal[f] = Vector3.Cross(after[t[1]] - after[t[0]], after[t[2]] - after[t[0]]);   // area weighted
            foreach (var v in t) (facesAt[v] ??= []).Add(f);
        }
        for (var v = 0; v < before.Count; v++)
        {
            var key = Key(before[v]);
            if (!byPosition.TryGetValue(key, out var list)) byPosition[key] = list = [];
            list.Add(v);
        }
        var result = new Vector3[after.Count];
        for (var v = 0; v < after.Count; v++)
        {
            var sum = Vector3.Zero;
            foreach (var w in byPosition[Key(before[v])])
                if (Vector3.Dot(packNormals[w], packNormals[v]) > 0.94f && facesAt[w] is { } fs)
                    foreach (var f in fs) sum += faceNormal[f];
            // Keep the pack's hand-set shading where the shape barely moved.
            var n = sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : packNormals[v];
            var moved = Math.Clamp(Vector3.Distance(before[v], after[v]) / 1.5f, 0f, 1f);
            result[v] = Vector3.Normalize(Vector3.Lerp(packNormals[v], n, moved));
        }
        return result;
    }

    /// <summary>Adds a vertex where a ray from the front at (x, z) first meets the part, splitting that triangle in three.</summary>
    private static void Insert(PackPart part, float x, float z)
    {
        var origin = new Vector3(x, -1000f, z);
        var dir = Vector3.UnitY;
        int best = -1; float bestT = float.MaxValue, bu = 0, bv = 0;
        for (var i = 0; i < part.Triangles.Count; i++)
        {
            var t = part.Triangles[i];
            Vector3 a = part.Positions[t[0]], e1 = part.Positions[t[1]] - a, e2 = part.Positions[t[2]] - a;
            var h = Vector3.Cross(dir, e2);
            var det = Vector3.Dot(e1, h);
            if (MathF.Abs(det) < 1e-9f) continue;
            var s = origin - a;
            var u = Vector3.Dot(s, h) / det;
            if (u < 0 || u > 1) continue;
            var q = Vector3.Cross(s, e1);
            var v = Vector3.Dot(dir, q) / det;
            if (v < 0 || u + v > 1) continue;
            var d = Vector3.Dot(e2, q) / det;
            if (d < bestT) { bestT = d; best = i; bu = u; bv = v; }
        }
        if (best < 0) return;
        var tri = part.Triangles[best];
        var w = new[] { 1 - bu - bv, bu, bv };
        Vector3 P(List<Vector3> l) => l[tri[0]] * w[0] + l[tri[1]] * w[1] + l[tri[2]] * w[2];
        var n = part.Positions.Count;
        part.Positions.Add(P(part.Positions));
        part.Normals.Add(Vector3.Normalize(P(part.Normals)));
        part.Uvs.Add(part.Uvs[tri[0]] * w[0] + part.Uvs[tri[1]] * w[1] + part.Uvs[tri[2]] * w[2]);
        var acc = new Dictionary<string, float>();
        for (var k = 0; k < 3; k++)
            foreach (var (bone, weight) in part.Weights[tri[k]])
                acc[bone] = acc.GetValueOrDefault(bone) + weight * w[k];
        part.Weights.Add(acc.Select(kv => (kv.Key, kv.Value)).ToArray());
        int i0 = tri[0], i1 = tri[1], i2 = tri[2];
        part.Triangles[best] = [i0, i1, n];
        part.Triangles.Add([i1, i2, n]);
        part.Triangles.Add([i2, i0, n]);
    }
}

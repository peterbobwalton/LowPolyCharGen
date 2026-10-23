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

        var packTexture = PackLibrary.Texture(spec.PackTexture);
        var militiaTexture = PackLibrary.Available && File.Exists(Path.Combine(PackLibrary.Folder, spec.MilitiaTexture + ".png"))
            ? PackLibrary.Texture(spec.MilitiaTexture) : null;
        TextureImage TextureOf(PackPart part) => IsMilitia(part) ? militiaTexture ?? packTexture : packTexture;

        // The head: a face cover (the Militia pack's balaclava and shemaghs) or a cloth headdress
        // (which needs a bare head) can replace the chosen one.
        var headName = spec.PackHead;
        if (spec.FaceCover == FaceCover.Balaclava) headName = "MIL_Mesh_head_L";
        else if (spec.FaceCover == FaceCover.Shemagh)
            headName = FacePainter.ToVec(spec.AccentColor).Length() < 0.45f ? "MIL_Mesh_head_K" : "MIL_Mesh_head_J";
        var headPart = PackLibrary.Part(headName);
        var headdress = spec.FaceCover == FaceCover.None && spec.Hat is HatStyle.Turban or HatStyle.Keffiyeh;
        if (headdress && !HairPieces.IsBare(headPart))
            headPart = PackLibrary.Part(IsMilitia(headPart) ? "MIL_Mesh_Head_A" : "TSA_Head_A1");

        // The character's own texture (material slot 1): face painted from the head's face tile, and
        // the hair and cloth strips.
        var face = headPart.Triangles.Any(t => t.All(i => FacePainter.IsFaceUv(headPart.Uvs[i])))
            ? new FacePainter(spec, headPart, TextureOf(headPart)) : null;
        var reshapeHead = HeadMorph.Changes(spec);

        var names = new List<string> { spec.PackLegs, spec.PackBody };
        names.AddRange(spec.PackEquipment.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var parts = names.Where(n => n.Length > 0).Select(PackLibrary.Part).ToList();
        parts.Add(headPart);
        var legsPart = parts[0];
        var thobe = spec.Legs == LegsStyle.Thobe && face is not null;
        if (face is not null)
        {
            if (headdress && spec.Hat == HatStyle.Turban) parts.Add(KitPieces.Turban(headPart));
            else if (headdress && spec.Hat == HatStyle.Keffiyeh) parts.Add(KitPieces.Keffiyeh(headPart));
            else if (HairPieces.For(spec, headPart) is { } hair) parts.Add(hair);
            if (thobe) parts.Add(KitPieces.ThobeSkirt(legsPart));
        }
        var bodyIndex = parts.FindIndex(p => p.Uses("Clavicle") && !ReferenceEquals(p, headPart));
        if (bodyIndex >= 0) parts[bodyIndex] = KitHands.Restyle(parts[bodyIndex], KitHands.StyleFor(spec));
        var bodyPart = bodyIndex >= 0 ? parts[bodyIndex] : null;

        var positions = new List<Vector3>();
        var source = new List<Vector3>();   // positions before the morph, for the normals
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var weights = new List<Influence[]>();
        var triangles = new List<int[]>();
        var triangleUvs = new List<Vector2[]>();
        var triangleMaterial = new List<byte>();
        var triangleGroup = new List<int>();   // clothes to recolour: 0 none, 1 body, 2 trousers, 3 boots
        var triangleTexture = new List<TextureImage>();
        foreach (var source0 in parts)
        {
            var part = source0;
            var name = part.Name;
            var isHead = ReferenceEquals(part, headPart);
            var isBody = ReferenceEquals(part, bodyPart);
            var isHair = HairPieces.IsHair(part);
            if (morph.BodyInserts.Count > 0 && isBody)
            {
                part = part.Clone();
                foreach (var at in morph.BodyInserts) Insert(part, at.X, at.Y);
            }
            var militia = IsMilitia(source0);
            var offset = positions.Count;
            for (var v = 0; v < part.Positions.Count; v++)
            {
                source.Add(part.Positions[v]);
                var p = reshapeHead ? HeadMorph.Apply(part.Positions[v], spec) : part.Positions[v];
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
            var isLegs = ReferenceEquals(source0, legsPart);
            var boot = isLegs ? BootVertices(part) : null;
            foreach (var t0 in part.Triangles)
            {
                var t = t0;
                // Generated pieces are wound to face along their normals (counter-clockwise from outside).
                if (isHair)
                {
                    Vector3 a = part.Positions[t[0]], b = part.Positions[t[1]], c = part.Positions[t[2]];
                    var n = part.Normals[t[0]] + part.Normals[t[1]] + part.Normals[t[2]];
                    if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0) t = [t[0], t[2], t[1]];
                }
                // Under a thobe only the boots and the shins above them show.
                if (thobe && isLegs && t.All(i => part.Positions[i].Z > 20f)) continue;
                triangles.Add([t[0] + offset, t[1] + offset, t[2] + offset]);
                triangleGroup.Add(isBody ? 1 : boot is null ? 0 : boot[t[0]] ? 3 : 2);
                triangleTexture.Add(TextureOf(source0));
                Vector2[] uv = [part.Uvs[t[0]], part.Uvs[t[1]], part.Uvs[t[2]]];
                byte material = militia ? (byte)2 : (byte)0;
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
        // Per-vertex data for the kit material (Nanite skinned meshes drop vertex colours, and GPU
        // skinning allows four UV sets, so it travels in UV channels 1 to 3):
        //   skin: on triangles that show skin, skin-coloured texels are multiplied by the ratio of the
        //     skin colour to the pack's (stored halved; 0.5 = unchanged, used everywhere else);
        //   clothes: a thobe recolours the body's clothes to the colour (linear) divided by the clothes'
        //     mean linear luminance, which the material multiplies by the texel's luminance (0 = off).
        // Colors = (skin R, G, B, cloth R), Colors2.XY = (cloth G, B).
        var tint = FacePainter.ToVec(spec.SkinColor) / FacePainter.ToVec(Rgb.FromHex("#D9A274")) / 2f;
        var colors = new Vector4[positions.Count];
        var colors2 = new Vector4[positions.Count];
        Array.Fill(colors, new Vector4(0.5f, 0.5f, 0.5f, 0));
        for (var f = 0; f < triangles.Count; f++)
            if (triangleMaterial[f] != 1 && ShowsSkin(triangleTexture[f], triangleUvs[f]))
                foreach (var v in triangles[f]) colors[v] = new Vector4(tint, colors[v].W);
        var trainers = spec.Footwear == Footwear.Trainers;
        Rgb? GroupColor(int group) => group switch
        {
            1 when thobe || spec.RecolourClothes => spec.ShirtColor,
            2 when spec.RecolourClothes => spec.TrousersColor,
            3 when trainers || spec.RecolourClothes => spec.BootsColor,
            _ => null,
        };
        for (var group = 1; group <= 3; group++)
        {
            if (GroupColor(group) is not { } color) continue;
            var cloth = FacePainter.ToVec(color);
            var linear = new Vector3(Lin(cloth.X), Lin(cloth.Y), Lin(cloth.Z));
            float sum = 0; var n = 0;
            for (var f = 0; f < triangles.Count; f++)
                if (triangleGroup[f] == group) foreach (var l in ClothLuminance(triangleTexture[f], triangleUvs[f])) { sum += l; n++; }
            var scaled = linear / (n > 0 ? MathF.Max(sum / n, 0.01f) : 0.1f);
            for (var f = 0; f < triangles.Count; f++)
                if (triangleGroup[f] == group)
                    foreach (var v in triangles[f])
                    {
                        colors[v].W = scaled.X;
                        colors2[v] = new Vector4(scaled.Y, scaled.Z, 0, 0);
                    }
        }

        var mesh = new MeshData
        {
            Colors = colors,
            Colors2 = colors2,
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

    /// <summary>The boots of a legs part: the pieces (joined by shared vertices) that reach the ground.</summary>
    private static bool[] BootVertices(PackPart legs)
    {
        var parent = Enumerable.Range(0, legs.Positions.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        foreach (var t in legs.Triangles) { parent[Find(t[1])] = Find(t[0]); parent[Find(t[2])] = Find(t[0]); }
        var grounded = new HashSet<int>();
        for (var v = 0; v < legs.Positions.Count; v++)
            if (legs.Positions[v].Z < 0.5f) grounded.Add(Find(v));
        return Enumerable.Range(0, legs.Positions.Count).Select(v => grounded.Contains(Find(v))).ToArray();
    }

    public static bool IsMilitia(PackPart part) => part.Name.StartsWith("MIL_", StringComparison.Ordinal);

    private static float Lin(float c) => MathF.Pow(c, 2.2f);

    /// <summary>Linear luminance of the non-skin texels sampled over a triangle.</summary>
    private static IEnumerable<float> ClothLuminance(TextureImage texture, Vector2[] uv)
    {
        const int n = 4;
        for (var i = 0; i <= n; i++)
            for (var j = 0; i + j <= n; j++)
            {
                float a = (float)i / n, b = (float)j / n;
                var p = uv[0] * (1 - a - b) + uv[1] * a + uv[2] * b;
                var x = Math.Clamp((int)((p.X - MathF.Floor(p.X)) * texture.Size), 0, texture.Size - 1);
                var y = Math.Clamp((int)((1 - (p.Y - MathF.Floor(p.Y))) * texture.Size), 0, texture.Size - 1);
                var o = (y * texture.Size + x) * texture.Channels;
                float r = texture.Rgb[o] / 255f, g = texture.Rgb[o + 1] / 255f, bl = texture.Rgb[o + 2] / 255f;
                if (r - g > 0.15f && r > 0.45f && g > bl) continue;
                yield return 0.3f * Lin(r) + 0.59f * Lin(g) + 0.11f * Lin(bl);
            }
    }

    /// <summary>True if any of a grid of samples over the triangle's texels is the pack's skin colour.</summary>
    private static bool ShowsSkin(TextureImage texture, Vector2[] uv)
    {
        const int n = 6;
        for (var i = 0; i <= n; i++)
            for (var j = 0; i + j <= n; j++)
            {
                float a = (float)i / n, b = (float)j / n;
                var p = uv[0] * (1 - a - b) + uv[1] * a + uv[2] * b;
                var x = Math.Clamp((int)((p.X - MathF.Floor(p.X)) * texture.Size), 0, texture.Size - 1);
                var y = Math.Clamp((int)((1 - (p.Y - MathF.Floor(p.Y))) * texture.Size), 0, texture.Size - 1);
                var o = (y * texture.Size + x) * texture.Channels;
                float r = texture.Rgb[o] / 255f, g = texture.Rgb[o + 1] / 255f, bl = texture.Rgb[o + 2] / 255f;
                if (r - g > 0.15f && r > 0.45f && g > bl) return true;
            }
        return false;
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

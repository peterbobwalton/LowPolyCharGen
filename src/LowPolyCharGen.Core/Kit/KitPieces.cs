using System.Numerics;

namespace LowPolyCharGen.Kit;

/// <summary>
/// Headwear and garments the packs don't have, as low-poly pieces in the packs' style, textured
/// from the cloth strips of the character's own texture (see <see cref="FacePainter"/>): turban,
/// keffiyeh with agal, and the thobe's skirt.
/// </summary>
public static class KitPieces
{
    private static readonly Vector3 Origin = new(0, -1.5f, 166f);

    /// <summary>Farthest hit of a ray from <paramref name="origin"/> along <paramref name="dir"/> on the part (0 if none).</summary>
    internal static float Hit(PackPart part, Vector3 origin, Vector3 dir)
    {
        var best = 0f;
        foreach (var t in part.Triangles)
        {
            Vector3 a = part.Positions[t[0]], e1 = part.Positions[t[1]] - a, e2 = part.Positions[t[2]] - a;
            var h = Vector3.Cross(dir, e2);
            var det = Vector3.Dot(e1, h);
            if (MathF.Abs(det) < 1e-9f) continue;
            var s0 = origin - a;
            var u = Vector3.Dot(s0, h) / det;
            if (u < 0 || u > 1) continue;
            var q = Vector3.Cross(s0, e1);
            var v = Vector3.Dot(dir, q) / det;
            if (v < 0 || u + v > 1) continue;
            best = MathF.Max(best, Vector3.Dot(e2, q) / det);
        }
        return best;
    }

    private static Vector3 Dir(float theta, float phi) =>
        new(MathF.Sin(theta) * MathF.Cos(phi), -MathF.Cos(theta) * MathF.Cos(phi), MathF.Sin(phi));

    private static float Rad(float degrees) => degrees * MathF.PI / 180f;

    /// <summary>
    /// A turban: wraps of cloth round the skull, each row standing a little proud of the one above,
    /// low over the brow at the front and down to the nape at the back, tucked in at the bottom edge.
    /// </summary>
    public static PackPart Turban(PackPart head)
    {
        const int columns = 16;
        // (elevation at the front, at the back, distance off the skull) per row, top to bottom.
        (float Front, float Back, float Offset)[] rows =
        [
            (76, 76, 9.6f), (60, 58, 9.4f), (45, 41, 8.0f), (32, 26, 7.6f), (21, 8, 5.0f), (18, 5, 0.6f),
        ];
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int[]>();
        float U(int c) => 0.01f + (FacePainter.HairStripU - 0.02f) * c / columns;   // the headwear cloth region

        var crownDir = Vector3.UnitZ;
        positions.Add(Origin + crownDir * (Hit(head, Origin, crownDir) + 9.2f));
        normals.Add(Vector3.UnitZ);
        uvs.Add(new Vector2(U(columns / 2), FacePainter.ClothV - 0.01f));
        var ring = new int[rows.Length, columns + 1];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c <= columns; c++)
            {
                var theta = MathF.Tau * c / columns;
                var back = (1 - MathF.Cos(theta)) / 2;   // 0 at the front, 1 at the back
                var phi = Rad(float.Lerp(rows[r].Front, rows[r].Back, back));
                var dir = Dir(theta, phi);
                ring[r, c] = positions.Count;
                positions.Add(Origin + dir * (Hit(head, Origin, dir) + rows[r].Offset));
                normals.Add(Vector3.Normalize(dir with { Z = dir.Z * 0.6f }));
                uvs.Add(new Vector2(U(c), FacePainter.ClothV - 0.02f - (FacePainter.ClothV - 0.04f) * (r + 1) / rows.Length));
            }
        for (var c = 0; c < columns; c++) tris.Add([0, ring[0, c + 1], ring[0, c]]);
        for (var r = 0; r + 1 < rows.Length; r++)
            for (var c = 0; c < columns; c++)
            {
                tris.Add([ring[r, c], ring[r, c + 1], ring[r + 1, c]]);
                tris.Add([ring[r, c + 1], ring[r + 1, c + 1], ring[r + 1, c]]);
            }
        return Part("Turban", positions, normals, uvs, tris, _ => [("Bip001-Head", 1f)]);
    }

    /// <summary>A keffiyeh: a cloth over the head to the shoulders, open round the face, held by a black agal.</summary>
    public static PackPart Keffiyeh(PackPart head)
    {
        var cloth = HairPieces.LongHair(head, 145f, "Keffiyeh", 0f, FacePainter.HairStripU, 1.2f, FacePainter.ClothV - 0.01f, 0.01f);
        // The agal: a band of black cord round the crown.
        const int columns = 14;
        float[] elevations = [33f, 26f];
        var positions = cloth.Positions; var normals = cloth.Normals; var uvs = cloth.Uvs; var tris = cloth.Triangles;
        var band = new int[4, columns + 1];
        for (var r = 0; r < 4; r++)   // outer top, outer bottom, inner bottom, inner top
            for (var c = 0; c <= columns; c++)
            {
                var theta = MathF.Tau * c / columns;
                var dir = Dir(theta, Rad(elevations[r is 0 or 3 ? 0 : 1]));
                var skull = Hit(head, Origin, dir);
                band[r, c] = positions.Count;
                positions.Add(Origin + dir * (skull + (r < 2 ? 2.3f : 1.3f)));
                normals.Add(r < 2 ? dir : r == 2 ? -Vector3.UnitZ : Vector3.UnitZ);
                uvs.Add(new Vector2(FacePainter.HatStripU + 0.01f + 0.04f * c / columns, 0.03f));
            }
        for (var r = 0; r < 4; r++)
        {
            var next = (r + 1) % 4;
            for (var c = 0; c < columns; c++)
            {
                tris.Add([band[r, c], band[next, c], band[r, c + 1]]);
                tris.Add([band[r, c + 1], band[next, c], band[next, c + 1]]);
            }
        }
        var weights = cloth.Weights;
        while (weights.Count < positions.Count) weights.Add([("Bip001-Head", 1f)]);
        return cloth;
    }

    /// <summary>
    /// The thobe's skirt: a loose tube from the hips to the ankles round both legs (measured from the
    /// legs part), flaring a little, bound to the pelvis at the top and the thighs and calves below.
    /// </summary>
    public static PackPart ThobeSkirt(PackPart legs, float topZ = 100f, float bottomZ = 16f)
    {
        const int columns = 16;
        var heights = new List<float>();
        for (var z = topZ; z > bottomZ + 0.1f; z -= 12f) heights.Add(z);
        heights.Add(bottomZ);
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int[]>();
        var weights = new List<(string, float)[]>();
        float U(int c) => FacePainter.GarmentStripU + 0.01f + (0.99f - FacePainter.GarmentStripU - 0.02f) * c / columns;
        // The cloth hangs straight from the hips: as wide as the legs at the hips all the way down,
        // flaring a little towards the hem.
        var hips = legs.Positions.Where(p => p.Z > topZ - 14f && p.Z < topZ + 2f).ToList();
        float halfWidth0 = hips.Count > 0 ? hips.Max(p => MathF.Abs(p.X)) : 20f;
        float front0 = hips.Count > 0 ? -hips.Min(p => p.Y) : 14f, back0 = hips.Count > 0 ? hips.Max(p => p.Y) : 14f;
        for (var r = 0; r < heights.Count; r++)
        {
            var z = heights[r];
            var halfWidth = halfWidth0; float front = front0, backY = back0;
            var flare = 1 + 0.12f * (topZ - z) / (topZ - bottomZ);
            var ease = 0.6f + 1.2f * (topZ - z) / (topZ - bottomZ);
            for (var c = 0; c <= columns; c++)
            {
                var theta = MathF.Tau * c / columns;
                float sx = MathF.Sin(theta), sy = -MathF.Cos(theta);
                var p = new Vector3(sx * (halfWidth * flare + ease), sy * ((sy < 0 ? front : backY) * float.Lerp(1f, 1.15f, (topZ - z) / (topZ - bottomZ)) + ease), z);
                if (r == 0) p = p with { X = p.X * 0.97f, Y = p.Y * 0.97f };
                positions.Add(p);
                normals.Add(Vector3.Normalize(new Vector3(sx, sy, 0.05f)));
                uvs.Add(new Vector2(U(c), 0.98f - 0.96f * (topZ - z) / (topZ - bottomZ)));
                // Weights: the pelvis at the hips, then the legs, each side mostly its own leg.
                var down = (topZ - z) / (topZ - bottomZ);
                var left = Math.Clamp(0.5f + p.X / (halfWidth * 1.4f), 0f, 1f);
                var pelvis = Math.Clamp(1 - down * 2.2f, 0f, 1f);
                var calf = Math.Clamp((down - 0.5f) * 1.2f, 0f, 0.6f);
                var thigh = 1 - pelvis - calf;
                var w = new List<(string, float)>();
                if (pelvis > 0) w.Add(("Bip001-Pelvis", pelvis));
                if (thigh > 0) { w.Add(("Bip001-L-Thigh", thigh * left)); w.Add(("Bip001-R-Thigh", thigh * (1 - left))); }
                if (calf > 0) { w.Add(("Bip001-L-Calf", calf * left)); w.Add(("Bip001-R-Calf", calf * (1 - left))); }
                weights.Add(w.Where(x => x.Item2 > 0.001f).ToArray());
            }
        }
        var row = columns + 1;
        for (var r = 0; r + 1 < heights.Count; r++)
            for (var c = 0; c < columns; c++)
            {
                int a = r * row + c, b = a + 1, d = a + row, e = d + 1;
                tris.Add([a, b, d]);
                tris.Add([b, e, d]);
            }
        // Inside of the hem, so it doesn't look hollow from below.
        var hem = heights.Count - 1;
        var inner = new int[row];
        for (var c = 0; c <= columns; c++)
        {
            var p = positions[hem * row + c];
            inner[c] = positions.Count;
            positions.Add(p + Vector3.Normalize(new Vector3(-p.X, -p.Y, 0)) * 0.8f + new Vector3(0, 0, 1.5f));
            normals.Add(Vector3.Normalize(new Vector3(-p.X, -p.Y, 0)));
            uvs.Add(uvs[hem * row + c]);
            weights.Add(weights[hem * row + c]);
        }
        for (var c = 0; c < columns; c++)
        {
            int a = hem * row + c, b = a + 1;
            tris.Add([a, inner[c], b]);
            tris.Add([b, inner[c], inner[c + 1]]);
        }
        return new PackPart { Name = "Thobe", Positions = positions, Normals = normals, Uvs = uvs, Triangles = tris, Weights = weights };
    }

    private static PackPart Part(string name, List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<int[]> tris,
        Func<Vector3, (string, float)[]> weight) =>
        new() { Name = name, Positions = positions, Normals = normals, Uvs = uvs, Triangles = tris, Weights = positions.Select(weight).ToList() };
}

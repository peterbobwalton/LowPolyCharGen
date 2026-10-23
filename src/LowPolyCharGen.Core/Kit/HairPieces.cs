using System.Numerics;

namespace LowPolyCharGen.Kit;

/// <summary>
/// Hair that stands off the pack's heads (a ponytail or a bun), as small low-poly pack parts bound to
/// the head bone, textured from the hair strip of the character's face texture.
/// </summary>
public static class HairPieces
{
    public static bool IsHair(PackPart part) => part.Name is "Ponytail" or "Bun" or "LongHair";

    public static PackPart? For(CharacterSpec spec, PackPart head)
    {
        // Long hair and bobs fit bare heads only; under headgear they fall back to a ponytail (women).
        var bare = !head.Positions.Any(p => p.Z > 181.5f || MathF.Abs(p.X) > 12.5f);
        if (bare && spec.Hair is HairStyle.Long or HairStyle.Bob)
            return LongHair(head, spec.Hair == HairStyle.Long ? 146f : 155.5f);
        if (spec.Gender != Gender.Female) return null;
        return spec.Hair switch
        {
            HairStyle.Ponytail or HairStyle.Long or HairStyle.Bob => Tube("Ponytail",
                [new(0, 8.6f, 166.5f), new(0, 11.6f, 163.5f), new(0, 13.6f, 159f), new(0, 14.4f, 154.5f), new(0, 14.2f, 150.5f)],
                [2.4f, 2.6f, 2.2f, 1.5f, 0.35f], 6),
            HairStyle.Bun => Tube("Bun",
                [new(0, 8.4f, 169.5f), new(0, 10.6f, 170.2f), new(0, 12.4f, 170.6f), new(0, 13.4f, 170.8f)],
                [2.2f, 3.3f, 2.8f, 0.6f], 7),
            _ => null,
        };
    }

    /// <summary>
    /// A solid piece of long hair, like a toy figure's: a shell over the skull (fitted to the head by
    /// casting rays at it) with a straight fringe, open round the face, hanging straight down to
    /// <paramref name="bottomZ"/> with a slight flare, and given thickness below the fringe.
    /// </summary>
    private static PackPart LongHair(PackPart head, float bottomZ)
    {
        const int columns = 14;
        var origin = new Vector3(0, -1.5f, 166f);
        float[] elevations = [72f, 50f, 30f, 15f, 2f, -12f];   // degrees; rows at and above 15° close over the face (the fringe)
        const float openAngle = 62f;                           // degrees each side of the front left open below the fringe
        float Hit(Vector3 dir)
        {
            var best = 0f;
            foreach (var t in head.Triangles)
            {
                Vector3 a = head.Positions[t[0]], e1 = head.Positions[t[1]] - a, e2 = head.Positions[t[2]] - a;
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
        Vector3 Dir(float theta, float phi) =>
            new(MathF.Sin(theta) * MathF.Cos(phi), -MathF.Cos(theta) * MathF.Cos(phi), MathF.Sin(phi));

        var rows = new List<Vector3[]>();
        var open = new List<bool>();
        foreach (var e in elevations)
        {
            var phi = e * MathF.PI / 180f;
            var isOpen = e < 15f;
            var start = isOpen ? openAngle * MathF.PI / 180f : 0f;
            var row = new Vector3[columns + 1];
            for (var c = 0; c <= columns; c++)
            {
                var theta = start + (MathF.Tau - 2 * start) * c / columns;
                var dir = Dir(theta, phi);
                var offset = 1.5f + 0.5f * (1 - MathF.Abs(MathF.Cos(theta)));   // fuller at the sides
                row[c] = origin + dir * (Hit(dir) + offset);
            }
            rows.Add(row);
            open.Add(isOpen);
        }
        // Hanging part: straight down from the lowest row, flaring a little.
        var last = rows[^1];
        var lastZ = last.Average(p => p.Z);
        foreach (var z in new[] { float.Lerp(lastZ, bottomZ, 0.5f), bottomZ })
        {
            var row = new Vector3[columns + 1];
            for (var c = 0; c <= columns; c++)
            {
                var flat = new Vector2(last[c].X - origin.X, last[c].Y - origin.Y);
                var flare = 1 + 0.025f * (lastZ - z);
                row[c] = new Vector3(origin.X + flat.X * flare, origin.Y + flat.Y * flare, z + (last[c].Z - lastZ) * 0.3f);
            }
            rows.Add(row);
            open.Add(true);
        }

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int[]>();
        float U(int c) => FacePainter.HairStripU + 0.02f + (0.97f - FacePainter.HairStripU) * c / columns;
        float V(int r) => 0.98f - 0.96f * r / rows.Count;
        // Crown point and outer shell.
        var crown = origin + Vector3.UnitZ * (Hit(Vector3.UnitZ) + 1.5f);
        positions.Add(crown); normals.Add(Vector3.UnitZ); uvs.Add(new Vector2(U(columns / 2), 0.99f));
        var outer = new int[rows.Count, columns + 1];
        for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c <= columns; c++)
            {
                outer[r, c] = positions.Count;
                positions.Add(rows[r][c]);
                var n = rows[r][c] - origin;
                normals.Add(Vector3.Normalize(r >= elevations.Length ? n with { Z = 0 } : n));
                uvs.Add(new Vector2(U(c), V(r + 1)));
            }
        for (var c = 0; c < columns; c++) tris.Add([0, outer[0, c + 1], outer[0, c]]);
        for (var r = 0; r + 1 < rows.Count; r++)
            for (var c = 0; c < columns; c++)
            {
                tris.Add([outer[r, c], outer[r, c + 1], outer[r + 1, c]]);
                tris.Add([outer[r, c + 1], outer[r + 1, c + 1], outer[r + 1, c]]);
            }
        // Inner shell where the hair hangs free (the open rows), its bottom edge and the edges by the face.
        var firstOpen = open.IndexOf(true);
        var inner = new int[rows.Count, columns + 1];
        for (var r = firstOpen; r < rows.Count; r++)
            for (var c = 0; c <= columns; c++)
            {
                var p = rows[r][c];
                var radial = Vector3.Normalize((p - origin) with { Z = 0 });
                inner[r, c] = positions.Count;
                positions.Add(p - radial * 1.0f);
                normals.Add(-radial);
                uvs.Add(new Vector2(U(c), V(r + 1)));
            }
        for (var r = firstOpen; r + 1 < rows.Count; r++)
            for (var c = 0; c < columns; c++)
            {
                tris.Add([inner[r, c], inner[r + 1, c], inner[r, c + 1]]);
                tris.Add([inner[r, c + 1], inner[r + 1, c], inner[r + 1, c + 1]]);
            }
        var b = rows.Count - 1;
        for (var c = 0; c < columns; c++)
        {
            tris.Add([outer[b, c], outer[b, c + 1], inner[b, c]]);
            tris.Add([outer[b, c + 1], inner[b, c + 1], inner[b, c]]);
        }
        for (var r = firstOpen; r + 1 < rows.Count; r++)
        {
            tris.Add([outer[r, 0], outer[r + 1, 0], inner[r, 0]]);
            tris.Add([inner[r, 0], outer[r + 1, 0], inner[r + 1, 0]]);
            tris.Add([outer[r, columns], inner[r, columns], outer[r + 1, columns]]);
            tris.Add([inner[r, columns], inner[r + 1, columns], outer[r + 1, columns]]);
        }
        var weights = positions.Select(p => p.Z > 152f
            ? new[] { ("Bip001-Head", 1f) }
            : new[] { ("Bip001-Head", 0.6f), ("Bip001-Neck", 0.4f) }).ToList();
        return new PackPart { Name = "LongHair", Positions = positions, Normals = normals, Uvs = uvs, Triangles = tris, Weights = weights };
    }

    /// <summary>A tapering tube of rings along a path (closed at the tip, open at the root inside the head).</summary>
    private static PackPart Tube(string name, Vector3[] path, float[] radii, int sides)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int[]>();
        for (var r = 0; r < path.Length; r++)
        {
            var along = Vector3.Normalize(path[Math.Min(r + 1, path.Length - 1)] - path[Math.Max(r - 1, 0)]);
            var side = Vector3.UnitX;
            var up = Vector3.Normalize(Vector3.Cross(along, side));
            for (var s = 0; s <= sides; s++)   // the seam column is doubled for the UVs
            {
                var a = s * MathF.Tau / sides;
                var dir = side * MathF.Cos(a) + up * MathF.Sin(a);
                positions.Add(path[r] + dir * radii[r]);
                normals.Add(dir);
                uvs.Add(new Vector2(FacePainter.HairStripU + 0.02f + (0.97f - FacePainter.HairStripU) * s / sides, 0.98f - 0.96f * r / (path.Length - 1)));
            }
        }
        var row = sides + 1;
        for (var r = 0; r + 1 < path.Length; r++)
            for (var s = 0; s < sides; s++)
            {
                int a = r * row + s, b = a + 1, c = a + row, d = c + 1;
                tris.Add([a, c, b]);
                tris.Add([b, c, d]);
            }
        var weights = positions.Select(_ => new[] { ("Bip001-Head", 1f) }).ToList();
        return new PackPart { Name = name, Positions = positions, Normals = normals, Uvs = uvs, Triangles = tris, Weights = weights };
    }
}

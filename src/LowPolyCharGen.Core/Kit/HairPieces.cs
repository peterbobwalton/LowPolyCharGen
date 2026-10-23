using System.Numerics;

namespace LowPolyCharGen.Kit;

/// <summary>
/// Hair that stands off the pack's heads (a ponytail or a bun), as small low-poly pack parts bound to
/// the head bone, textured from the hair strip of the character's face texture.
/// </summary>
public static class HairPieces
{
    public static PackPart? For(CharacterSpec spec)
    {
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

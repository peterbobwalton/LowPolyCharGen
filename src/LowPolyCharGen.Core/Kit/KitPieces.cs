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

    /// <summary>
    /// How far a shell point along <paramref name="dir"/> must stand to clear the head: the farthest hit, and every head
    /// vertex within <paramref name="coneDegrees"/>. Hits alone miss the ridges and corners between a coarse shell's rays.
    /// </summary>
    internal static float Reach(PackPart head, Vector3 origin, Vector3 dir, float coneDegrees)
    {
        var reach = Hit(head, origin, dir);
        var cone = MathF.Cos(coneDegrees * MathF.PI / 180f);
        foreach (var p in head.Positions)
        {
            var to = p - origin;
            var distance = to.Length();
            if (distance > reach && Vector3.Dot(to, dir) > cone * distance) reach = distance;
        }
        return reach;
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
        positions.Add(Origin + crownDir * (Reach(head, Origin, crownDir, 14f) + 9.2f));
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
                positions.Add(Origin + dir * (Reach(head, Origin, dir, 14f) + rows[r].Offset));
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
    public static PackPart Keffiyeh(PackPart head, bool agal = true)
    {
        var cloth = HairPieces.LongHair(head, 145f, "Keffiyeh", 0f, FacePainter.HairStripU, 1.2f, FacePainter.ClothV - 0.01f, 0.01f);
        if (!agal) return cloth;
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
                var skull = Reach(head, Origin, dir, HairPieces.ClearanceCone);   // as the cloth is fitted, so it stays on top of it
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

    // ---- flight gear and bandages: flat colours from the swatches of the face texture ----------------------------

    /// <summary>The name of every piece coloured from the swatches (see <see cref="FacePainter.Swatch"/>).</summary>
    public const string GearPiece = "GearPiece";

    public const int SwatchShell = 0, SwatchShellDark = 1, SwatchVisor = 2, SwatchHousing = 3, SwatchMask = 4, SwatchRubber = 5,
        SwatchVest = 6, SwatchVestDark = 7, SwatchGSuit = 8, SwatchBandage = 9, SwatchReflective = 10;

    private static readonly (string, float)[] OnHead = [("Bip001-Head", 1f)];

    /// <summary>
    /// A shell over a (rows + 1) x (columns + 1) grid of points: the kept cells, smooth shaded, and a wall down to the
    /// inner surface along every open edge, so it has thickness. Wound by the builder from its normals.
    /// <paramref name="wrap"/>: the first and last columns meet (else they are open edges). <paramref name="inward"/>: a
    /// lining, facing the center and without walls: everything is single sided, so a shell seen from inside shows the sky.
    /// </summary>
    private static PackPart Shell(int rows, int columns, Func<int, int, Vector3> outer, Func<int, int, Vector3> inner, Func<int, int, bool> keep,
        Func<int, int, Vector2> uv, Func<Vector3, (string, float)[]> weight, Vector3 center, bool wrap = true, bool inward = false)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int[]>();
        int At(int r, int c) => r * (columns + 1) + c;
        for (var r = 0; r <= rows; r++)
            for (var c = 0; c <= columns; c++)
            {
                var p = outer(r, c);
                positions.Add(p);
                var n = inward ? center - p : p - center;
                normals.Add(n.LengthSquared() > 1e-6f ? Vector3.Normalize(n) : Vector3.UnitZ);
                uvs.Add(uv(r, c));
            }
        void Wall(int r0, int c0, int r1, int c1, Vector3 cell)
        {
            Vector3 a = outer(r0, c0), b = outer(r1, c1), a2 = inner(r0, c0), b2 = inner(r1, c1);
            var n = (a + b) / 2 - cell;
            n = n.LengthSquared() > 1e-6f ? Vector3.Normalize(n) : Vector3.UnitZ;
            var i = positions.Count;
            positions.AddRange([a, b, a2, b2]);
            normals.AddRange([n, n, n, n]);
            uvs.AddRange([uv(r0, c0), uv(r1, c1), uv(r0, c0), uv(r1, c1)]);
            tris.Add([i, i + 1, i + 2]);
            tris.Add([i + 1, i + 3, i + 2]);
        }
        bool Kept(int r, int c) => r >= 0 && r < rows && (wrap || (c >= 0 && c < columns)) && keep(r, (c + columns) % columns);
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < columns; c++)
            {
                if (!keep(r, c)) continue;
                tris.Add([At(r, c), At(r, c + 1), At(r + 1, c)]);
                tris.Add([At(r, c + 1), At(r + 1, c + 1), At(r + 1, c)]);
                if (inward) continue;
                var cell = (outer(r, c) + outer(r, c + 1) + outer(r + 1, c) + outer(r + 1, c + 1)) / 4;
                if (!Kept(r - 1, c)) Wall(r, c, r, c + 1, cell);
                if (!Kept(r + 1, c)) Wall(r + 1, c, r + 1, c + 1, cell);
                if (!Kept(r, c - 1)) Wall(r, c, r + 1, c, cell);
                if (!Kept(r, c + 1)) Wall(r, c + 1, r + 1, c + 1, cell);
            }
        return Part(GearPiece, positions, normals, uvs, tris, weight);
    }

    /// <summary>A round tube through the points, with closed ends.</summary>
    private static PackPart Tube(Vector3[] points, float[] radii, int swatch, Func<Vector3, (string, float)[]> weight, int sides = 8)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int[]>();
        for (var i = 0; i < points.Length; i++)
        {
            var along = Vector3.Normalize(points[Math.Min(i + 1, points.Length - 1)] - points[Math.Max(i - 1, 0)]);
            var side = Vector3.Normalize(Vector3.Cross(along, MathF.Abs(along.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ));
            var up = Vector3.Cross(side, along);
            for (var s = 0; s < sides; s++)
            {
                var a = MathF.Tau * s / sides;
                var n = side * MathF.Cos(a) + up * MathF.Sin(a);
                positions.Add(points[i] + n * radii[i]);
                normals.Add(n);
                uvs.Add(FacePainter.Swatch(swatch, 0.35f + 0.5f * MathF.Max(0, n.Z)));
            }
        }
        for (var i = 0; i + 1 < points.Length; i++)
            for (var s = 0; s < sides; s++)
            {
                int a = i * sides + s, b = i * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                tris.Add([a, b, c]);
                tris.Add([b, d, c]);
            }
        foreach (var (ring, dir) in new[] { (0, points[0] - points[1]), (points.Length - 1, points[^1] - points[^2]) })
        {
            var cap = positions.Count;
            positions.Add(points[ring] + Vector3.Normalize(dir) * radii[ring] * 0.5f);
            normals.Add(Vector3.Normalize(dir));
            uvs.Add(FacePainter.Swatch(swatch, 0.6f));
            for (var s = 0; s < sides; s++) tris.Add([cap, ring * sides + s, ring * sides + (s + 1) % sides]);
        }
        return Part(GearPiece, positions, normals, uvs, tris, weight);
    }

    /// <summary>A point on the ellipsoid of a helmet, round <see cref="Origin"/>; a smooth shape, unlike the head's planes.</summary>
    private static Vector3 Dome(float theta, float phi, float sides, float front, float back, float top, float scale = 1f)
    {
        float cosPhi = MathF.Cos(phi), sinPhi = MathF.Sin(phi), cosTheta = MathF.Cos(theta);
        return Origin + new Vector3(MathF.Sin(theta) * cosPhi * sides, -cosTheta * cosPhi * (cosTheta > 0 ? front : back), sinPhi * (phi > 0 ? top : top * 0.9f)) * scale;
    }

    /// <summary>The lining's size against the shell's, clear of the head at the ears and jaw.</summary>
    private const float HelmetLiner = 0.92f;

    /// <summary>A helmet's sizes (cm) from the origin inside the head, whose sides are 11.5 and top 14.3 away.</summary>
    private static (float Sides, float Front, float Back, float Top) HelmetSize(bool jet) =>
        jet ? (14.2f, 15.4f, 14.2f, 17.4f) : (14.6f, 16f, 14.6f, 17.8f);

    /// <summary>
    /// The head with whatever rises above the brow inside the helmet's lining, pulled in: the pack's hair comes to a peak
    /// over the forehead, further out than any helmet shape that still looks like one.
    /// </summary>
    public static PackPart TuckUnderHelmet(PackPart head, bool jet)
    {
        var (sides, front, back, top) = HelmetSize(jet);
        var tucked = head.Clone();
        for (var v = 0; v < tucked.Positions.Count; v++)
        {
            var to = tucked.Positions[v] - Origin;
            if (tucked.Positions[v].Z <= 172f) continue;
            var length = to.Length();
            var dir = to / length;
            // The lining's radius along this direction, from the helmet's ellipsoid.
            var depth = dir.Y < 0 ? front : back;
            var radius = HelmetLiner / MathF.Sqrt(dir.X * dir.X / (sides * sides) + dir.Y * dir.Y / (depth * depth) + dir.Z * dir.Z / (top * top));
            if (length > radius - 0.2f) tucked.Positions[v] = Origin + dir * (radius - 0.2f);
        }
        return tucked;
    }

    /// <summary>A flight helmet's parts: the shell, the visor and its housing, then the mask or the mic.</summary>
    public static IEnumerable<PackPart> FlightHelmet(CharacterSpec spec)
    {
        var jet = spec.FlightHelmet == LowPolyCharGen.FlightHelmet.Jet;
        const int columns = 24, skirtColumns = 18;
        const float liner = HelmetLiner;
        var (sides, front, back, top) = HelmetSize(jet);
        var earBulge = jet ? 0.7f : 1.5f;
        var rim = jet ? 26f : 24f;
        // The dome, down to the brow all the way round.
        float[] elevations = [90, 72, 54, 38, rim];
        float ShellTone(float z) => 0.3f + 0.7f * Math.Clamp((z - 150f) / 34f, 0f, 1f);
        yield return Shell(elevations.Length - 1, columns,
            (r, c) => Dome(MathF.Tau * c / columns, Rad(elevations[r]), sides, front, back, top),
            (r, c) => Dome(MathF.Tau * c / columns, Rad(elevations[r]), sides, front, back, top, liner),
            (_, _) => true, (r, _) => FacePainter.Swatch(SwatchShell, ShellTone(Origin.Z + MathF.Sin(Rad(elevations[r])) * top)), _ => OnHead, Origin);
        yield return Shell(elevations.Length - 1, columns,
            (r, c) => Dome(MathF.Tau * c / columns, Rad(elevations[r]), sides, front, back, top, liner),
            (r, c) => Dome(MathF.Tau * c / columns, Rad(elevations[r]), sides, front, back, top, liner),
            (_, _) => true, (_, _) => FacePainter.Swatch(SwatchRubber, 0.3f), _ => OnHead, Origin, inward: true);

        // The skirt below it: from the edge of the face round the back and on to the other edge, down over the ears to
        // the jaw hinge and the nape, swelling over the ears. Each row keeps its own edge, so the opening is a smooth curve:
        // (height, width, degrees from the front left open).
        var brow = MathF.Sin(Rad(rim)) * top;
        (float Z, float Width, float Open)[] skirt =
        [
            (Origin.Z + brow, MathF.Cos(Rad(rim)), 58), (Origin.Z + MathF.Sin(Rad(12)) * top, MathF.Cos(Rad(12)), 58), (Origin.Z, 1f, 58),
            (162f, 0.985f, 60), (158f, 0.96f, 66), (154f, 0.93f, 82), (jet ? 152.5f : 150.5f, 0.9f, 108),
        ];
        Vector3 Skirt(int r, int c, float size)
        {
            var open = Rad(skirt[r].Open);
            var theta = float.Lerp(open, MathF.Tau - open, (float)c / skirtColumns);
            float cos = MathF.Cos(theta), sin = MathF.Sin(theta), z = skirt[r].Z;
            var bulge = size < 1f ? 0f : earBulge * MathF.Exp(-(cos / 0.42f) * (cos / 0.42f)) * MathF.Exp(-((z - 160f) / 5.5f) * ((z - 160f) / 5.5f));
            var depth = cos > 0 ? front : back;
            return new Vector3(sin * (sides * skirt[r].Width * size + bulge), Origin.Y - cos * (depth * skirt[r].Width * size + bulge), z);
        }
        yield return Shell(skirt.Length - 1, skirtColumns, (r, c) => Skirt(r, c, 1f), (r, c) => Skirt(r, c, liner), (_, _) => true,
            (r, _) => FacePainter.Swatch(SwatchShell, ShellTone(skirt[r].Z)), _ => OnHead, Origin, wrap: false);
        yield return Shell(skirt.Length - 1, skirtColumns, (r, c) => Skirt(r, c, liner), (r, c) => Skirt(r, c, liner), (_, _) => true,
            (_, _) => FacePainter.Swatch(SwatchRubber, 0.3f), _ => OnHead, Origin with { Z = 160f }, wrap: false, inward: true);

        // The visor: down over the eyes, clear of the nose; or raised up the front of the helmet.
        const int visorColumns = 14;
        var span = Rad(70);
        float Theta(int c) => -span + 2 * span * c / visorColumns;
        if (spec.Visor)
        {
            // A curved lens: deepest in the middle, tapering to the pivots at the temples, its top edge under the housing.
            float[] heights = [1f, 0.55f, 0f, -0.5f, -1f];
            const float mid = 168.5f, halfHeight = 8f;
            Vector3 Lens(int r, int c, float grow)
            {
                var along = Theta(c) / span;
                var z = mid + heights[r] * halfHeight * (1 - 0.55f * along * along) + 1.6f * along * along;
                return new Vector3(MathF.Sin(Theta(c)) * (sides + 0.8f + grow), Origin.Y - MathF.Cos(Theta(c)) * (21.2f + grow - MathF.Abs(heights[r] - 0.1f) * 1.2f), z);
            }
            yield return Shell(heights.Length - 1, visorColumns, (r, c) => Lens(r, c, 0), (r, c) => Lens(r, c, -0.6f), (_, _) => true,
                (r, c) => FacePainter.Swatch(SwatchVisor, (r is 1 ? 0.75f : 0.25f) + 0.25f * MathF.Abs(c - visorColumns / 2f) / visorColumns), _ => OnHead, Origin);
        }
        else
        {
            float[] angles = [34, 44, 54, 64];
            yield return Shell(angles.Length - 1, visorColumns,
                (r, c) => Dome(Theta(c), Rad(angles[r]), sides, front, back, top, 1.08f),
                (r, c) => Dome(Theta(c), Rad(angles[r]), sides, front, back, top, 1.0f),
                (_, _) => true, (r, _) => FacePainter.Swatch(SwatchHousing, 0.3f + 0.25f * r), _ => OnHead, Origin);
        }
        // The visor's housing: a band across the brow, its ends at the temples.
        float[] band = [24, 33];
        yield return Shell(1, visorColumns,
            (r, c) => Dome(Theta(c) * 1.08f, Rad(band[r]), sides, front, back, top, 1.07f),
            (r, c) => Dome(Theta(c) * 1.08f, Rad(band[r]), sides, front, back, top, 0.98f),
            (_, _) => true, (r, _) => FacePainter.Swatch(SwatchHousing, 0.4f + 0.4f * r), _ => OnHead, Origin);

        if (!jet)
        {
            // The ear cups' caps set into the shell's bulges, a boom mic from the shell's left side to the mouth, and a
            // night-vision mount above the brow.
            foreach (var sign in new[] { -1f, 1f })
                yield return Tube([new(sign * 14f, -1.5f, 160f), new(sign * (sides + 1.3f), -1.5f, 160f)], [4.4f, 3.7f], SwatchShellDark, _ => OnHead, 10);
            yield return Tube([new(12.6f, -6.5f, 156.5f), new(13.6f, -8f, 156f)], [1.3f, 1.1f], SwatchRubber, _ => OnHead, 7);
            yield return Tube([new(13.4f, -8f, 156f), new(10.5f, -15f, 153.5f), new(5f, -21f, 154.6f), new(2.2f, -21.8f, 155.4f)], [0.5f, 0.45f, 0.4f, 0.4f], SwatchRubber, _ => OnHead, 6);
            yield return Tube([new(2.8f, -21.8f, 155.3f), new(1.2f, -22f, 155.6f)], [1.25f, 1.25f], SwatchRubber, _ => OnHead, 7);
            yield return Tube([new(0, -13.5f, 178.5f), new(0, -17.5f, 176.5f)], [2.2f, 1.7f], SwatchRubber, _ => OnHead, 7);
        }
        if (jet && spec.OxygenMask)
        {
            // Rings from the seal on the cheeks to the valve, then the hose down to the chest and the straps to the helmet.
            (float Y, float Z, float W, float H)[] rings = [(-13f, 155.5f, 8f, 8.6f), (-19f, 155f, 7f, 7.2f), (-23f, 154f, 4.4f, 4.6f), (-25f, 153.5f, 2.7f, 2.8f)];
            const int around = 14;
            Vector3 Ring(int r, int c, float grow)
            {
                var a = MathF.Tau * c / around;
                var (y, z, w, h) = rings[r];
                return new Vector3(MathF.Sin(a) * (w + grow), y, z + MathF.Cos(a) * (h + grow));
            }
            yield return Shell(rings.Length - 1, around, (r, c) => Ring(r, c, 0), (r, c) => Ring(r, c, -0.5f), (_, _) => true,
                (r, c) => FacePainter.Swatch(SwatchMask, 0.3f + 0.15f * r + 0.2f * MathF.Max(0, MathF.Cos(MathF.Tau * c / around))), _ => OnHead, new Vector3(0, -16, 155));
            yield return Tube([new(0, -25f, 152f), new(0, -25.6f, 153.5f), new(0, -25.6f, 155f)], [2.6f, 2.5f, 1.8f], SwatchRubber, _ => OnHead, 8);
            yield return Tube([new(1f, -25f, 150f), new(3.5f, -23f, 143f), new(6.5f, -20.5f, 133f), new(8f, -18.5f, 122f)], [1.25f, 1.2f, 1.2f, 1.2f], SwatchRubber,
                p => p.Z > 141f ? OnHead : [("Bip001-Spine", 1f)], 7);
            foreach (var sign in new[] { -1f, 1f })
                yield return Tube([new(sign * 7f, -15f, 159f), new(sign * 12.6f, -8f, 162.5f)], [0.5f, 0.5f], SwatchRubber, _ => OnHead, 5);
        }
    }

    /// <summary>A bandage round the head: two turns above the brow, fitted to the skull.</summary>
    public static PackPart HeadBandage(PackPart head)
    {
        const int columns = 18;
        float[] angles = [39, 31, 23, 16];
        var hits = new float[angles.Length, columns + 1];
        for (var r = 0; r < angles.Length; r++)
            for (var c = 0; c <= columns; c++)
                hits[r, c] = Reach(head, Origin, Dir(MathF.Tau * c / columns, Rad(angles[r])), 12f);
        return Shell(angles.Length - 1, columns,
            (r, c) => Origin + Dir(MathF.Tau * c / columns, Rad(angles[r])) * (hits[r, c] + 0.7f),
            (r, c) => Origin + Dir(MathF.Tau * c / columns, Rad(angles[r])) * hits[r, c],
            (_, _) => true, (r, _) => FacePainter.Swatch(SwatchBandage, r == 1 ? 0.55f : 0.9f), _ => OnHead, Origin);
    }

    /// <summary>The weights of the part's nearest vertex: a piece over a part moves with it.</summary>
    private static Func<Vector3, (string, float)[]> Follow(PackPart part, float side = 0) =>
        p =>
        {
            var best = 0; var distance = float.MaxValue;
            for (var v = 0; v < part.Positions.Count; v++)
            {
                if (part.Positions[v].X * side < 0) continue;   // a chap's inside edge must not follow the other leg
                var d = Vector3.DistanceSquared(part.Positions[v], p);
                if (d < distance) { distance = d; best = v; }
            }
            return part.Weights[best];
        };

    /// <summary>A vest over the torso: rays from the spine find the body, cut away round the arms, straps over the shoulders.</summary>
    public static PackPart LifeVest(PackPart body)
    {
        const int columns = 20;
        float[] heights = [103, 108, 114, 120, 127, 133, 139, 145, 149.5f];
        var center = new Vector3(0, 1.2f, 0);
        var hits = new float[heights.Length, columns + 1];
        for (var r = 0; r < heights.Length; r++)
            for (var c = 0; c <= columns; c++)
            {
                var theta = MathF.Tau * c / columns;
                var dir = new Vector3(MathF.Sin(theta), -MathF.Cos(theta), 0);
                // The farthest hit could be on an arm: never wider than the chest.
                var limit = 1f / MathF.Sqrt(dir.X * dir.X / (23f * 23f) + dir.Y * dir.Y / (18.5f * 18.5f));
                var hit = Hit(body, center + Vector3.UnitZ * heights[r], dir);
                // Above the shoulders the rays can miss the body altogether: the strap carries on from the row below.
                hits[r, c] = hit > 0 ? MathF.Min(hit, limit) : r > 0 ? MathF.Max(hits[r - 1, c] - 1.5f, 6f) : limit;
            }
        Vector3 Point(int r, int c, float grow)
        {
            var theta = MathF.Tau * c / columns;
            var dir = new Vector3(MathF.Sin(theta), -MathF.Cos(theta), 0);
            var bulge = r is >= 1 and <= 3 ? 0.9f : 0.2f;
            return center + Vector3.UnitZ * heights[r] + dir * (hits[r, c] + grow + bulge);
        }
        bool Keep(int r, int c)
        {
            var angle = (c + 0.5f) / columns * MathF.Tau;
            var x = MathF.Abs(MathF.Sin(angle)) * 22f;
            var frontGap = MathF.Cos(angle) > 0.97f;   // it closes down the front, over the suit's zip
            // Full rings to the armpits, then the straps over the shoulders.
            return r < 4 ? !frontGap : x is > 7f and < 15f;
        }
        return Shell(heights.Length - 1, columns, (r, c) => Point(r, c, 1.2f), (r, c) => Point(r, c, 0.2f), Keep,
            (r, c) => FacePainter.Swatch(r <= 3 ? SwatchVest : SwatchVestDark, r is 2 ? 0.85f : 0.5f), Follow(body), center + Vector3.UnitZ * 122f);
    }

    /// <summary>G-suit chaps: a band round the waist, the thighs and the shins, leaving the knees and the backs of the legs free.</summary>
    public static IEnumerable<PackPart> GSuit(PackPart legs)
    {
        const int columns = 12;
        foreach (var sign in new[] { -1f, 1f })
            foreach (var (heights, _) in new[] { (new float[] { 97, 88, 78, 68, 60 }, 0), (new float[] { 48, 42, 35, 27 }, 1) })
            {
                var centers = new Vector3[heights.Length];
                var size = new Vector2[heights.Length];
                for (var r = 0; r < heights.Length; r++)
                {
                    var near = legs.Positions.Where(p => MathF.Abs(p.Z - heights[r]) < 6f && p.X * sign > 1f).ToList();
                    if (near.Count == 0) near = legs.Positions.Where(p => p.X * sign > 1f).ToList();
                    float x0 = near.Min(p => p.X), x1 = near.Max(p => p.X), y0 = near.Min(p => p.Y), y1 = near.Max(p => p.Y);
                    // The pack's cargo pockets and holsters stick out: never wider than a leg.
                    centers[r] = new Vector3(sign * 13.5f, (y0 + y1) / 2, heights[r]);
                    size[r] = new Vector2(MathF.Min((x1 - x0) / 2, 11.5f), MathF.Min((y1 - y0) / 2, 13.5f));
                }
                Vector3 Point(int r, int c, float grow)
                {
                    var a = MathF.Tau * c / columns;
                    return centers[r] + new Vector3(MathF.Sin(a) * (size[r].X + grow), -MathF.Cos(a) * (size[r].Y + grow), 0);
                }
                var zip = columns / 2;
                yield return Shell(heights.Length - 1, columns, (r, c) => Point(r, c, 1.3f), (r, c) => Point(r, c, 0.1f),
                    (_, c) => c != zip && c != zip - 1,
                    (_, _) => FacePainter.Swatch(SwatchGSuit, 0.55f), Follow(legs, sign), centers[heights.Length / 2]);
            }
    }
}

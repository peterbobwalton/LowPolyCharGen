using System.Numerics;

namespace LowPolyCharGen.Parts;

/// <summary>
/// The realistic body's head as a designed low-poly mesh rather than a reduced one: reducing the dense
/// MakeHuman face left random slivers round the eyes, nose and mouth. Like the Toon Soldiers pack's
/// heads, this one is a few clean, angular planes (a skull, a brow, a wedge nose, cheek planes, the
/// jaw) with the features painted. Its layout is fixed: rows at the face's landmarks (chin, mouth, base
/// and tip of the nose, eyes, brow) by columns round the head, each corner put on the dense head
/// where a ray out from the head's axis meets it. So every character gets the same tidy layout, and
/// it still takes the shape of their own face.
/// </summary>
internal static class RealisticHead
{
    /// <summary>Columns round the head, degrees from the front (left side; the right mirrors them).</summary>
    private static readonly float[] Columns = [0f, 8f, 17f, 28f, 42f, 60f, 85f, 115f, 148f, 180f];

    /// <summary>Triangles the head is reduced to (the face keeps its layout; the back of the head and the neck give way).</summary>
    public const int HeadTriangles = 220;

    /// <summary>Height below which the dense body is kept: the head mesh overlaps it there, so no seam shows.</summary>
    public static float BodyCutZ(BuildContext c) => c.Paint.NeckBaseZ - 0.3f;

    public static void Build(BuildContext c, HumanBody.Fitted human, Func<int, Slot> slotOf,
        Func<int, bool> isHeadOrNeck, Func<int, bool> isEar, Action<Decimator.Result, Func<int, Slot>> emit)
    {
        var pos = human.Positions;
        var s = c.HeadScale;
        var paint = c.Paint;
        var tris = RealisticBody.TriangulateFaces(HumanBody.Default.BodyFaces)
            .Where(t => isHeadOrNeck(t[0]) && isHeadOrNeck(t[1]) && isHeadOrNeck(t[2])).ToArray();

        // The surface the rays meet: the dense head with the ears smoothed away (they are added as
        // simple flaps), so a ray at ear height finds the side of the skull.
        var surface = (Vector3[])pos.Clone();
        var neighbours = new Dictionary<int, HashSet<int>>();
        foreach (var t in tris)
            for (var k = 0; k < 3; k++)
            {
                if (!neighbours.TryGetValue(t[k], out var set)) neighbours[t[k]] = set = [];
                set.Add(t[(k + 1) % 3]);
                set.Add(t[(k + 2) % 3]);
            }
        var ears = neighbours.Keys.Where(isEar).ToArray();
        for (var pass = 0; pass < 40 && ears.Length > 0; pass++)
            foreach (var v in ears)
            {
                var avg = Vector3.Zero;
                foreach (var w in neighbours[v]) avg += surface[w];
                surface[v] = avg / neighbours[v].Count;
            }

        // Rows: heights of the face's landmarks, from the neck up to the crown.
        var eyeZ = paint.EyeZ;
        var noseTip = paint.NoseTipZ;
        var mouth = paint.MouthZ;
        var chin = c.ChinZ;
        var top = c.HeadZ(1f);
        float[] rows =
        [
            paint.NeckBaseZ - 1.5f,              // base of the neck, over the reduced body's cut edge
            chin - 1.6f * s,                     // throat and nape
            chin + 0.35f * s,                    // under the jaw, the point of the chin
            (chin + mouth) / 2f - 0.2f * s,      // chin
            mouth - 0.55f * s,                   // lower lip
            mouth,                               // mouth
            mouth + 0.6f * s,                    // upper lip
            noseTip - 0.75f * s,                 // under the nose
            noseTip,                             // tip of the nose, cheekbones
            (noseTip + eyeZ) / 2f,               // bridge of the nose
            eyeZ - 0.1f * s,                     // eyes
            eyeZ + 1.25f * s,                    // brow
            Geo.Lerp(eyeZ, top, 0.42f),          // forehead
            Geo.Lerp(eyeZ, top, 0.78f),          // crown
        ];

        // Each row's axis: the head's, leaning back to the neck's below the jaw.
        float AxisY(float z) => Geo.Lerp(paint.NeckCenter.Y, c.HeadCenter.Y, Geo.SmoothStep((z - (chin - 1.6f * s)) / (2f * s)));

        // Rays from the axis meet the surface; the furthest hit is the outside.
        Vector3? Cast(Vector3 origin, Vector3 dir)
        {
            float best = -1f;
            foreach (var t in tris)
            {
                Vector3 a = surface[t[0]], b = surface[t[1]], d = surface[t[2]];
                var e1 = b - a;
                var e2 = d - a;
                var p = Vector3.Cross(dir, e2);
                var det = Vector3.Dot(e1, p);
                if (MathF.Abs(det) < 1e-9f) continue;
                var inv = 1f / det;
                var o = origin - a;
                var u = Vector3.Dot(o, p) * inv;
                if (u < 0f || u > 1f) continue;
                var q = Vector3.Cross(o, e1);
                var v = Vector3.Dot(dir, q) * inv;
                if (v < 0f || u + v > 1f) continue;
                var dist = Vector3.Dot(e2, q) * inv;
                if (dist > best) best = dist;
            }
            return best > 0f ? origin + dir * best : null;
        }

        // The grid: left columns plus their mirrors (the middle ones shared).
        var angles = Columns.Concat(Columns.Skip(1).Take(Columns.Length - 2).Reverse().Select(a => -a)).ToArray();
        var grid = new Vector3[rows.Length, angles.Length];
        for (var r = 0; r < rows.Length; r++)
            for (var k = 0; k < angles.Length; k++)
            {
                var a = angles[k];
                if (a < 0f) continue;   // mirrored below
                var rad = a * MathF.PI / 180f;
                var dir = new Vector3(MathF.Sin(rad), -MathF.Cos(rad), 0f);
                var origin = new Vector3(0f, AxisY(rows[r]), rows[r]);
                var hit = Cast(origin, dir) ?? origin + dir * (c.HeadHalfWidth * 0.8f);
                if (a == 0f || a == 180f) hit.X = 0f;
                grid[r, k] = hit;
            }
        for (var k = 0; k < angles.Length; k++)
        {
            if (angles[k] >= 0f) continue;
            var mirror = Array.IndexOf(angles, -angles[k]);
            for (var r = 0; r < rows.Length; r++) grid[r, k] = grid[r, mirror] with { X = -grid[r, mirror].X };
        }
        // The bottom of the neck stands a little off the skin, over the reduced body's cut edge.
        for (var k = 0; k < angles.Length; k++)
        {
            var axis = new Vector3(0f, AxisY(rows[0]), rows[0]);
            var out0 = grid[0, k] - axis;
            out0.Z = 0f;
            if (out0.LengthSquared() > 1e-6f) grid[0, k] += Vector3.Normalize(out0) * 0.6f;
            var axis1 = new Vector3(0f, AxisY(rows[1]), rows[1]);
            var out1 = grid[1, k] - axis1;
            out1.Z = 0f;
            if (out1.LengthSquared() > 1e-6f) grid[1, k] += Vector3.Normalize(out1) * 0.2f;
        }

        // The crown: one point on the top of the head.
        var crown = surface[neighbours.Keys.Where(v => !isEar(v)).MaxBy(v => surface[v].Z)];
        crown.X = 0f;

        var points = new List<Vector3>();
        var index = new int[rows.Length, angles.Length];
        for (var r = 0; r < rows.Length; r++)
            for (var k = 0; k < angles.Length; k++)
            {
                index[r, k] = points.Count;
                points.Add(grid[r, k]);
            }
        var crownIndex = points.Count;
        points.Add(crown);

        var faces = new List<int[]>();
        var n = angles.Length;
        for (var r = 0; r + 1 < rows.Length; r++)
            for (var k = 0; k < n; k++)
            {
                var k1 = (k + 1) % n;
                int a = index[r, k], b = index[r, k1], cc = index[r + 1, k1], d = index[r + 1, k];
                // Each quad split along its shorter diagonal (only the left half is kept and mirrored).
                if (Vector3.DistanceSquared(points[a], points[cc]) <= Vector3.DistanceSquared(points[b], points[d]))
                {
                    faces.Add([a, b, cc]);
                    faces.Add([a, cc, d]);
                }
                else
                {
                    faces.Add([a, b, d]);
                    faces.Add([b, cc, d]);
                }
            }
        for (var k = 0; k < n; k++)
            faces.Add([index[rows.Length - 1, k], index[rows.Length - 1, (k + 1) % n], crownIndex]);

        // Faces wound outward: the first column runs to the character's left, so check one and flip all if needed.
        var f0 = faces[n * 2 * 6];
        var normal0 = Vector3.Cross(points[f0[1]] - points[f0[0]], points[f0[2]] - points[f0[0]]);
        var centre0 = (points[f0[0]] + points[f0[1]] + points[f0[2]]) / 3f;
        if (Vector3.Dot(normal0, centre0 - new Vector3(0f, c.HeadCenter.Y, centre0.Z)) < 0f)
            foreach (var f in faces) (f[1], f[2]) = (f[2], f[1]);

        // The face keeps its layout; the neck, the back and the top of the head give way to the
        // budget. Reduced as the body is: the left half, mirrored, so the two sides match exactly.
        var face = new HashSet<int>();
        for (var r = 2; r <= 11; r++)
            for (var k = 0; k < n; k++)
                if (MathF.Abs(angles[k]) <= 85f) face.Add(index[r, k]);
        var reduced = RealisticBody.SymmetricSimplify(points.ToArray(), faces.ToArray(), HeadTriangles, v => face.Contains(v) ? 1000f : 1f);

        // Each corner takes the bones, slot and colour of the dense vertex nearest it.
        var candidates = neighbours.Keys.ToArray();
        int Nearest(Vector3 p) => candidates.MinBy(v => Vector3.DistanceSquared(pos[v], p));
        var head = reduced with { Source = Array.ConvertAll(reduced.Positions, Nearest) };
        emit(head, slotOf);

        BuildEars(c, human, isEar, emit);
    }

    /// <summary>Simple ear flaps (like the pack's), from the dense ear's outline: a few triangles each.</summary>
    private static void BuildEars(BuildContext c, HumanBody.Fitted human, Func<int, bool> isEar, Action<Decimator.Result, Func<int, Slot>> emit)
    {
        var pos = human.Positions;
        var ear = Enumerable.Range(0, pos.Length).Where(v => isEar(v) && pos[v].X > 0f).ToArray();
        if (ear.Length < 8) return;
        var top = ear.MaxBy(v => pos[v].Z);
        var bottom = ear.MinBy(v => pos[v].Z);
        var back = ear.MaxBy(v => pos[v].Y - 0.3f * pos[v].Z);
        var outer = ear.MaxBy(v => pos[v].X);
        var frontTop = ear.Where(v => pos[v].Z > (pos[top].Z + pos[bottom].Z) / 2f).MinBy(v => pos[v].Y);
        var frontBottom = ear.Where(v => pos[v].Z <= (pos[top].Z + pos[bottom].Z) / 2f).MinBy(v => pos[v].Y);
        // Root points pulled in towards the skull so the flap grows out of it.
        Vector3 Root(int v) => pos[v] with { X = pos[v].X - 0.6f * c.HeadScale };
        var points = new List<Vector3> { Root(frontTop), Root(frontBottom), pos[top], pos[back], pos[bottom], pos[outer] };
        int[] source = [frontTop, frontBottom, top, back, bottom, outer];
        int[][] tris =
        [
            [0, 2, 5], [2, 3, 5], [3, 4, 5], [4, 1, 5], [1, 0, 5],    // outer face, round the rim
            [0, 1, 4], [0, 4, 3], [0, 3, 2],                          // back of the flap, against the head
        ];
        // Wind the outer face away from the head.
        var n = Vector3.Cross(points[2] - points[0], points[5] - points[0]);
        if (n.X < 0f) foreach (var t in tris) (t[1], t[2]) = (t[2], t[1]);
        var left = new Decimator.Result(points.ToArray(), tris, source);
        emit(left, _ => Slot.Head);
        // The right ear: the mirror image, with the mirror vertices' bones.
        var mirrored = points.Select(p => p with { X = -p.X }).ToArray();
        var mirrorSource = source.Select(v => ear.Length > 0 ? MirrorOf(pos, v) : v).ToArray();
        var rightTris = tris.Select(t => new[] { t[0], t[2], t[1] }).ToArray();
        emit(new Decimator.Result(mirrored, rightTris, mirrorSource), _ => Slot.Head);
    }

    private static int MirrorOf(Vector3[] pos, int v)
    {
        var p = pos[v] with { X = -pos[v].X };
        var best = v;
        var bestD = float.MaxValue;
        for (var w = 0; w < pos.Length; w++)
        {
            var d = Vector3.DistanceSquared(pos[w], p);
            if (d < bestD) { bestD = d; best = w; }
        }
        return best;
    }
}

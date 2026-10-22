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



    /// <summary>Height of the head mesh's lowest ring, on the neck just above the collar.</summary>
    public static float NeckRingZ(BuildContext c) => c.ChinZ - 3.8f * c.HeadScale;

    public static void Build(BuildContext c, HumanBody.Fitted human, Vector3[] detailed, Func<int, Slot> slotOf,
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

        // ---- the cage: a fixed low-poly head, like the Toon Soldiers pack's ----
        // Heights of the face's landmarks.
        var s2 = s;
        var eyeZ = paint.EyeZ;
        var noseTip = paint.NoseTipZ;
        var mouth = paint.MouthZ;
        var chin = c.ChinZ;
        var top = neighbours.Keys.Where(v => !isEar(v)).Max(v => surface[v].Z);
        float zF = Geo.Lerp(eyeZ, top, 0.55f), zB = eyeZ + 1.15f * s2, zE = eyeZ, zN = noseTip, zS = noseTip - 0.85f * s2,
            zW = noseTip - 0.45f * s2, zM = mouth, zC = (chin + mouth) / 2f - 0.15f * s2, zT = chin - 2.0f * s2, zR = chin - 3.8f * s2,
            zU = Geo.Lerp(zF, top, 0.6f);

        // Each row's axis: the head's, leaning back to the neck's below the jaw.
        float AxisY(float z) => Geo.Lerp(paint.NeckCenter.Y, c.HeadCenter.Y, Geo.SmoothStep((z - zT) / (2.5f * s2)));

        // Rays from the axis meet the surface; the furthest hit is the outside.
        Vector3? Cast(Vector3 origin, Vector3 dir, bool nearest = false)
        {
            float best = nearest ? float.MaxValue : -1f;
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
                if (nearest ? dist > 0f && dist < best : dist > best) best = dist;
            }
            return best > 0f && best < float.MaxValue ? origin + dir * best : null;
        }

        // Cage corners (left half and the middle line): a height, a direction round the head
        // (degrees from the front), a tilt up or down and how far to sink it below the surface.
        var points = new List<Vector3>();
        var names = new Dictionary<string, int>();
        void Corner(string name, float z, float azimuth, float tilt = 0f, float inset = 0f, float outset = 0f)
        {
            var a = azimuth * MathF.PI / 180f;
            var e = tilt * MathF.PI / 180f;
            var dir = new Vector3(MathF.Sin(a) * MathF.Cos(e), -MathF.Cos(a) * MathF.Cos(e), MathF.Sin(e));
            var origin = new Vector3(0f, AxisY(z), z);
            var hit = Cast(origin, dir) ?? origin + dir * c.HeadHalfWidth;
            hit -= dir * inset;
            hit += new Vector3(dir.X, dir.Y, 0f) * outset;
            if (azimuth is 0f or 180f) hit.X = 0f;
            names[name] = points.Count;
            points.Add(hit);
        }
        // Middle line, front and back.
        Corner("crown", top - 0.5f * s2, 0f, 88f);
        Corner("topF", zU, 0f);
        Corner("topL", zU, 58f);
        Corner("topBL", zU, 125f);
        Corner("topB", zU, 180f);
        Corner("fore", zF, 0f);
        Corner("brow", zB, 0f);
        Corner("bridge", zE, 0f, 0f, 0.15f * s2);
        Corner("tip", zN, 0f);
        Corner("base", zS, 0f);
        Corner("lip", zM, 0f);
        Corner("chin", zC, 0f);
        {
            // Under the chin: straight down from just behind the point of the chin.
            var chinFront = points[names["chin"]];
            var origin = new Vector3(0f, chinFront.Y + 1.4f * s2, zC);
            var hit = Cast(origin, -Vector3.UnitZ, nearest: true) ?? origin with { Z = chin };
            names["under"] = points.Count;
            points.Add(hit with { X = 0f });
        }
        Corner("throat", zT, 0f);
        Corner("backTop", zF, 180f);
        Corner("occiput", zE, 180f);
        Corner("nape", zS, 180f);
        Corner("napeLow", zT, 180f);
        Corner("neck", zR, 0f, 0f, 0f, 0.15f);
        Corner("neckBack", zR, 180f, 0f, 0f, 0.15f);
        Corner("neckF", zR, 40f, 0f, 0f, 0.15f);
        Corner("neckS", zR, 90f, 0f, 0f, 0.15f);
        Corner("neckB", zR, 140f, 0f, 0f, 0.15f);
        // Left side.
        Corner("foreL", zF, 40f);
        Corner("sideTop", zF, 85f);
        Corner("backUpper", zF, 135f);
        Corner("browIn", zB, 14f);
        Corner("browOut", zB, 36f);
        Corner("temple", zB, 62f);
        Corner("eyeIn", zE, 12f, 0f, 0.35f * s2);       // the eye band sits back under the brow
        Corner("eyeOut", zE, 33f, 0f, 0.3f * s2);
        Corner("sideEye", zE, 80f);
        Corner("backMid", zE, 130f);
        Corner("cheekbone", zN, 38f);
        Corner("earFront", zN, 96f);
        Corner("wing", zW, 15f);
        Corner("mouth", zM, 21f);
        Corner("jawAngle", zM - 0.6f * s2, 80f);
        Corner("backLow", zS, 130f);
        Corner("jawFront", zC, 26f);
        Corner("throatL", zT, 35f);
        Corner("neckSide", zT, 90f);
        Corner("neckBackL", zT, 145f);

        // The left half's triangles (each wound outward below).
        string[][] layout =
        [
            // top of the head
            ["fore", "foreL", "topF"], ["foreL", "topL", "topF"], ["foreL", "sideTop", "topL"], ["sideTop", "topBL", "topL"],
            ["sideTop", "backUpper", "topBL"], ["backUpper", "topB", "topBL"], ["backUpper", "backTop", "topB"],
            ["crown", "topF", "topL"], ["crown", "topL", "topBL"], ["crown", "topBL", "topB"],
            // forehead and temple
            ["fore", "brow", "browIn"], ["fore", "browIn", "foreL"], ["foreL", "browIn", "browOut"], ["foreL", "browOut", "temple"],
            ["foreL", "temple", "sideTop"], ["sideTop", "temple", "sideEye"],
            // side and back of the skull
            ["sideTop", "sideEye", "backMid"], ["sideTop", "backMid", "backUpper"], ["backUpper", "backMid", "occiput"], ["backUpper", "occiput", "backTop"],
            // brow shelf and eye band
            ["brow", "bridge", "eyeIn"], ["brow", "eyeIn", "browIn"], ["browIn", "eyeIn", "eyeOut"], ["browIn", "eyeOut", "browOut"],
            ["browOut", "eyeOut", "temple"], ["temple", "eyeOut", "sideEye"],
            // nose: a wedge
            ["bridge", "tip", "eyeIn"], ["eyeIn", "tip", "wing"], ["tip", "base", "wing"],
            // cheek: three planes
            ["eyeIn", "wing", "cheekbone"], ["eyeIn", "cheekbone", "eyeOut"], ["eyeOut", "cheekbone", "sideEye"],
            ["sideEye", "cheekbone", "earFront"], ["wing", "mouth", "cheekbone"], ["cheekbone", "mouth", "jawAngle"], ["cheekbone", "jawAngle", "earFront"],
            // behind the cheek
            ["sideEye", "earFront", "backMid"], ["backMid", "earFront", "backLow"], ["backMid", "backLow", "nape"], ["backMid", "nape", "occiput"],
            ["earFront", "jawAngle", "backLow"],
            // mouth and chin
            ["base", "lip", "mouth"], ["base", "mouth", "wing"], ["lip", "chin", "jawFront"], ["lip", "jawFront", "mouth"], ["mouth", "jawFront", "jawAngle"],
            // under the jaw
            ["chin", "under", "jawFront"], ["under", "throat", "throatL"], ["under", "throatL", "jawFront"], ["jawFront", "throatL", "jawAngle"],
            ["jawAngle", "throatL", "neckSide"], ["jawAngle", "neckSide", "backLow"], ["backLow", "neckSide", "neckBackL"], ["backLow", "neckBackL", "nape"],
            ["nape", "neckBackL", "napeLow"],
            // neck, down to the collar
            ["throat", "neck", "neckF"], ["throat", "neckF", "throatL"], ["throatL", "neckF", "neckS"], ["throatL", "neckS", "neckSide"],
            ["neckSide", "neckS", "neckB"], ["neckSide", "neckB", "neckBackL"], ["neckBackL", "neckB", "neckBack"], ["neckBackL", "neckBack", "napeLow"],
        ];

        // Mirror: the right half shares the middle line.
        var count = points.Count;
        var mirrorOf = new int[count];
        for (var i = 0; i < count; i++)
        {
            if (points[i].X == 0f) { mirrorOf[i] = i; continue; }
            mirrorOf[i] = points.Count;
            points.Add(points[i] with { X = -points[i].X });
        }
        var faces = new List<int[]>();
        foreach (var t in layout)
        {
            var f = t.Select(x => names[x]).ToArray();
            faces.Add(f);
            faces.Add(f.Select(i => mirrorOf[i]).ToArray());
        }
        // Wind every triangle outward, away from the head's axis.
        foreach (var f in faces)
        {
            var nrm = Vector3.Cross(points[f[1]] - points[f[0]], points[f[2]] - points[f[0]]);
            var centre = (points[f[0]] + points[f[1]] + points[f[2]]) / 3f;
            var axisPoint = new Vector3(0f, AxisY(centre.Z), Math.Clamp(centre.Z, zR, top - 4f * s2));
            if (Vector3.Dot(nrm, centre - axisPoint) < 0f) (f[1], f[2]) = (f[2], f[1]);
        }

        // Each corner takes the bones of the dense vertex nearest it.
        var candidates = neighbours.Keys.ToArray();
        int Nearest(Vector3 p) => candidates.MinBy(v => Vector3.DistanceSquared(pos[v], p));
        var head = new Decimator.Result(points.ToArray(), faces.ToArray(), points.Select(Nearest).ToArray());
        // Painted as head above the jaw and as neck below it (the collar, hood and nape hair are
        // drawn on both, so the edge between them does not show).
        var neckBelow = chin - 0.6f * s2;
        emit(head, v => pos[v].Z < neckBelow ? Slot.Neck : Slot.Head);

        // Hair that hangs past the ears covers them, as on the pack's long-haired heads.
        var hidden = c.Spec.Hair is HairStyle.Bob or HairStyle.Long && c.Spec.Hat == HatStyle.None;
        if (!hidden) BuildEars(c, human, surface, isEar, emit);
        paint.HeadSurface = null;   // SurfaceField(detailed, tris, s): the painted ToonFace reads better
    }

    /// <summary>
    /// A lookup of the detailed head (see <see cref="Texturing.PaintContext.HeadSurface"/>): its
    /// vertices with smooth normals in a grid, averaged over the few nearest a point.
    /// </summary>
    private static Func<Vector3, Vector3, (float, Vector3)> SurfaceField(Vector3[] pos, int[][] tris, float s)
    {
        var normals = new Dictionary<int, Vector3>();
        foreach (var t in tris)
        {
            var n = Vector3.Cross(pos[t[1]] - pos[t[0]], pos[t[2]] - pos[t[0]]);
            foreach (var v in t) normals[v] = normals.GetValueOrDefault(v) + n;
        }
        const float cell = 1.2f;
        static (int, int, int) Key(Vector3 p) => ((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell), (int)MathF.Floor(p.Z / cell));
        var grid = new Dictionary<(int, int, int), List<(Vector3 P, Vector3 N)>>();
        foreach (var (v, n) in normals)
        {
            var k = Key(pos[v]);
            if (!grid.TryGetValue(k, out var list)) grid[k] = list = [];
            list.Add((pos[v], n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitZ));
        }
        var radius = 0.8f * s;
        return (p, _) =>
        {
            // The detailed surface near p, smoothed over a few centimetres: its mean point and normal.
            // Independent of the low-poly face p is on, so the paint runs on across face edges.
            var (kx, ky, kz) = Key(p);
            float wsum = 0f;
            Vector3 mean = Vector3.Zero, normal = Vector3.Zero;
            var reach = (int)MathF.Ceiling(2.5f * radius / cell);
            for (var dx = -reach; dx <= reach; dx++)
                for (var dy = -reach; dy <= reach; dy++)
                    for (var dz = -reach; dz <= reach; dz++)
                        if (grid.TryGetValue((kx + dx, ky + dy, kz + dz), out var list))
                            foreach (var (q, n) in list)
                            {
                                var d2 = Vector3.DistanceSquared(q, p);
                                if (d2 > 6.25f * radius * radius) continue;
                                var w = MathF.Exp(-d2 / (radius * radius));
                                wsum += w;
                                mean += q * w;
                                normal += n * w;
                            }
            if (wsum < 1e-6f) return (0f, Vector3.UnitZ);
            mean /= wsum;
            normal = Vector3.Normalize(normal);
            // Positive where the detailed surface rises above the low-poly one, negative over hollows.
            return (Vector3.Dot(mean - p, normal), normal);
        };
    }

    /// <summary>
    /// Chunky block ears, like the pack's: the dense ear's outline (front top, top, back, bottom,
    /// front bottom) as a flat outer face, walled back to the skull; about a dozen triangles each.
    /// The painter draws the bowl and canal on the outer face (Slot.Ear).
    /// </summary>
    private static void BuildEars(BuildContext c, HumanBody.Fitted human, Vector3[] skull, Func<int, bool> isEar, Action<Decimator.Result, Func<int, Slot>> emit)
    {
        var pos = human.Positions;
        var s = c.HeadScale;
        // The ear proper: what stands off the skull (the head with the ears smoothed away).
        var ear = Enumerable.Range(0, pos.Length).Where(v => isEar(v) && pos[v].X > 0f).ToArray();
        if (ear.Length < 8) return;
        var top = ear.MaxBy(v => pos[v].Z);
        var bottom = ear.MinBy(v => pos[v].Z);
        var midZ = (pos[top].Z + pos[bottom].Z) / 2f;
        var back = ear.MaxBy(v => pos[v].Y);
        var frontTop = ear.Where(v => pos[v].Z > midZ).MinBy(v => pos[v].Y);
        var frontBottom = ear.Where(v => pos[v].Z <= midZ).MinBy(v => pos[v].Y);
        int[] outline = [frontTop, top, back, bottom, frontBottom];
        // The outer face: the outline a little in from the real ear, hugging the head; the root on the skull.
        var outer = outline.Select(v => pos[v] with { X = skull[v].X + 0.8f * (pos[v].X - skull[v].X) }).ToArray();
        var root = outline.Select(v => pos[v] with { X = skull[v].X - 0.3f * s }).ToArray();
        var centre = outer.Aggregate(Vector3.Zero, (a, b) => a + b) / outer.Length;
        // A little smaller than the detailed ear, as the pack draws them.
        for (var i = 0; i < outer.Length; i++)
        {
            outer[i] = Vector3.Lerp(outer[i], centre with { X = outer[i].X }, 0.12f);
            root[i] = Vector3.Lerp(root[i], centre with { X = root[i].X }, 0.25f);
        }
        var points = outer.Concat(root).ToList();
        var source = outline.Concat(outline).ToArray();
        var tris = new List<int[]> { new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 0, 3, 4 } };
        for (var i = 0; i < 4; i++) { tris.Add([i, i + 1, i + 6]); tris.Add([i, i + 6, i + 5]); }
        // Wind each triangle away from the middle of the ear.
        var inside = points.Aggregate(Vector3.Zero, (a, b) => a + b) / points.Count;
        foreach (var t in tris)
        {
            var nrm = Vector3.Cross(points[t[1]] - points[t[0]], points[t[2]] - points[t[0]]);
            var mid = (points[t[0]] + points[t[1]] + points[t[2]]) / 3f;
            if (Vector3.Dot(nrm, mid - inside) < 0f) (t[1], t[2]) = (t[2], t[1]);
        }
        emit(new Decimator.Result(points.ToArray(), tris.ToArray(), source), _ => Slot.Ear);
        var mirrored = points.Select(p => p with { X = -p.X }).ToArray();
        var mirrorSource = source.Select(v => MirrorOf(pos, v)).ToArray();
        emit(new Decimator.Result(mirrored, tris.Select(t => new[] { t[0], t[2], t[1] }).ToArray(), mirrorSource), _ => Slot.Ear);

        // Where the painter draws the ear: its outer face's centre and axes.
        c.Paint.EarCenter = centre;
        c.Paint.EarAcross = Vector3.UnitY;
        c.Paint.EarUp = Vector3.UnitZ;
        c.Paint.EarScale = (pos[top].Z - pos[bottom].Z) / 5f;
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

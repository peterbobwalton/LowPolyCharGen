using System.Numerics;

namespace LowPolyCharGen.Parts;

/// <summary>
/// Adds the realistic body (<see cref="BuildContext.Human"/>) to the mesh: the dense fitted body
/// decimated to <see cref="BodyTriangles"/>, with the face and hands keeping more of their detail,
/// plus the eyeballs. Each triangle is painted as the part of the body its bone says it is.
/// </summary>
internal static class RealisticBody
{
    /// <summary>Triangles the body is reduced to (the eyes come on top).</summary>
    public const int BodyTriangles = 2600;

    public static void Build(BuildContext c)
    {
        var human = c.Human ?? throw new InvalidOperationException("No fitted body.");
        var source = HumanBody.Default;
        var bones = source.Bones;
        var m = c.Mesh;

        var neckSplitZ = human.Joint("joint-neck").Z + 3f;
        Slot SlotOf(int v)
        {
            var name = bones[human.DominantSourceBone[v]].Name;
            if (name == "head") return Slot.Head;
            // The lower neck counts as body, so the painter draws the neckline inside it, not along the edge.
            if (name == "neck_01") return human.Positions[v].Z < neckSplitZ ? Slot.Body : Slot.Neck;
            if (name.StartsWith("upperarm") || name.StartsWith("lowerarm")) return Slot.Arm;
            if (name.StartsWith("thigh") || name.StartsWith("calf")) return Slot.Leg;
            if (name.StartsWith("foot") || name.StartsWith("ball")) return Slot.Foot;
            if (name.StartsWith("hand") || name.StartsWith("thumb") || name.StartsWith("index") || name.StartsWith("middle")
                || name.StartsWith("ring") || name.StartsWith("pinky")) return Slot.Hand;
            return Slot.Body;
        }

        // The face and hands hold their shape best if their error counts for more.
        float Importance(int v)
        {
            var slot = SlotOf(v);
            var p = human.Positions[v];
            if (slot == Slot.Head) return p.Y < c.HeadCenter.Y - 2f ? 6f : 1.5f;
            if (slot == Slot.Hand) return 2.5f;
            if (slot == Slot.Foot) return 1.5f;
            return 1f;
        }

        var bodyTris = Triangulate(source.BodyFaces);
        var body = Decimator.Simplify(human.Positions, bodyTris, new Decimator.Options { TargetTriangles = BodyTriangles, Importance = Importance });
        Emit(c, m, body, SlotOf);

        // Clothing that stands off the skin: shells offset from the dense body, smoothed, cut at their
        // hems and decimated. The skin they cover is dropped later as hidden.
        BuildClothing(c, human, SlotOf);

        // Eyeballs: small already; reduced a little.
        var eyes = Decimator.Simplify(human.Positions, Triangulate(source.EyeFaces), new Decimator.Options { TargetTriangles = 96 });
        Emit(c, m, eyes, _ => Slot.Head);

        // Nails for the painter, from the finger joints (left hand; it mirrors by |x|).
        var nails = new List<HandTemplate.Nail>();
        for (var f = 1; f <= 5; f++)
        {
            var tip = human.Joint($"joint-l-finger-{f}-4");
            var last = human.Joint($"joint-l-finger-{f}-3");
            var along = Vector3.Normalize(tip - last);
            // The nail faces out of the back of the hand: up in the T-pose, turned for the thumb.
            var up = f == 1 ? Vector3.Normalize(new Vector3(0, -0.75f, 0.66f)) : Vector3.UnitZ;
            up = Vector3.Normalize(up - along * Vector3.Dot(up, along));
            var radius = 0.62f * human.Scale;
            nails.Add(new HandTemplate.Nail(f == 1 ? "thumb" : $"finger{f}", tip - along * radius * 0.6f, along, up, radius));
        }
        c.Paint.Nostril = new Vector2(0.9f / c.HeadScale, (c.Paint.NoseTipZ - 0.045f * c.HeadHeight - c.ChinZ) / c.HeadScale);
        c.Paint.ModelledHands = true;
        c.Paint.HandOrigin = Vector3.Zero;
        c.Paint.HandScale = 1f;
        c.Paint.Nails = nails;
    }

    private static void BuildClothing(BuildContext c, HumanBody.Fitted human, Func<int, Slot> slotOf)
    {
        var spec = c.Spec;
        var paint = c.Paint;
        var bones = HumanBody.Default.Bones;
        string Bone(int v) => bones[human.DominantSourceBone[v]].Name;
        bool IsTrunk(string b) => b is "pelvis" or "spine_01" or "spine_02" or "spine_03" || b.StartsWith("clavicle");
        bool IsArm(string b) => b.StartsWith("upperarm") || b.StartsWith("lowerarm");
        bool IsLeg(string b) => b.StartsWith("thigh") || b.StartsWith("calf");
        var pos = human.Positions;

        // Trousers (and shorts): legs and hips up to the belt; looser down the leg as the garment is.
        if (paint.TrouserEndZ < paint.BeltBottomZ)
        {
            var crotch = c.Torso.Sections[0].Z;
            Shell(c, human, v => (IsLeg(Bone(v)) || (IsTrunk(Bone(v)) && pos[v].Z < paint.BeltBottomZ)) && pos[v].Z > paint.TrouserEndZ - 3f,
                p =>
                {
                    var leg = c.LegL.Clothing(p.Z);
                    var offset = Geo.Lerp(leg, 0.3f, Geo.SmoothStep((p.Z - (crotch - 6f)) / 12f));
                    // Less between the thighs, where the two legs would run into each other.
                    return offset * Geo.Lerp(0.35f, 1f, Geo.SmoothStep((MathF.Abs(p.X) - 3f) / 5f));
                },
                smoothing: 4, keep: (a, b, d) => MathF.Min(a.Z, MathF.Min(b.Z, d.Z)) >= paint.TrouserEndZ, triangles: 640, Slot.Leg);
        }

        // Sleeves, and for a jacket or hoodie the whole loose top.
        var jacket = spec.Torso is TorsoStyle.Jacket or TorsoStyle.Hoodie;
        if (paint.SleeveEndX > c.ShoulderX || jacket)
        {
            var end = paint.SleeveEndX;
            var neckTop = paint.NeckBaseZ;
            Shell(c, human,
                v => jacket
                    ? (IsTrunk(Bone(v)) || IsArm(Bone(v))) && pos[v].Z > paint.HemZ - 2f && pos[v].Z < neckTop + 1f && MathF.Abs(pos[v].X) < end + 2f
                    : IsArm(Bone(v)) && MathF.Abs(pos[v].X) < end + 2f,
                p =>
                {
                    var x = MathF.Abs(p.X);
                    var sleeve = c.ArmL.Clothing(MathF.Max(x, c.ShoulderX + 6f));
                    if (jacket)
                        return x > c.ShoulderX + 2f ? sleeve : 0.9f * (1f - Geo.SmoothStep((p.Z - (neckTop - 4f)) / 4f));
                    return sleeve * Geo.SmoothStep((x - c.ShoulderX) / 6f);   // grows out of the painted shoulder
                },
                smoothing: 3,
                keep: (a, b, d) => MathF.Max(MathF.Abs(a.X), MathF.Max(MathF.Abs(b.X), MathF.Abs(d.X))) <= end
                    && (!jacket || MathF.Min(a.Z, MathF.Min(b.Z, d.Z)) >= paint.HemZ),
                triangles: jacket ? 900 : 260, slot: jacket ? Slot.Body : Slot.Arm, armSlot: jacket ? Slot.Arm : null, isArm: v => IsArm(Bone(v)));
        }

        // Boot shafts above the boot (tall boots, or boots worn under bloused trousers).
        if (paint.BootTopZ > 13f)
            Shell(c, human, v => Bone(v).StartsWith("calf") && pos[v].Z < paint.BootTopZ + 2f,
                _ => 0.7f, smoothing: 3, keep: (a, b, d) => MathF.Max(a.Z, MathF.Max(b.Z, d.Z)) <= paint.BootTopZ, triangles: 160, Slot.Foot);
    }

    /// <summary>
    /// A garment as a shell over part of the dense body: the faces whose corners all pass
    /// <paramref name="region"/>, pushed out along the surface normal by <paramref name="offset"/>,
    /// smoothed (so it skims over toes and creases instead of following them, but never sinks below
    /// the offset), cut to the faces <paramref name="keep"/> accepts and decimated.
    /// </summary>
    private static void Shell(BuildContext c, HumanBody.Fitted human, Func<int, bool> region, Func<Vector3, float> offset,
        int smoothing, Func<Vector3, Vector3, Vector3, bool> keep, int triangles, Slot slot, Slot? armSlot = null, Func<int, bool>? isArm = null)
    {
        var faces = Triangulate(HumanBody.Default.BodyFaces).Where(f => region(f[0]) && region(f[1]) && region(f[2])).ToArray();
        if (faces.Length == 0) return;
        var pos = human.Positions;
        var normals = new Dictionary<int, Vector3>();
        foreach (var f in faces)
        {
            var n = Vector3.Cross(pos[f[1]] - pos[f[0]], pos[f[2]] - pos[f[0]]);
            foreach (var v in f) normals[v] = normals.GetValueOrDefault(v) + n;
        }
        var moved = new Dictionary<int, Vector3>();
        foreach (var (v, n) in normals) moved[v] = pos[v] + Vector3.Normalize(n) * offset(pos[v]);
        var neighbours = new Dictionary<int, HashSet<int>>();
        foreach (var f in faces)
            for (var k = 0; k < 3; k++)
            {
                if (!neighbours.TryGetValue(f[k], out var set)) neighbours[f[k]] = set = [];
                set.Add(f[(k + 1) % 3]);
                set.Add(f[(k + 2) % 3]);
            }
        for (var i = 0; i < smoothing; i++)
        {
            var next = new Dictionary<int, Vector3>(moved.Count);
            foreach (var (v, p) in moved)
            {
                var avg = Vector3.Zero;
                foreach (var w in neighbours[v]) avg += moved[w];
                avg /= neighbours[v].Count;
                var q = Vector3.Lerp(p, avg, 0.5f);
                // Never closer to the skin than the offset asks for.
                var n = Vector3.Normalize(normals[v]);
                var depth = Vector3.Dot(q - pos[v], n);
                var want = offset(pos[v]);
                if (depth < want) q += n * (want - depth);
                next[v] = q;
            }
            moved = next;
        }
        var kept = faces.Where(f => keep(moved[f[0]], moved[f[1]], moved[f[2]])).ToArray();
        if (kept.Length == 0) return;
        var local = new Dictionary<int, int>();
        var points = new List<Vector3>();
        var source = new List<int>();
        var tris = kept.Select(f => f.Select(v =>
        {
            if (!local.TryGetValue(v, out var i))
            {
                i = local[v] = points.Count;
                points.Add(moved[v]);
                source.Add(v);
            }
            return i;
        }).ToArray()).ToArray();
        var piece = Decimator.Simplify(points, tris, new Decimator.Options { TargetTriangles = triangles });
        // Back to the body's vertex numbering, for the weights.
        var result = piece with { Source = piece.Source.Select(i => source[i]).ToArray() };
        Emit(c, c.Mesh, result, v => armSlot is { } a && isArm!(v) ? a : slot);
    }

    private static int[][] Triangulate(int[][] faces)
    {
        var tris = new List<int[]>(faces.Length * 2);
        foreach (var f in faces)
            for (var k = 1; k + 1 < f.Length; k++) tris.Add([f[0], f[k], f[k + 1]]);
        return tris.ToArray();
    }

    /// <summary>
    /// Adds a decimated piece: vertices with the fitted weights of the vertex each was kept from, and
    /// the triangles in UV patches - grouped by slot and by the axis they face, split into connected
    /// pieces, each flattened along its axis.
    /// </summary>
    private static void Emit(BuildContext c, MeshBuilder m, Decimator.Result piece, Func<int, Slot> slotOf)
    {
        var human = c.Human!;
        var ids = new int[piece.Positions.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            var w = human.Weights[piece.Source[i]];
            ids[i] = m.AddVertex(piece.Positions[i], _ => w);
        }

        var axes = new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ };
        var tris = piece.Triangles;
        var slot = new Slot[tris.Length];
        var axis = new int[tris.Length];
        for (var f = 0; f < tris.Length; f++)
        {
            var t = tris[f];
            // The slot most of its corners belong to.
            var s0 = slotOf(piece.Source[t[0]]);
            var s1 = slotOf(piece.Source[t[1]]);
            var s2 = slotOf(piece.Source[t[2]]);
            slot[f] = s1 == s2 ? s1 : s0;
            var n = Vector3.Cross(piece.Positions[t[1]] - piece.Positions[t[0]], piece.Positions[t[2]] - piece.Positions[t[0]]);
            var best = 0;
            for (var x = 1; x < axes.Length; x++)
                if (Vector3.Dot(n, axes[x]) > Vector3.Dot(n, axes[best])) best = x;
            axis[f] = best;
        }
        var byEdge = new Dictionary<(int, int), List<int>>();
        for (var f = 0; f < tris.Length; f++)
            for (var e = 0; e < 3; e++)
            {
                var (a, b) = (tris[f][e], tris[f][(e + 1) % 3]);
                var key = a < b ? (a, b) : (b, a);
                if (!byEdge.TryGetValue(key, out var list)) byEdge[key] = list = [];
                list.Add(f);
            }
        var done = new bool[tris.Length];
        for (var f0 = 0; f0 < tris.Length; f0++)
        {
            if (done[f0]) continue;
            var patch = new List<int[]>();
            var stack = new Stack<int>([f0]);
            done[f0] = true;
            while (stack.Count > 0)
            {
                var f = stack.Pop();
                var t = tris[f];
                patch.Add([ids[t[0]], ids[t[1]], ids[t[2]]]);
                for (var e = 0; e < 3; e++)
                {
                    var (a, b) = (t[e], t[(e + 1) % 3]);
                    foreach (var g in byEdge[a < b ? (a, b) : (b, a)])
                        if (!done[g] && axis[g] == axis[f0] && slot[g] == slot[f0])
                        {
                            done[g] = true;
                            stack.Push(g);
                        }
                }
            }
            m.AddPatch(slot[f0], axes[axis[f0]], patch);
        }
    }
}

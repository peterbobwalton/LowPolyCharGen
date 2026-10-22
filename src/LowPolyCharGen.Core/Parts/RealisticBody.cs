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

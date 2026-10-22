using System.Numerics;
using LowPolyCharGen.Texturing;

namespace LowPolyCharGen.Parts;

/// <summary>
/// Adds the realistic body (<see cref="BuildContext.Human"/>) to the mesh: the dense fitted body
/// decimated to <see cref="BodyTriangles"/>, with the face and hands keeping more of their detail,
/// plus the eyeballs. Each triangle is painted as the part of the body its bone says it is.
/// </summary>
internal static class RealisticBody
{
    /// <summary>Triangles the body is reduced to (the eyes come on top).</summary>
    public const int BodyTriangles = 3000;

    public static void Build(BuildContext c)
    {
        var human = c.Human ?? throw new InvalidOperationException("No fitted body.");
        var source = HumanBody.Default;
        PoseHands(c, human);
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
        var mouth = MouthCentre(human);
        var hs = c.HeadScale;
        float Importance(int v)
        {
            var slot = SlotOf(v);
            var p = human.Positions[v];
            if (slot == Slot.Head)
            {
                // The lips and nostrils most of all: they are small and the face is read by them.
                if (p.Y < mouth.Y + 1.2f * hs)
                {
                    var dx = p.X / (3.4f * hs);
                    var dz = (p.Z - mouth.Z) / (1.8f * hs);
                    if (dx * dx + dz * dz < 1f) return 90f;
                    if (MathF.Abs(p.X) < 2.2f * hs && p.Z > mouth.Z + 1f * hs && p.Z < mouth.Z + 4.2f * hs) return 60f;
                }
                // The face carries the character: it keeps by far the most detail, the ears next.
                if (MathF.Abs(p.X) > c.HeadHalfWidth * 0.92f && MathF.Abs(p.Y - c.HeadCenter.Y) < 4f) return 10f;
                return p.Y < c.HeadCenter.Y - 2f ? 30f : 3f;
            }
            if (slot == Slot.Hand) return 3.5f;
            if (slot == Slot.Foot) return 1.5f;
            return 1f;
        }

        var bodyTris = Triangulate(source.BodyFaces);
        var body = SymmetricSimplify(human.Positions, bodyTris, BodyTriangles, Importance);
        // The reduced body shades with the dense body's normals, so its flat facets do not show.
        var denseNormals = VertexNormals(human.Positions, bodyTris, smoothing: 12);
        Emit(c, m, body, SlotOf, Array.ConvertAll(body.Source, v => denseNormals[v]));

        // Clothing that stands off the skin: shells offset from the dense body, smoothed, cut at their
        // hems and decimated. The skin they cover is dropped later as hidden.
        BuildClothing(c, human, SlotOf);

        // Eyeballs: small already; reduced a little.
        var eyes = SymmetricSimplify(human.Positions, Triangulate(source.EyeFaces), 120, null);
        Emit(c, m, eyes, _ => Slot.Eye);
        var eyeCentre = human.Joint("joint-l-eye");
        var eyeVerts = source.EyeFaces.SelectMany(f => f).Distinct().Where(v => human.Positions[v].X > 0).ToArray();
        c.Paint.ModelledFace = true;
        c.Paint.EyeCenter = eyeCentre;
        c.Paint.EyeRadius = eyeVerts.Length > 0 ? eyeVerts.Average(v => Vector3.Distance(human.Positions[v], eyeCentre)) : 1.2f;

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
        // The left ear, for the painted sideburn in front of it.
        var ear = Enumerable.Range(0, human.Positions.Length).Where(v =>
        {
            var p = human.Positions[v];
            var t = (p.Z - c.ChinZ) / c.HeadHeight;
            return SlotOf(v) == Slot.Head && p.X > c.HeadHalfWidth * 0.9f && MathF.Abs(p.Y - c.HeadCenter.Y) < 4f * c.HeadScale && t > 0.3f && t < 0.68f;
        }).ToArray();
        if (ear.Length > 0)
        {
            var centre = Vector3.Zero;
            foreach (var v in ear) centre += human.Positions[v];
            c.Paint.EarCenter = centre / ear.Length;
        }
        if (MeasureLips(c, human, bodyTris) is { } lips)
        {
            c.Paint.Lips = lips;
            c.Paint.MouthZ = lips.At(0f).Seam;
        }
        c.Paint.Nostril = new Vector2(0.9f / c.HeadScale, (c.Paint.NoseTipZ - 0.045f * c.HeadHeight - c.ChinZ) / c.HeadScale);
        c.Paint.ModelledHands = true;
        c.Paint.HandOrigin = Vector3.Zero;
        c.Paint.HandScale = 1f;
        c.Paint.Nails = nails;
    }

    /// <summary>The middle of the mouth: where the vertices the lip targets move most are.</summary>
    private static Vector3 MouthCentre(HumanBody.Fitted human)
    {
        var targets = HumanBody.Default.Targets;
        var pos = human.Positions;
        Vector3 Centre(string name)
        {
            if (!targets.TryGetValue(name, out var t) || t.Deltas.Length == 0) return Vector3.Zero;
            var max = t.Deltas.Max(d => d.Length());
            var sum = Vector3.Zero;
            var n = 0;
            for (var i = 0; i < t.Vertices.Length; i++)
                if (t.Deltas[i].Length() > 0.5f * max)
                {
                    sum += pos[t.Vertices[i]];
                    n++;
                }
            return n > 0 ? sum / n : Vector3.Zero;
        }
        return (Centre("mouth-upperlip-volume-incr") + Centre("mouth-lowerlip-volume-incr")) / 2f;
    }

    /// <summary>
    /// Measures the lips off the dense face so the painted lips sit on the modelled ones: the face is
    /// rasterised as a depth map seen from the front, and in each column out from the middle the
    /// deepest groove between the two bulges of the lips is where they meet; each lip's edge is where
    /// the surface has fallen back a little from the lip's most forward point.
    /// </summary>
    private static LipProfile? MeasureLips(BuildContext c, HumanBody.Fitted human, int[][] tris)
    {
        var targets = HumanBody.Default.Targets;
        if (!targets.TryGetValue("mouth-upperlip-volume-incr", out var upperTarget)
            || !targets.TryGetValue("mouth-lowerlip-volume-incr", out var lowerTarget)) return null;
        var pos = human.Positions;
        var mouth = MouthCentre(human);
        var s = c.HeadScale;

        // Depth map of the front of the face round the mouth (left half; the face is symmetric).
        var step = 0.04f * s;
        var width = 4.2f * s;
        var zLo = mouth.Z - 3f * s;
        var cols = (int)(width / step) + 1;
        var rows = (int)(6f * s / step) + 1;
        var depth = new float[cols, rows];
        for (var i = 0; i < cols; i++)
            for (var j = 0; j < rows; j++) depth[i, j] = float.MaxValue;
        foreach (var t in tris)
        {
            Vector3 a = pos[t[0]], b = pos[t[1]], d = pos[t[2]];
            if (MathF.Max(a.X, MathF.Max(b.X, d.X)) < -step || MathF.Min(a.X, MathF.Min(b.X, d.X)) > width + step) continue;
            if (MathF.Max(a.Z, MathF.Max(b.Z, d.Z)) < zLo || MathF.Min(a.Z, MathF.Min(b.Z, d.Z)) > zLo + rows * step) continue;
            if (MathF.Min(a.Y, MathF.Min(b.Y, d.Y)) > mouth.Y + 2.5f * s) continue;
            var den = (b.Z - d.Z) * (a.X - d.X) + (d.X - b.X) * (a.Z - d.Z);
            if (MathF.Abs(den) < 1e-9f) continue;
            var i0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, d.X)) / step));
            var i1 = Math.Min(cols - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, d.X)) / step));
            var j0 = Math.Max(0, (int)MathF.Floor((MathF.Min(a.Z, MathF.Min(b.Z, d.Z)) - zLo) / step));
            var j1 = Math.Min(rows - 1, (int)MathF.Ceiling((MathF.Max(a.Z, MathF.Max(b.Z, d.Z)) - zLo) / step));
            for (var i = i0; i <= i1; i++)
                for (var j = j0; j <= j1; j++)
                {
                    float x = i * step, z = zLo + j * step;
                    var w0 = ((b.Z - d.Z) * (x - d.X) + (d.X - b.X) * (z - d.Z)) / den;
                    var w1 = ((d.Z - a.Z) * (x - d.X) + (a.X - d.X) * (z - d.Z)) / den;
                    var w2 = 1f - w0 - w1;
                    if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                    var y = w0 * a.Y + w1 * b.Y + w2 * d.Y;
                    if (y < depth[i, j]) depth[i, j] = y;
                }
        }

        var reach = (int)(1.3f * s / step);
        // The line between the lips is a narrow, sharp crease; a groove found over a short span picks
        // it out from the broad, shallow hollows above and below the mouth.
        var narrow = Math.Max(2, (int)(0.3f * s / step));
        var seam = new float[cols];
        var upper = new float[cols];
        var lower = new float[cols];
        var groove = new float[cols];
        var mid = (int)((mouth.Z - zLo) / step);
        var previous = mid;
        var fallBack = 0.12f * s;
        for (var i = 0; i < cols; i++)
        {
            // The deepest groove near the last column's, so the line follows the mouth out to its corners.
            var best = previous;
            var bestDepth = float.MinValue;
            // The middle column looks over the whole mouth; the others close to their neighbour.
            var search = (int)((i == 0 ? 1.2f : 0.12f) * s / step);
            for (var j = Math.Max(reach, previous - search); j <= Math.Min(rows - 1 - reach, previous + search); j++)
            {
                if (depth[i, j] == float.MaxValue) continue;
                float above = float.MaxValue, below = float.MaxValue;
                for (var k = 1; k <= narrow; k++)
                {
                    above = MathF.Min(above, depth[i, j + k]);
                    below = MathF.Min(below, depth[i, j - k]);
                }
                var g = MathF.Min(depth[i, j] - above, depth[i, j] - below);
                if (g > bestDepth)
                {
                    bestDepth = g;
                    best = j;
                }
            }
            previous = best;
            groove[i] = bestDepth;
            seam[i] = zLo + best * step;

            // Each lip's edge: past its most forward point, where the surface has fallen back.
            int Edge(int dir)
            {
                var front = best + dir;
                for (var k = 1; k <= reach; k++)
                    if (depth[i, best + dir * k] < depth[i, front]) front = best + dir * k;
                var j = front;
                while (MathF.Abs(j - best) < reach + 6 && j + dir >= 0 && j + dir < rows && depth[i, j + dir] < depth[i, front] + fallBack) j += dir;
                return j;
            }
            upper[i] = zLo + Edge(1) * step;
            lower[i] = zLo + Edge(-1) * step;
        }

        // The corner: where the groove between the lips fades out.
        var corner = cols - 1;
        for (var i = (int)(1.2f * s / step); i < cols; i++)
            if (groove[i] < 0.03f * s)
            {
                corner = i;
                break;
            }
        var cornerX = Math.Clamp(corner * step, 1.6f * s, 3.4f * s);
        for (var pass = 0; pass < 3; pass++)
            foreach (var a in new[] { seam, upper, lower })
            {
                var copy = (float[])a.Clone();
                for (var i = 1; i < cols - 1; i++) a[i] = 0.25f * copy[i - 1] + 0.5f * copy[i] + 0.25f * copy[i + 1];
            }
        for (var i = 0; i < cols; i++)
        {
            // Lips taper to nothing at the corners, and never grow past a believable height.
            var x = i * step;
            var taper = MathF.Sqrt(MathF.Max(0f, 1f - MathF.Pow(x / cornerX, 4f)));
            upper[i] = seam[i] + taper * Math.Clamp(upper[i] - seam[i], 0.35f * s, (c.Female ? 0.85f : 0.65f) * s);
            lower[i] = seam[i] - taper * Math.Clamp(seam[i] - lower[i], 0.45f * s, (c.Female ? 1.1f : 0.9f) * s);
        }
        return new LipProfile(step, seam, upper, lower, cornerX, mouth.Y + 0.9f * s);
    }

    /// <summary>
    /// Joint bends (degrees) for each hand pose: per finger (index, middle, ring, little) the knuckle,
    /// middle and end joints; for the thumb its base, middle and end joints and how far its base
    /// swings in across the palm.
    /// </summary>
    private static (float[][] Fingers, float[] Thumb, float ThumbAcross) PoseAngles(HandPose pose) => pose switch
    {
        HandPose.Open => ([[0, 0, 0], [0, 0, 0], [0, 0, 0], [0, 0, 0]], [0, 0, 0], -6f),
        HandPose.Closed => ([[42, 58, 34], [48, 64, 38], [54, 68, 40], [60, 70, 42]], [12, 22, 24], 14f),
        HandPose.Fist => ([[86, 102, 62], [90, 104, 64], [92, 104, 64], [94, 104, 62]], [24, 46, 42], 26f),
        _ => ([[10, 20, 12], [14, 24, 14], [18, 28, 16], [22, 32, 18]], [4, 10, 12], 4f),   // relaxed
    };

    /// <summary>
    /// Bends the fingers of the dense body into <see cref="CharacterSpec.HandPose"/> (each finger bone
    /// turns about the axis across the back of the finger, carrying the bones beyond it, and the
    /// vertices follow by their finger weights), then puts the skeleton's finger bones on the bent
    /// joints. The bones keep the mannequin's orientations, turned by the same bends, so the bind
    /// pose holds the hand pose and the mannequin's finger animations still drive the fingers.
    /// </summary>
    private static void PoseHands(BuildContext c, HumanBody.Fitted human)
    {
        var skeleton = c.Skeleton;
        if (!skeleton.HasFingers) return;
        var source = HumanBody.Default;
        var bones = source.Bones;
        var boneIndex = bones.Select((b, i) => (b.Name, i)).ToDictionary(x => x.Name, x => x.i);
        var (fingerAngles, thumbAngles, thumbAcross) = PoseAngles(c.Spec.HandPose);
        var moves = new Dictionary<int, Matrix4x4>();     // source bone -> its move
        var turns = new Dictionary<string, Quaternion>(); // mannequin bone -> its turn
        var original = (Vector3[])human.Positions.Clone();
        string[] names = ["thumb", "index", "middle", "ring", "pinky"];

        foreach (var (s, side) in new[] { ("l", "_l"), ("r", "_r") })
        {
            var sign = side == "_l" ? 1f : -1f;
            var hand = human.Joint($"joint-{s}-hand");
            for (var f = 1; f <= 5; f++)
            {
                var thumb = f == 1;
                var joints = Enumerable.Range(1, 4).Select(k => human.Joint($"joint-{s}-finger-{f}-{k}")).ToArray();
                var angles = thumb ? thumbAngles : fingerAngles[f - 2];
                // The back of the finger faces up (palm down in the T-pose); the thumb's nail faces out to the front.
                var up = thumb ? Vector3.Normalize(new Vector3(0, -0.75f, 0.66f)) : Vector3.UnitZ;
                var move = Matrix4x4.Identity;
                var turn = Quaternion.Identity;
                if (thumb && thumbAcross != 0f)
                {
                    // Swing the thumb in across the palm about the hand's up axis.
                    var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, sign * thumbAcross * MathF.PI / 180f);
                    move = Matrix4x4.CreateTranslation(-joints[0]) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(joints[0]);
                    turn = q;
                }
                var posed = new Vector3[4];
                for (var k = 0; k < 3; k++)
                {
                    posed[k] = Vector3.Transform(joints[k], move);
                    var next = Vector3.Transform(joints[k + 1], move);
                    var d = Vector3.Normalize(next - posed[k]);
                    var u = Vector3.Transform(up, turn);
                    var axis = Vector3.Normalize(Vector3.Cross(u, d));
                    var q = Quaternion.CreateFromAxisAngle(axis, angles[k] * MathF.PI / 180f);
                    move = move * Matrix4x4.CreateTranslation(-posed[k]) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(posed[k]);
                    turn = q * turn;
                    var name = $"{names[f - 1]}_0{k + 1}{side}";
                    moves[boneIndex[name]] = move;
                    turns[name] = turn;
                    // The bone sits on its (bent) joint and is turned with it.
                    var bone = skeleton[name];
                    bone.Position = posed[k];
                    bone.Rotation = Quaternion.Normalize(turn * bone.Rotation);
                }
                if (!thumb)
                {
                    // The metacarpal sits in the palm on the way from the wrist to the knuckle.
                    skeleton[$"{names[f - 1]}_metacarpal{side}"].Position = Vector3.Lerp(hand, joints[0], 0.35f);
                }
            }
        }

        // Skin the dense body's fingers to the bent bones.
        for (var v = 0; v < original.Length; v++)
        {
            var list = human.SourceWeights[v];
            if (!list.Any(x => moves.ContainsKey(x.Bone))) continue;
            var total = list.Sum(x => x.Weight);
            var p = Vector3.Zero;
            foreach (var (b, w) in list)
                p += (moves.TryGetValue(b, out var m) ? Vector3.Transform(original[v], m) : original[v]) * (w / total);
            human.Positions[v] = p;
        }
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
    /// Hair as a shell over the dense head: everything above <paramref name="edge"/> pushed out by
    /// <paramref name="volume"/>, thinning to almost nothing at the hairline (so it grows out of the
    /// painted hair instead of sitting on the head like a helmet) and fuller at the crown. Hair that
    /// reaches below the widest part of the skull (<paramref name="hangs"/>) falls straight down from
    /// it instead of following the head in under the jaw and round the neck.
    /// </summary>
    internal static void BuildHair(BuildContext c, ScalpEdge edge, float volume, bool hangs)
    {
        var human = c.Human!;
        var pos = human.Positions;
        var bones = HumanBody.Default.Bones;
        var centre = c.HeadCenter;
        float Height(Vector3 p) => (p.Z - c.ChinZ) / c.HeadHeight;
        float Frontness(Vector3 p)
        {
            var r = new Vector2(p.X, p.Y - centre.Y);
            return r.LengthSquared() > 1e-6f ? r.Y / r.Length() : -1f;
        }
        // Above the hairline, by how much (in head heights).
        float Above(Vector3 p) => Height(p) - edge.At(Frontness(p));
        bool IsEar(Vector3 p) => MathF.Abs(p.X) > c.HeadHalfWidth * 0.9f && MathF.Abs(p.Y - centre.Y) < 4f * c.HeadScale
            && Height(p) > 0.3f && Height(p) < 0.68f;
        bool Region(int v)
        {
            var bone = bones[human.DominantSourceBone[v]].Name;
            if (bone != "head" && bone != "neck_01") return false;
            var p = pos[v];
            // Short hair stops round the ears; hanging hair covers them.
            if (!hangs && IsEar(p)) return false;
            // Starts just inside the painted hairline, which hides the shell's stepped edge.
            return Above(p) > 0.01f;
        }
        float Offset(Vector3 p)
        {
            var grow = Geo.SmoothStep(Above(p) / 0.13f);
            var crown = Geo.SmoothStep((Height(p) - 0.72f) / 0.25f);
            return volume * (0.18f + 0.82f * grow) * (1f + 0.35f * crown);
        }

        var tris = KeepLargePieces(Triangulate(HumanBody.Default.BodyFaces).Where(f => Region(f[0]) && Region(f[1]) && Region(f[2])).ToArray(), 40);
        if (tris.Length == 0) return;
        var used = tris.SelectMany(f => f).Distinct().ToArray();
        // The edge of the hair stays thin: smoothing would otherwise lift it off the skin in a ledge.
        var edgeCount = new Dictionary<(int, int), int>();
        foreach (var f in tris)
            for (var k = 0; k < 3; k++)
            {
                var (a, b) = (f[k], f[(k + 1) % 3]);
                var key = a < b ? (a, b) : (b, a);
                edgeCount[key] = edgeCount.GetValueOrDefault(key) + 1;
            }
        var rim = edgeCount.Where(e => e.Value == 1).SelectMany(e => new[] { e.Key.Item1, e.Key.Item2 }).ToHashSet();
        var neighbours = new Dictionary<int, HashSet<int>>();
        foreach (var f in tris)
            for (var k = 0; k < 3; k++)
            {
                if (!neighbours.TryGetValue(f[k], out var set)) neighbours[f[k]] = set = [];
                set.Add(f[(k + 1) % 3]);
                set.Add(f[(k + 2) % 3]);
            }
        // The skin the hair lies on. Hair over the ears does not follow their folds: they are
        // replaced by a smooth membrane across them, and the hair is made to clear them later.
        var skin = used.ToDictionary(v => v, v => pos[v]);
        var ears = used.Where(v => IsEar(pos[v])).ToArray();
        for (var pass = 0; pass < 40 && ears.Length > 0; pass++)
            foreach (var v in ears)
            {
                var avg = Vector3.Zero;
                foreach (var w in neighbours[v]) avg += skin[w];
                skin[v] = avg / neighbours[v].Count;
            }
        var normals = new Dictionary<int, Vector3>();
        foreach (var f in tris)
        {
            var n = Vector3.Cross(skin[f[1]] - skin[f[0]], skin[f[2]] - skin[f[0]]);
            foreach (var v in f) normals[v] = normals.GetValueOrDefault(v) + n;
        }
        var moved = used.ToDictionary(v => v, v => skin[v] + Vector3.Normalize(normals[v]) * Offset(pos[v]));

        if (hangs)
        {
            // Below the widest part of the skull the hair falls straight: no closer to the middle than
            // the shell is there in the same direction, drawing in a little towards the ends.
            var widestZ = c.HeadZ(0.55f);
            const int bins = 48;
            var reach = new float[bins];
            int Bin(Vector3 p) => (int)((MathF.Atan2(p.X, p.Y - centre.Y) / MathF.Tau + 0.5f) * bins) % bins;
            foreach (var (_, q) in moved)
                if (MathF.Abs(q.Z - widestZ) < 1.5f * c.HeadScale)
                {
                    var b = Bin(q);
                    reach[b] = MathF.Max(reach[b], new Vector2(q.X, q.Y - centre.Y).Length());
                }
            for (var pass = 0; pass < 2; pass++)
            {
                var copy = (float[])reach.Clone();
                for (var b = 0; b < bins; b++)
                    reach[b] = MathF.Max(copy[b], 0.5f * (copy[(b + bins - 1) % bins] + copy[(b + 1) % bins]));
            }
            // How far out the ears reach, so the hair falls clear of them.
            var earReach = new float[bins];
            float earTop = float.MinValue, earBottom = float.MaxValue;
            foreach (var v in ears)
            {
                var p = pos[v];
                var b = Bin(p);
                earReach[b] = MathF.Max(earReach[b], new Vector2(p.X, p.Y - centre.Y).Length() + 0.5f * c.HeadScale);
                earTop = MathF.Max(earTop, p.Z);
                earBottom = MathF.Min(earBottom, p.Z);
            }
            for (var pass = 0; pass < 3; pass++)
            {
                var copy = (float[])earReach.Clone();
                for (var b = 0; b < bins; b++)
                    earReach[b] = MathF.Max(copy[b], 0.9f * MathF.Max(copy[(b + bins - 1) % bins], copy[(b + 1) % bins]));
            }
            var lowest = moved.Values.Min(q => q.Z);
            foreach (var v in used)
            {
                var q = moved[v];
                var overEar = ears.Length > 0
                    ? Geo.SmoothStep((q.Z - (earBottom - 2f)) / 2f) * (1f - Geo.SmoothStep((q.Z - earTop) / 2.5f))
                    : 0f;
                if (q.Z >= widestZ && overEar <= 0f) continue;
                var r = new Vector2(q.X, q.Y - centre.Y);
                var length = r.Length();
                var drop = Geo.SmoothStep((widestZ - q.Z) / MathF.Max(1f, widestZ - lowest));
                var want = q.Z < widestZ ? reach[Bin(q)] * (1f - 0.07f * drop) : 0f;
                want = MathF.Max(want, earReach[Bin(q)] * overEar);
                // Only where the hair is: the face stays open at the front.
                var front = Frontness(q);
                want *= Geo.SmoothStep((front + 0.85f) / 0.3f);
                if (length < want && length > 1e-4f)
                {
                    var blend = MathF.Max(Geo.SmoothStep((widestZ - q.Z) / (2f * c.HeadScale)), overEar);
                    var target = r * (want / length);
                    moved[v] = new Vector3(Geo.Lerp(q.X, target.X, blend), centre.Y + Geo.Lerp(r.Y, target.Y, blend), q.Z);
                }
            }
        }

        // Smooth it into one soft mass, never sinking closer to the skin than the offset asks.
        var hung = moved.ToDictionary(x => x.Key, x => x.Value);
        for (var pass = 0; pass < 6; pass++)
        {
            var next = new Dictionary<int, Vector3>(moved.Count);
            foreach (var (v, q) in moved)
            {
                var avg = Vector3.Zero;
                foreach (var w in neighbours[v]) avg += moved[w];
                avg /= neighbours[v].Count;
                var r = Vector3.Lerp(q, avg, 0.5f);
                var n = Vector3.Normalize(normals[v]);
                var depth = Vector3.Dot(r - skin[v], n);
                var want = Offset(pos[v]);
                if (depth < want) r += n * (want - depth);
                else if (!hangs || pos[v].Z > c.HeadZ(0.55f))
                {
                    // Not much fuller than asked for, and the rim right down on the skin.
                    var most = rim.Contains(v) ? want : want + 0.35f * volume;
                    if (depth > most) r -= n * (depth - most);
                }
                // Hanging hair stays out where it fell.
                if (hangs)
                {
                    var h = hung[v];
                    var outward = new Vector2(h.X, h.Y - centre.Y);
                    var length = outward.Length();
                    var now = new Vector2(r.X, r.Y - centre.Y);
                    if (length > 1e-4f && now.Length() < length - 0.3f * c.HeadScale)
                    {
                        var fixedR = now * ((length - 0.3f * c.HeadScale) / MathF.Max(now.Length(), 1e-4f));
                        r = new Vector3(fixedR.X, centre.Y + fixedR.Y, r.Z);
                    }
                }
                next[v] = r;
            }
            moved = next;
        }

        var local = used.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
        var points = used.Select(v => moved[v]).ToArray();
        var localTris = tris.Select(f => new[] { local[f[0]], local[f[1]], local[f[2]] }).ToArray();
        var piece = Decimator.Simplify(points, localTris, new Decimator.Options { TargetTriangles = hangs ? 700 : 420 });
        var shellNormals = VertexNormals(points, localTris, smoothing: 2);
        var pieceNormals = Array.ConvertAll(piece.Source, i => shellNormals[i]);
        Emit(c, c.Mesh, piece with { Source = piece.Source.Select(i => used[i]).ToArray() }, _ => Slot.Hair, pieceNormals);
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
        var shellNormals = VertexNormals(points, tris);
        var pieceNormals = Array.ConvertAll(piece.Source, i => shellNormals[i]);
        // Back to the body's vertex numbering, for the weights.
        var result = piece with { Source = piece.Source.Select(i => source[i]).ToArray() };
        Emit(c, c.Mesh, result, v => armSlot is { } a && isArm!(v) ? a : slot, pieceNormals);
    }

    /// <summary>
    /// Reduces a left-right symmetric mesh symmetrically: the left half is reduced and mirrored onto
    /// the right, so the two sides of the face (and body) match exactly instead of each side
    /// collapsing its own way. Vertices on the middle stay on it. Falls back to reducing the whole
    /// mesh if it is not symmetric.
    /// </summary>
    private static Decimator.Result SymmetricSimplify(Vector3[] positions, int[][] tris, int target, Func<int, float>? importance)
    {
        // The fitted middle line wanders by a fraction of a millimetre.
        const float middle = 0.05f;
        var options = new Decimator.Options { TargetTriangles = target, Importance = importance };
        // Each vertex's mirror image.
        // The fitted halves match to within a few millimetres (the skeleton's sides differ slightly).
        const float cell = 0.3f;
        static (long, long, long) Key(Vector3 p) => ((long)MathF.Floor(p.X / cell), (long)MathF.Floor(p.Y / cell), (long)MathF.Floor(p.Z / cell));
        var used = tris.SelectMany(t => t).Distinct().ToArray();
        var grid = new Dictionary<(long, long, long), List<int>>();
        foreach (var v in used)
        {
            var k = Key(positions[v]);
            if (!grid.TryGetValue(k, out var list)) grid[k] = list = [];
            list.Add(v);
        }
        var mirror = new Dictionary<int, int>();
        var misses = 0;
        foreach (var v in used)
        {
            var p = positions[v] with { X = -positions[v].X };
            var (kx, ky, kz) = Key(p);
            var best = -1;
            var bestD = cell * cell;
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    for (var dz = -1; dz <= 1; dz++)
                        if (grid.TryGetValue((kx + dx, ky + dy, kz + dz), out var list))
                            foreach (var w in list)
                            {
                                var d = Vector3.DistanceSquared(positions[w], p);
                                if (d < bestD) { bestD = d; best = w; }
                            }
            if (best < 0) misses++;
            mirror[v] = best < 0 ? v : best;
        }
        if (misses > used.Length / 50) return Decimator.Simplify(positions, tris, options);

        var left = tris.Where(t => t.All(v => positions[v].X >= -middle)).ToArray();
        if (left.Length * 2 < tris.Length * 0.95f) return Decimator.Simplify(positions, tris, options);
        var half = Decimator.Simplify(positions, left, new Decimator.Options
        {
            TargetTriangles = target / 2, Importance = importance, OnMiddle = v => MathF.Abs(positions[v].X) <= middle,
        });

        var points = new List<Vector3>(half.Positions.Length * 2);
        var source = new List<int>(half.Positions.Length * 2);
        var onMiddle = new bool[half.Positions.Length];
        for (var i = 0; i < half.Positions.Length; i++)
        {
            var p = half.Positions[i];
            // The middle line is the open edge of the half: keep it exactly on the middle.
            onMiddle[i] = MathF.Abs(positions[half.Source[i]].X) <= middle;
            if (onMiddle[i]) p.X = 0f;
            points.Add(p);
            source.Add(half.Source[i]);
        }
        var mirrored = new int[half.Positions.Length];
        for (var i = 0; i < half.Positions.Length; i++)
        {
            if (onMiddle[i])
            {
                mirrored[i] = i;
                continue;
            }
            mirrored[i] = points.Count;
            var p = points[i];
            points.Add(p with { X = -p.X });
            source.Add(mirror[half.Source[i]]);
        }
        var triangles = new List<int[]>(half.Triangles.Length * 2);
        foreach (var t in half.Triangles)
        {
            triangles.Add(t);
            triangles.Add([mirrored[t[0]], mirrored[t[2]], mirrored[t[1]]]);
        }
        return new Decimator.Result(points.ToArray(), triangles.ToArray(), source.ToArray());
    }

    /// <summary>The triangles of the connected pieces with at least <paramref name="min"/> triangles (drops stray scraps).</summary>
    private static int[][] KeepLargePieces(int[][] tris, int min)
    {
        var byVertex = new Dictionary<int, List<int>>();
        for (var f = 0; f < tris.Length; f++)
            foreach (var v in tris[f])
            {
                if (!byVertex.TryGetValue(v, out var list)) byVertex[v] = list = [];
                list.Add(f);
            }
        var piece = new int[tris.Length];
        Array.Fill(piece, -1);
        var sizes = new List<int>();
        for (var f0 = 0; f0 < tris.Length; f0++)
        {
            if (piece[f0] >= 0) continue;
            var id = sizes.Count;
            var count = 0;
            var stack = new Stack<int>([f0]);
            piece[f0] = id;
            while (stack.Count > 0)
            {
                var f = stack.Pop();
                count++;
                foreach (var v in tris[f])
                    foreach (var g in byVertex[v])
                        if (piece[g] < 0)
                        {
                            piece[g] = id;
                            stack.Push(g);
                        }
            }
            sizes.Add(count);
        }
        return tris.Where((_, f) => sizes[piece[f]] >= min).ToArray();
    }

    /// <summary>
    /// Area-weighted smooth vertex normals of a triangle mesh, then averaged with their neighbours'
    /// <paramref name="smoothing"/> times: the reduced mesh samples them at a few of the vertices, so
    /// detail finer than its triangles (creases, wrinkles) is filtered out first rather than smeared
    /// across a triangle as a stray shadow.
    /// </summary>
    private static Vector3[] VertexNormals(IReadOnlyList<Vector3> positions, int[][] tris, int smoothing = 0)
    {
        var normals = new Vector3[positions.Count];
        foreach (var t in tris)
        {
            var n = Vector3.Cross(positions[t[1]] - positions[t[0]], positions[t[2]] - positions[t[0]]);
            normals[t[0]] += n;
            normals[t[1]] += n;
            normals[t[2]] += n;
        }
        for (var i = 0; i < normals.Length; i++)
            normals[i] = normals[i].LengthSquared() > 1e-12f ? Vector3.Normalize(normals[i]) : Vector3.UnitZ;
        if (smoothing > 0)
        {
            var neighbours = new HashSet<int>?[positions.Count];
            foreach (var t in tris)
                for (var k = 0; k < 3; k++)
                {
                    var set = neighbours[t[k]] ??= [];
                    set.Add(t[(k + 1) % 3]);
                    set.Add(t[(k + 2) % 3]);
                }
            for (var pass = 0; pass < smoothing; pass++)
            {
                var next = new Vector3[normals.Length];
                for (var i = 0; i < normals.Length; i++)
                {
                    var sum = normals[i];
                    if (neighbours[i] is { } set)
                        foreach (var j in set) sum += normals[j];
                    next[i] = sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : normals[i];
                }
                normals = next;
            }
        }
        return normals;
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
    private static void Emit(BuildContext c, MeshBuilder m, Decimator.Result piece, Func<int, Slot> slotOf, Vector3[]? normals = null)
    {
        var human = c.Human!;
        var ids = new int[piece.Positions.Length];
        m.VertexNormals.Clear();
        for (var i = 0; i < ids.Length; i++)
        {
            var w = human.Weights[piece.Source[i]];
            // Decimation may settle a sole vertex a hair below the ground: keep it on it.
            var at = piece.Positions[i];
            if (at.Z < 0f) at.Z = 0f;
            ids[i] = m.AddVertex(at, _ => w);
            if (normals is not null) m.VertexNormals[ids[i]] = normals[i];
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
            // The face and eyes get more of the texture, so eyes, brows and lips stay sharp.
            m.Density = slot[f0] is Slot.Head or Slot.Eye ? 3.2f : 1f;
            m.AddPatch(slot[f0], axes[axis[f0]], patch);
            m.Density = 1f;
        }
        m.VertexNormals.Clear();
    }
}

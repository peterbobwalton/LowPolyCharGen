using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Text;

namespace LowPolyCharGen.Parts;

/// <summary>
/// The realistic body: the MakeHuman base mesh (CC0, see tools/import_makehuman.py) with its shape
/// targets for gender, muscle, weight, ancestry and bust, fitted onto the mannequin skeleton in the
/// T-pose. The dense result (about 27,000 triangles) is what the generator decimates to its budget.
/// </summary>
public sealed class HumanBody
{
    public sealed record Target(string Name, int[] Vertices, Vector3[] Deltas);

    /// <summary>A bone of the source rig (MakeHuman's game_engine rig, Unreal-style names).</summary>
    public sealed record SourceBone(string Name, string Parent, string HeadJoint, string TailJoint, (int Vertex, float Weight)[] Weights);

    public Vector3[] BasePositions { get; }
    public int[][] BodyFaces { get; }
    public int[][] EyeFaces { get; }
    public IReadOnlyDictionary<string, int[]> Joints { get; }
    public IReadOnlyList<SourceBone> Bones { get; }
    public IReadOnlyDictionary<string, Target> Targets { get; }

    private HumanBody(Vector3[] positions, int[][] body, int[][] eyes, Dictionary<string, int[]> joints, List<SourceBone> bones, Dictionary<string, Target> targets)
    {
        BasePositions = positions;
        BodyFaces = body;
        EyeFaces = eyes;
        Joints = joints;
        Bones = bones;
        Targets = targets;
    }

    private static readonly Lazy<HumanBody> Instance = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LowPolyCharGen.Assets.human_body.bin")
            ?? throw new InvalidOperationException("The human body resource is missing.");
        return Read(stream);
    });

    public static HumanBody Default => Instance.Value;

    public static HumanBody Read(Stream stream)
    {
        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        using var r = new BinaryReader(gz);
        if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "LPHB") throw new InvalidDataException("Not a human body file.");
        var version = r.ReadInt32();
        if (version != 1) throw new InvalidDataException($"Unsupported human body version {version}.");
        string Str() => Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
        int[][] Faces()
        {
            var faces = new int[r.ReadInt32()][];
            for (var f = 0; f < faces.Length; f++)
            {
                var n = r.ReadByte();
                faces[f] = new int[n];
                for (var k = 0; k < n; k++) faces[f][k] = r.ReadInt32();
            }
            return faces;
        }
        var positions = new Vector3[r.ReadInt32()];
        for (var i = 0; i < positions.Length; i++) positions[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        var body = Faces();
        var eyes = Faces();
        var joints = new Dictionary<string, int[]>();
        for (int i = 0, n = r.ReadInt32(); i < n; i++)
        {
            var name = Str();
            var members = new int[r.ReadInt32()];
            for (var k = 0; k < members.Length; k++) members[k] = r.ReadInt32();
            joints[name] = members;
        }
        var bones = new List<SourceBone>();
        for (int i = 0, n = r.ReadInt32(); i < n; i++)
        {
            var (name, parent, head, tail) = (Str(), Str(), Str(), Str());
            var weights = new (int, float)[r.ReadInt32()];
            for (var k = 0; k < weights.Length; k++) weights[k] = (r.ReadInt32(), r.ReadSingle());
            bones.Add(new SourceBone(name, parent, head, tail, weights));
        }
        var targets = new Dictionary<string, Target>();
        for (int i = 0, n = r.ReadInt32(); i < n; i++)
        {
            var name = Str();
            var count = r.ReadInt32();
            var idx = new int[count];
            for (var k = 0; k < count; k++) idx[k] = r.ReadInt32();
            var deltas = new Vector3[count];
            for (var k = 0; k < count; k++) deltas[k] = new Vector3((float)r.ReadHalf(), (float)r.ReadHalf(), (float)r.ReadHalf());
            targets[name] = new Target(name, idx, deltas);
        }
        return new HumanBody(positions, body, eyes, joints, bones, targets);
    }

    // ---- shape -----------------------------------------------------------------------------

    /// <summary>
    /// The body shaped by MakeHuman's macro rules for a young adult: <paramref name="gender"/> 0 =
    /// female .. 1 = male; muscle and weight 0 .. 1 with 0.5 average; bust 0 .. 1 (women);
    /// ancestry weights (African, Asian, European) summing to 1.
    /// </summary>
    public Vector3[] Shape(float gender, float muscle, float weight, float bust, Vector3 ancestry)
    {
        var p = (Vector3[])BasePositions.Clone();
        void Add(string name, float w)
        {
            if (w <= 1e-5f || !Targets.TryGetValue(name, out var t)) return;
            for (var k = 0; k < t.Vertices.Length; k++) p[t.Vertices[k]] += t.Deltas[k] * w;
        }
        static (string, float)[] Three(float v, string lo, string mid, string hi) => v < 0.5f
            ? [(lo, 1f - 2f * v), (mid, 2f * v)]
            : [(mid, 2f - 2f * v), (hi, 2f * v - 1f)];
        var genders = new[] { ("female", 1f - gender), ("male", gender) };
        var races = new[] { ("african", ancestry.X), ("asian", ancestry.Y), ("caucasian", ancestry.Z) };
        foreach (var (g, gw) in genders)
        {
            foreach (var (race, rw) in races) Add($"{race}-{g}-young", gw * rw);
            foreach (var (m, mw) in Three(muscle, "minmuscle", "averagemuscle", "maxmuscle"))
                foreach (var (w, ww) in Three(weight, "minweight", "averageweight", "maxweight"))
                    Add($"universal-{g}-young-{m}-{w}", gw * mw * ww);
        }
        foreach (var (c, cw) in Three(bust, "mincup", "averagecup", "maxcup"))
            Add($"female-young-averagemuscle-averageweight-{c}-averagefirmness", (1f - gender) * cw);
        return p;
    }

    public Vector3 Joint(Vector3[] positions, string name)
    {
        var members = Joints[name];
        var sum = Vector3.Zero;
        foreach (var v in members) sum += positions[v];
        return sum / members.Length;
    }

    // ---- fit to the mannequin -----------------------------------------------------------------

    /// <summary>The shaped body on the mannequin skeleton: positions in model space and weights on its bones.</summary>
    public sealed record Fitted(Vector3[] Positions, Influence[][] Weights, int[] DominantSourceBone, Func<string, Vector3> Joint, float Scale,
        (int Bone, float Weight)[][] SourceWeights);

    /// <summary>
    /// Moves every source bone onto the matching mannequin bone (turned onto its direction, stretched
    /// to its length, thickened by <paramref name="girth"/>) and every vertex by the blend of its
    /// bones' moves, the way skinning does; then hands the weights over to the mannequin's bones.
    /// The trunk weights are spread over the mannequin's five spine and two neck bones by
    /// <paramref name="trunkChain"/>.
    /// </summary>
    public Fitted Fit(Vector3[] shaped, Skeleton skeleton, float girth, WeightFn trunkChain)
    {
        Vector3 J(string name) => Joint(shaped, name);
        Vector3 U(string bone) => skeleton[bone].Position;
        var maps = new Dictionary<string, Matrix4x4>();

        Matrix4x4 Map(Vector3 fromHead, Vector3 fromTail, Vector3 toHead, Vector3 toTail)
        {
            var dirFrom = fromTail - fromHead;
            var dirTo = toTail - toHead;
            var axis = Vector3.Normalize(dirFrom);
            var along = dirTo.Length() / MathF.Max(dirFrom.Length(), 1e-4f);
            var scale = Matrix4x4.CreateScale(girth) + OuterProduct(axis, along - girth);
            scale.M44 = 1f;
            return Matrix4x4.CreateTranslation(-fromHead) * scale * Turn(dirFrom, dirTo) * Matrix4x4.CreateTranslation(toHead);
        }
        Matrix4x4 Rigid(Vector3 fromHead, Matrix4x4 turn, Vector3 toHead, float s) =>
            Matrix4x4.CreateTranslation(-fromHead) * Matrix4x4.CreateScale(s) * turn * Matrix4x4.CreateTranslation(toHead);

        // Trunk, neck and head stand upright on both skeletons: they are moved and stretched up and
        // down, never turned (a few degrees of difference in the spine would shear the whole torso).
        Matrix4x4 Upright(Vector3 fromHead, float fromTopZ, Vector3 toHead, float toTopZ)
        {
            var up = (toTopZ - toHead.Z) / MathF.Max(fromTopZ - fromHead.Z, 1e-3f);
            return Matrix4x4.CreateTranslation(-fromHead) * Matrix4x4.CreateScale(girth, girth, up) * Matrix4x4.CreateTranslation(toHead);
        }
        var trunk = Upright(J("joint-pelvis"), J("joint-neck").Z, U("pelvis"), U("neck_01").Z);
        foreach (var bone in new[] { "pelvis", "spine_01", "spine_02", "spine_03" }) maps[bone] = trunk;
        maps["neck_01"] = Upright(J("joint-neck"), J("joint-head").Z, U("neck_01"), U("head").Z);
        maps["head"] = Rigid(J("joint-head"), Matrix4x4.Identity, U("head"), girth);
        foreach (var (s, side) in new[] { ("l", "_l"), ("r", "_r") })
        {
            // The collarbone only stretches out sideways to the shoulder.
            var clavicleFrom = J($"joint-{s}-clavicle");
            var outward = (U("upperarm" + side).X - U("clavicle" + side).X) / (J($"joint-{s}-shoulder").X - clavicleFrom.X);
            maps["clavicle" + side] = Matrix4x4.CreateTranslation(-clavicleFrom) * Matrix4x4.CreateScale(outward, girth, girth)
                * Matrix4x4.CreateTranslation(Vector3.Transform(clavicleFrom, trunk));
            maps["upperarm" + side] = Map(J($"joint-{s}-shoulder"), J($"joint-{s}-elbow"), U("upperarm" + side), U("lowerarm" + side));
            maps["lowerarm" + side] = Map(J($"joint-{s}-elbow"), J($"joint-{s}-hand"), U("lowerarm" + side), U("hand" + side));
            // The hand keeps its shape and its pose relative to the forearm.
            var foreTurn = Turn(J($"joint-{s}-hand") - J($"joint-{s}-elbow"), U("hand" + side) - U("lowerarm" + side));
            maps["hand" + side] = Rigid(J($"joint-{s}-hand"), foreTurn, U("hand" + side), girth);
            maps["thigh" + side] = Map(J($"joint-{s}-upper-leg"), J($"joint-{s}-knee"), U("thigh" + side), U("calf" + side));
            maps["calf" + side] = Map(J($"joint-{s}-knee"), J($"joint-{s}-ankle"), U("calf" + side), U("foot" + side));
            maps["foot" + side] = Map(J($"joint-{s}-ankle"), J($"joint-{s}-foot-1"), U("foot" + side), U("ball" + side));
            var footTurn = Turn(J($"joint-{s}-foot-1") - J($"joint-{s}-ankle"), U("ball" + side) - U("foot" + side));
            maps["ball" + side] = Rigid(J($"joint-{s}-foot-1"), footTurn, U("ball" + side), girth);
        }
        // Fingers ride on the hand.
        string MapName(string bone)
        {
            if (maps.ContainsKey(bone)) return bone;
            var side = bone.EndsWith("_l", StringComparison.Ordinal) ? "_l" : "_r";
            return "hand" + side;
        }

        // Per-vertex source weights.
        var n = shaped.Length;
        var sourceWeights = new List<(int Bone, float Weight)>[n];
        for (var v = 0; v < n; v++) sourceWeights[v] = [];
        for (var b = 0; b < Bones.Count; b++)
            foreach (var (v, w) in Bones[b].Weights)
                if (w > 1e-4f) sourceWeights[v].Add((b, w));
        var headIndex = Bones.Select((x, i) => (x, i)).First(t => t.x.Name == "head").i;

        var positions = new Vector3[n];
        var dominant = new int[n];
        var boneMaps = Bones.Select(b => maps[MapName(b.Name)]).ToArray();
        for (var v = 0; v < n; v++)
        {
            var list = sourceWeights[v];
            if (list.Count == 0) list.Add((headIndex, 1f));   // eyeballs and loose helpers ride on the head
            var total = list.Sum(x => x.Weight);
            var p = Vector3.Zero;
            var best = list[0];
            foreach (var (b, w) in list)
            {
                p += Vector3.Transform(shaped[v], boneMaps[b]) * (w / total);
                if (w > best.Weight) best = (b, w);
            }
            positions[v] = p;
            dominant[v] = best.Bone;
        }

        // The soles stand on the ground: anything that ended up below it is squeezed up into the last centimetre.
        var minZ = positions.Min(q => q.Z);
        if (minZ < 0f)
            for (var v = 0; v < n; v++)
                if (positions[v].Z < 1f)
                    positions[v].Z = MathF.Max(0f, positions[v].Z - minZ * (1f - positions[v].Z) / (1f - minZ));

        // Weights onto the mannequin's bones.
        var trunkBones = new HashSet<string> { "pelvis", "spine_01", "spine_02", "spine_03", "neck_01" };
        var weights = new Influence[n][];
        for (var v = 0; v < n; v++)
        {
            var acc = new Dictionary<int, float>();
            void Put(int bone, float w) => acc[bone] = acc.GetValueOrDefault(bone) + w;
            var trunkWeight = 0f;
            foreach (var (b, w) in sourceWeights[v])
            {
                var name = Bones[b].Name;
                if (trunkBones.Contains(name)) trunkWeight += w;
                else Put(skeleton.Contains(name) ? skeleton.IndexOf(name) : skeleton.IndexOf(MapName(name)), w);
            }
            if (trunkWeight > 0f)
                foreach (var inf in trunkChain(positions[v])) Put(inf.Bone, inf.Weight * trunkWeight);
            weights[v] = Skinning.Normalize(acc.Select(kv => new Influence(kv.Key, kv.Value)).ToList());
        }

        // Joints in model space, for landmarks.
        Vector3 FittedJoint(string name)
        {
            var members = Joints[name];
            var sum = Vector3.Zero;
            foreach (var v in members) sum += positions[v];
            return sum / members.Length;
        }
        return new Fitted(positions, weights, dominant, FittedJoint, girth, sourceWeights.Select(l => l.ToArray()).ToArray());
    }

    private static Matrix4x4 OuterProduct(Vector3 v, float k) => new(
        k * v.X * v.X, k * v.X * v.Y, k * v.X * v.Z, 0,
        k * v.Y * v.X, k * v.Y * v.Y, k * v.Y * v.Z, 0,
        k * v.Z * v.X, k * v.Z * v.Y, k * v.Z * v.Z, 0,
        0, 0, 0, 0);

    /// <summary>The shortest rotation from direction <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static Matrix4x4 Turn(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        var axis = Vector3.Cross(from, to);
        var sin = axis.Length();
        if (sin < 1e-6f) return Matrix4x4.Identity;
        return Matrix4x4.CreateFromAxisAngle(axis / sin, MathF.Atan2(sin, Vector3.Dot(from, to)));
    }
}

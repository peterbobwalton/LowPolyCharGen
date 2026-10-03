using System.Numerics;

namespace LowPolyCharGen;

/// <summary>
/// Turns a character built on the mannequin T-pose into a Toon Soldiers character: the mesh is
/// reshaped onto the pack skeleton's joints with the pack's chunky proportions (wide trunk, thick
/// limbs, big head, block fists, big boots) and its skin weights are moved onto the pack's bones.
/// </summary>
/// <remarks>
/// Each mannequin bone gets an affine map: scale about its own joint in a frame aligned with the bone,
/// turn from the mannequin bone direction to the pack bone direction, and move the joint onto the
/// pack joint. A vertex moves by the weighted blend of its bones' maps (the same blend skinning uses),
/// so everything built on the body - clothes, gear, hair, hats - follows without knowing about it.
/// </remarks>
public static class ToonRig
{
    /// <summary>Mannequin bone to Toon Soldiers bone. Bones that are not listed carry no weights (IK bones).</summary>
    public static readonly IReadOnlyDictionary<string, string> BoneMap = BuildBoneMap();

    private static Dictionary<string, string> BuildBoneMap()
    {
        var map = new Dictionary<string, string>
        {
            ["root"] = "Bip001",
            ["pelvis"] = "Bip001-Pelvis",
            ["spine_01"] = "Bip001-Spine", ["spine_02"] = "Bip001-Spine", ["spine_03"] = "Bip001-Spine",
            ["spine_04"] = "Bip001-Spine", ["spine_05"] = "Bip001-Spine",
            ["neck_01"] = "Bip001-Neck", ["neck_02"] = "Bip001-Neck",
            ["head"] = "Bip001-Head",
        };
        foreach (var (side, s) in new[] { ("_l", "L"), ("_r", "R") })
        {
            map["clavicle" + side] = $"Bip001-{s}-Clavicle";
            map["upperarm" + side] = $"Bip001-{s}-UpperArm";
            map["lowerarm" + side] = $"Bip001-{s}-Forearm";
            map["hand" + side] = $"Bip001-{s}-Hand";
            map["thigh" + side] = $"Bip001-{s}-Thigh";
            map["calf" + side] = $"Bip001-{s}-Calf";
            map["foot" + side] = $"Bip001-{s}-Foot";
            map["ball" + side] = $"Bip001-{s}-Toe0";
        }
        return map;
    }

    // ---- proportions: scale factors relative to the mannequin build ----------------------------
    // (along the bone, across it sideways or up, across it front-to-back)

    private static readonly Vector3 Trunk = new(1f, 1.46f, 1.24f);    // along = from the joints; wide, deep chest
    private static readonly Vector3 Neck = new(1f, 1.35f, 1.30f);
    private const float HeadScale = 1.50f;
    private static readonly Vector3 Clavicle = new(1f, 1.50f, 1.45f);
    private static readonly Vector3 Arm = new(1f, 1.55f, 1.50f);
    private static readonly Vector3 Hand = new(1.05f, 1.45f, 1.40f);    // along, up, front (the fist itself is built by BodyBuilder)
    private static readonly Vector3 Thigh = new(1f, 1.40f, 1.32f);
    private static readonly Vector3 Calf = new(1f, 1.45f, 1.38f);
    private static readonly Vector3 Foot = new(1.50f, 1.38f, 1.0f);    // side, forward, up (up comes from the ankle height)

    /// <summary>Reshapes <paramref name="mesh"/> (built on <paramref name="design"/>) for the Toon Soldiers skeleton.</summary>
    public static (Skeleton Skeleton, MeshData Mesh) Convert(Skeleton design, MeshData mesh, float creaseDegrees)
    {
        var toon = Skeleton.CreateToonSoldier();
        var maps = BoneMaps(design, toon);

        var positions = new Vector3[mesh.Positions.Length];
        var weights = new Influence[mesh.Positions.Length][];
        for (var v = 0; v < positions.Length; v++)
        {
            var p = Vector3.Zero;
            var list = new List<Influence>(Skinning.MaxInfluences);
            foreach (var w in mesh.Weights[v])
            {
                p += Vector3.Transform(mesh.Positions[v], maps[w.Bone]) * w.Weight;
                var target = toon.IndexOf(BoneMap[design[w.Bone].Name]);
                var i = list.FindIndex(x => x.Bone == target);
                if (i >= 0) list[i] = new Influence(target, list[i].Weight + w.Weight);
                else list.Add(new Influence(target, w.Weight));
            }
            positions[v] = p;
            weights[v] = Skinning.Normalize(list);
        }

        // Nothing may end up below the ground (the boots grow downwards a little).
        var minZ = positions.Min(p => p.Z);
        if (minZ < 0)
            for (var v = 0; v < positions.Length; v++)
                if (positions[v].Z < 0.5f) positions[v].Z = MathF.Max(positions[v].Z - minZ * (0.5f - positions[v].Z) / (0.5f - minZ), 0f);

        return (toon, new MeshData
        {
            Positions = positions,
            Weights = weights,
            Faces = mesh.Faces,
            Charts = mesh.Charts,
            Normals = MeshData.ComputeNormals(positions, mesh.Faces, creaseDegrees),
            Uvs = mesh.Uvs,
        });
    }

    /// <summary>The reshaping map of every design bone (row-vector matrices, design space to toon space).</summary>
    private static Matrix4x4[] BoneMaps(Skeleton d, Skeleton t)
    {
        Vector3 D(string name) => d[name].Position;
        Vector3 T(string name) => t[name].Position;

        var maps = new Matrix4x4[d.Bones.Count];
        for (var i = 0; i < maps.Length; i++) maps[i] = Matrix4x4.Identity;
        void Set(string bone, Vector3 target, Matrix4x4 shape) =>
            maps[d.IndexOf(bone)] = Matrix4x4.CreateTranslation(-D(bone)) * shape * Matrix4x4.CreateTranslation(target);

        // Trunk: pelvis to neck becomes the pack's pelvis to neck; the spine joints are spread along it.
        var pelvisD = D("pelvis");
        var neckD = D("neck_01");
        var pelvisT = T("Bip001-Pelvis");
        var neckT = T("Bip001-Neck");
        var trunkAlong = (neckT.Z - pelvisT.Z) / (neckD.Z - pelvisD.Z);
        var trunk = Scale(Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, Trunk with { X = trunkAlong });
        Vector3 OnTrunk(Vector3 p) => Vector3.Lerp(pelvisT, neckT, (p.Z - pelvisD.Z) / (neckD.Z - pelvisD.Z)) with { X = 0 };
        maps[d.IndexOf("root")] = Matrix4x4.CreateTranslation(-pelvisD) * trunk * Matrix4x4.CreateTranslation(pelvisT);
        Set("pelvis", pelvisT, trunk);
        foreach (var spine in new[] { "spine_01", "spine_02", "spine_03", "spine_04", "spine_05" })
            Set(spine, OnTrunk(D(spine)), trunk);

        // Neck and head. The head grows about its joint, so it stays on the neck.
        var headT = T("Bip001-Head");
        var neckAlong = Vector3.Distance(neckT, headT) / Vector3.Distance(neckD, D("head"));
        var neck = Scale(Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, Neck with { X = neckAlong });
        Set("neck_01", neckT, neck);
        Set("neck_02", Vector3.Lerp(neckT, headT, (D("neck_02").Z - neckD.Z) / (D("head").Z - neckD.Z)), neck);
        Set("head", headT, Matrix4x4.CreateScale(HeadScale));

        foreach (var (side, s) in new[] { ("_l", "L"), ("_r", "R") })
        {
            // Arm chain: each bone is stretched to the pack bone's length, thickened and turned onto it.
            Matrix4x4 Limb(string bone, string child, string toonBone, string toonChild, Vector3 factors, Vector3 up)
            {
                var dirD = D(child + side) - D(bone + side);
                var dirT = T($"Bip001-{s}-{toonChild}") - T($"Bip001-{s}-{toonBone}");
                var axis = Vector3.Normalize(dirD);
                var u = Vector3.Normalize(up - Vector3.Dot(up, axis) * axis);
                var f = Vector3.Cross(axis, u);
                var shape = Scale(axis, u, f, factors with { X = factors.X * dirT.Length() / dirD.Length() }) * Turn(dirD, dirT);
                Set(bone + side, T($"Bip001-{s}-{toonBone}"), shape);
                return Turn(dirD, dirT);
            }

            Limb("clavicle", "upperarm", "Clavicle", "UpperArm", Clavicle, Vector3.UnitZ);
            Limb("upperarm", "lowerarm", "UpperArm", "Forearm", Arm, Vector3.UnitZ);
            var foreTurn = Limb("lowerarm", "hand", "Forearm", "Hand", Arm, Vector3.UnitZ);
            var handAxis = Vector3.Normalize(D("hand" + side) - D("lowerarm" + side));
            var handUp = Vector3.Normalize(Vector3.UnitZ - Vector3.Dot(Vector3.UnitZ, handAxis) * handAxis);
            Set("hand" + side, T($"Bip001-{s}-Hand"), Scale(handAxis, handUp, Vector3.Cross(handAxis, handUp), Hand) * foreTurn);

            Limb("thigh", "calf", "Thigh", "Calf", Thigh, Vector3.UnitX);
            Limb("calf", "foot", "Calf", "Foot", Calf, Vector3.UnitX);

            // Boots: the ankle sits higher on the pack skeleton, so the foot grows up to it from the sole.
            var ankleD = D("foot" + side);
            var ankleT = T($"Bip001-{s}-Foot");
            var foot = Scale(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Foot with { Z = ankleT.Z / ankleD.Z });
            Set("foot" + side, ankleT, foot);
            Set("ball" + side, ankleT + Vector3.Transform(D("ball" + side) - ankleD, foot), foot);
        }
        return maps;
    }

    /// <summary>Scales by factors.X along <paramref name="a"/>, .Y along <paramref name="b"/> and .Z along <paramref name="c"/> (an orthonormal frame).</summary>
    private static Matrix4x4 Scale(Vector3 a, Vector3 b, Vector3 c, Vector3 factors)
    {
        static Matrix4x4 Outer(Vector3 v, float k) => new(
            k * v.X * v.X, k * v.X * v.Y, k * v.X * v.Z, 0,
            k * v.Y * v.X, k * v.Y * v.Y, k * v.Y * v.Z, 0,
            k * v.Z * v.X, k * v.Z * v.Y, k * v.Z * v.Z, 0,
            0, 0, 0, 0);
        var m = Outer(a, factors.X) + Outer(b, factors.Y) + Outer(c, factors.Z);
        m.M44 = 1;
        return m;
    }

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

using System.Numerics;

namespace LowPolyCharGen;

public enum PreviewPose { TPose, APose, Relaxed, Walk, Action }

/// <summary>
/// Poses a character with linear blend skinning. Only used to preview how the rig deforms;
/// the exported FBX always contains the T-pose bind.
/// </summary>
public static class Posing
{
    /// <summary>
    /// Skin matrices (bind space to posed space) for a pose given as per-bone rotations about the
    /// bone's own joint, expressed in model-space axes.
    /// </summary>
    public static Matrix4x4[] SkinMatrices(Skeleton skeleton, IReadOnlyDictionary<string, Quaternion> rotations)
    {
        var bones = skeleton.Bones;
        var result = new Matrix4x4[bones.Count];
        for (var i = 0; i < bones.Count; i++)   // parents always precede their children
        {
            var bone = bones[i];
            var own = Matrix4x4.Identity;
            if (rotations.TryGetValue(bone.Name, out var q) || TryGetToon(rotations, bone.Name, out q))
                own = Matrix4x4.CreateTranslation(-bone.Position) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(bone.Position);
            result[i] = bone.Parent < 0 ? own : own * result[bone.Parent];
        }
        return result;
    }

    /// <summary>Poses are written with mannequin bone names; the Toon Soldiers rig takes the first mannequin bone that maps onto it.</summary>
    private static bool TryGetToon(IReadOnlyDictionary<string, Quaternion> rotations, string toonBone, out Quaternion q)
    {
        foreach (var (mannequin, toon) in ToonRig.BoneMap)
            if (toon == toonBone && rotations.TryGetValue(mannequin, out q)) return true;
        q = Quaternion.Identity;
        return false;
    }

    public static Vector3[] SkinPositions(MeshData mesh, Matrix4x4[] skin)
    {
        var posed = new Vector3[mesh.Positions.Length];
        for (var v = 0; v < posed.Length; v++)
        {
            var p = Vector3.Zero;
            foreach (var influence in mesh.Weights[v])
                p += Vector3.Transform(mesh.Positions[v], skin[influence.Bone]) * influence.Weight;
            posed[v] = p;
        }
        return posed;
    }

    /// <summary>Joint positions after posing.</summary>
    public static Vector3[] BonePositions(Skeleton skeleton, Matrix4x4[] skin) =>
        skeleton.Bones.Select((b, i) => Vector3.Transform(b.Position, skin[i])).ToArray();

    public static Dictionary<string, Quaternion> GetPose(PreviewPose pose)
    {
        var p = new Dictionary<string, Quaternion>();

        // Rotations are about model axes: X = left, Y = back, Z = up (right-handed).
        static Quaternion Rot(Vector3 axis, float degrees) => Quaternion.CreateFromAxisAngle(axis, Geo.Deg(degrees));

        // Both arms/legs with mirrored rotations (rotations about Y and Z flip sign on the right).
        void Sym(string bone, float aboutX, float aboutY, float aboutZ)
        {
            p[bone + "_l"] = Rot(Vector3.UnitZ, aboutZ) * Rot(Vector3.UnitY, aboutY) * Rot(Vector3.UnitX, aboutX);
            p[bone + "_r"] = Rot(Vector3.UnitZ, -aboutZ) * Rot(Vector3.UnitY, -aboutY) * Rot(Vector3.UnitX, aboutX);
        }

        switch (pose)
        {
            case PreviewPose.APose:
                Sym("upperarm", 0, 50, 0);
                break;
            case PreviewPose.Relaxed:
                Sym("upperarm", 0, 76, 0);
                Sym("lowerarm", 0, 0, -18);      // elbows bend forward
                break;
            case PreviewPose.Walk:
                // Left leg forward, right arm forward.
                p["upperarm_l"] = Rot(Vector3.UnitX, 28) * Rot(Vector3.UnitY, 74);
                p["upperarm_r"] = Rot(Vector3.UnitX, -30) * Rot(Vector3.UnitY, -74);
                p["lowerarm_l"] = Rot(Vector3.UnitZ, -12);
                p["lowerarm_r"] = Rot(Vector3.UnitZ, 38);
                p["thigh_l"] = Rot(Vector3.UnitX, -32);
                p["calf_l"] = Rot(Vector3.UnitX, 12);
                p["thigh_r"] = Rot(Vector3.UnitX, 24);
                p["calf_r"] = Rot(Vector3.UnitX, 38);
                p["spine_03"] = Rot(Vector3.UnitZ, 6);
                break;
            case PreviewPose.Action:
                p["spine_02"] = Rot(Vector3.UnitX, 10) * Rot(Vector3.UnitZ, -12);
                p["head"] = Rot(Vector3.UnitZ, 22);
                p["upperarm_l"] = Rot(Vector3.UnitZ, -62) * Rot(Vector3.UnitY, 18);
                p["lowerarm_l"] = Rot(Vector3.UnitZ, -64);
                p["upperarm_r"] = Rot(Vector3.UnitY, -58) * Rot(Vector3.UnitZ, 40);
                p["lowerarm_r"] = Rot(Vector3.UnitZ, 82);
                p["thigh_l"] = Rot(Vector3.UnitX, -58) * Rot(Vector3.UnitY, -8);
                p["calf_l"] = Rot(Vector3.UnitX, 84);
                break;
        }
        return p;
    }
}

using System.Numerics;

namespace LowPolyCharGen;

/// <summary>One joint. Position and rotation are in model (world) space, centimetres.</summary>
public sealed class Bone
{
    public required string Name { get; init; }
    public int Index { get; init; }
    public int Parent { get; init; } = -1;
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    /// <summary>Uniform world scale (1 for the mannequin; the Toon Soldiers rig carries 1.8 from its Biped root).</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>Bone-to-model matrix (row-vector convention, translation in the last row).</summary>
    public Matrix4x4 World => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);
}

/// <summary>
/// Bone hierarchy that is name-, hierarchy- and proportion-compatible with the Unreal Engine 5 mannequin
/// (SK_Mannequin), bound in a T-pose instead of the mannequin's A-pose.
/// Space: +X = character left, -Y = front, +Z = up (right-handed, the same space the FBX is written in).
/// </summary>
public sealed class Skeleton
{
    private readonly List<Bone> _bones = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public IReadOnlyList<Bone> Bones => _bones;
    public Bone this[string name] => _bones[IndexOf(name)];
    public Bone this[int index] => _bones[index];

    public int IndexOf(string name) =>
        _index.TryGetValue(name, out var i) ? i : throw new KeyNotFoundException($"No bone named '{name}'.");

    /// <summary>Index of the bone on the other side of the body (itself for centre bones).</summary>
    public int Mirror(int index)
    {
        var name = _bones[index].Name;
        var other = name.EndsWith("_l", StringComparison.Ordinal) ? name[..^2] + "_r"
                  : name.EndsWith("_r", StringComparison.Ordinal) ? name[..^2] + "_l"
                  : name;
        return _index.GetValueOrDefault(other, index);
    }

    /// <summary>Translation, rotation and scale relative to the parent bone.</summary>
    public (Vector3 Translation, Quaternion Rotation, float Scale) Local(int index)
    {
        var bone = _bones[index];
        if (bone.Parent < 0) return (bone.Position, bone.Rotation, bone.Scale);
        var parent = _bones[bone.Parent];
        var inv = Quaternion.Inverse(parent.Rotation);
        return (Vector3.Transform(bone.Position - parent.Position, inv) / parent.Scale,
                Quaternion.Normalize(inv * bone.Rotation), bone.Scale / parent.Scale);
    }

    /// <summary>The rig this skeleton is: decides bone names and what the exported file binds to.</summary>
    public RigTarget Target { get; private init; } = RigTarget.Mannequin;

    public bool Contains(string name) => _index.ContainsKey(name);

    private Bone Add(string name, string? parent, Vector3 position, Quaternion rotation, float scale = 1f)
    {
        var bone = new Bone
        {
            Name = name,
            Index = _bones.Count,
            Parent = parent is null ? -1 : IndexOf(parent),
            Position = position,
            Rotation = Quaternion.Normalize(rotation),
            Scale = scale,
        };
        _index.Add(name, bone.Index);
        _bones.Add(bone);
        return bone;
    }

    /// <summary>
    /// The mannequin convention for a right-side bone: the left bone's frame mirrored across the
    /// sagittal plane with all three axes negated (so it stays a proper rotation).
    /// </summary>
    private static Quaternion MirrorRotation(Quaternion q) =>
        Quaternion.Normalize(new Quaternion(q.X, -q.Y, -q.Z, q.W) * new Quaternion(1, 0, 0, 0));

    private static Vector3 MirrorPosition(Vector3 p) => new(-p.X, p.Y, p.Z);

    public static Skeleton CreateMannequinTPose(bool includeIkBones = true)
    {
        var s = new Skeleton();
        foreach (var e in MannequinData.Bones)
            s.Add(e.Name, e.Parent, e.Position, e.Rotation);

        // Trunk and legs are identical in the A- and T-pose. Only the arm chains are re-posed:
        // the mannequin's left-arm bones point along their local +X with the elbow hinge on local Z,
        // so a straight horizontal arm with the palm down is simply the identity rotation.
        var upper = s["upperarm_l"];
        var lower = s["lowerarm_l"];
        var hand = s["hand_l"];
        var upperLength = Vector3.Distance(upper.Position, lower.Position);
        var foreLength = Vector3.Distance(lower.Position, hand.Position);

        upper.Rotation = Quaternion.Identity;
        lower.Rotation = Quaternion.Identity;
        lower.Position = upper.Position + new Vector3(upperLength, 0, 0);
        hand.Position = lower.Position + new Vector3(foreLength, 0, 0);
        // The hand's local -Z points at the thumb; a quarter turn about the arm axis puts the thumb
        // forward (-Y) and the palm down.
        hand.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2);

        // Make the right side an exact mirror of the left.
        foreach (var bone in s._bones)
        {
            if (!bone.Name.EndsWith("_r", StringComparison.Ordinal)) continue;
            var left = s[bone.Name[..^2] + "_l"];
            bone.Position = MirrorPosition(left.Position);
            bone.Rotation = MirrorRotation(left.Rotation);
        }

        if (includeIkBones)
        {
            var handR = s["hand_r"];
            var handL = s["hand_l"];
            s.Add("ik_foot_root", "root", Vector3.Zero, Quaternion.Identity);
            s.Add("ik_foot_l", "ik_foot_root", s["foot_l"].Position, s["foot_l"].Rotation);
            s.Add("ik_foot_r", "ik_foot_root", s["foot_r"].Position, s["foot_r"].Rotation);
            s.Add("ik_hand_root", "root", Vector3.Zero, Quaternion.Identity);
            s.Add("ik_hand_gun", "ik_hand_root", handR.Position, handR.Rotation);
            s.Add("ik_hand_l", "ik_hand_gun", handL.Position, handL.Rotation);
            s.Add("ik_hand_r", "ik_hand_gun", handR.Position, handR.Rotation);
        }

        return s;
    }

    /// <summary>
    /// The Toon Soldiers pack skeleton (Characters_Skeleton) in its exact reference pose, so generated
    /// meshes import onto it and play the pack's animations unchanged.
    /// </summary>
    public static Skeleton CreateToonSoldier()
    {
        var s = new Skeleton { Target = RigTarget.ToonSoldiers };
        foreach (var e in ToonSoldierData.Bones)
            s.Add(e.Name, e.Parent, e.Position, e.Rotation, e.Scale);
        return s;
    }
}

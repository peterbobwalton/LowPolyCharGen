using System.Numerics;

namespace LowPolyCharGen;

/// <summary>
/// Gives a character built on the mannequin frame its own frame: a different height and shoulder
/// width on the same bones. The joints move (the same names and hierarchy, a different reference
/// pose, as Unreal's Manny and Quinn share one skeleton) and every vertex follows the weighted blend
/// of its bones, so clothes, gear and hair come along.
/// </summary>
public static class BodyFrame
{
    /// <summary>
    /// Moves the arm chains in by <paramref name="shoulderShift"/> centimetres (the clavicles half as
    /// far, so the shoulder line narrows smoothly) and then scales everything about the ground point
    /// under the character by <paramref name="heightScale"/>.
    /// </summary>
    public static (Skeleton Skeleton, MeshData Mesh) Apply(Skeleton design, MeshData mesh, float heightScale, float shoulderShift,
        bool includeIkBones, float creaseDegrees)
    {
        var shifts = new Vector3[design.Bones.Count];
        foreach (var bone in design.Bones)
        {
            var side = bone.Name.EndsWith("_l", StringComparison.Ordinal) ? 1f
                     : bone.Name.EndsWith("_r", StringComparison.Ordinal) ? -1f : 0f;
            if (side == 0f) continue;
            var name = bone.Name[..^2];
            var amount = name switch
            {
                "clavicle" => 0.5f,
                "upperarm" or "lowerarm" or "hand" => 1f,
                _ => 0f,
            };
            shifts[bone.Index] = new Vector3(-side * shoulderShift * amount, 0, 0);
        }

        Vector3 Move(Vector3 p) => p * heightScale;

        var skeleton = Skeleton.CreateMannequinTPose(includeIkBones);
        foreach (var bone in skeleton.Bones)
        {
            // The IK bones copy the bone they stand in for.
            var source = bone.Name switch
            {
                "ik_foot_l" => "foot_l",
                "ik_foot_r" => "foot_r",
                "ik_hand_gun" or "ik_hand_r" => "hand_r",
                "ik_hand_l" => "hand_l",
                _ => bone.Name,
            };
            var from = design[source];
            bone.Position = Move(from.Position + shifts[from.Index]);
        }

        var positions = new Vector3[mesh.Positions.Length];
        for (var v = 0; v < positions.Length; v++)
        {
            var shift = Vector3.Zero;
            foreach (var w in mesh.Weights[v]) shift += shifts[w.Bone] * w.Weight;
            positions[v] = Move(mesh.Positions[v] + shift);
        }

        return (skeleton, new MeshData
        {
            Positions = positions,
            Weights = mesh.Weights,
            Faces = mesh.Faces,
            Charts = mesh.Charts,
            Normals = MeshData.ComputeNormals(positions, mesh.Faces, creaseDegrees),
            Uvs = mesh.Uvs,
        });
    }
}

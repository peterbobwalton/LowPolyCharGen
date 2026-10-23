using LowPolyCharGen.Parts;
using LowPolyCharGen.Texturing;

namespace LowPolyCharGen;

/// <summary>A generated character: skeleton, skinned and UV-mapped mesh, and what the painter needs to texture it.</summary>
public sealed class CharacterModel
{
    public required CharacterSpec Spec { get; init; }

    /// <summary>The skeleton the character is exported on (see <see cref="CharacterSpec.Rig"/>).</summary>
    public required Skeleton Skeleton { get; init; }

    /// <summary>The exported mesh, bound to <see cref="Skeleton"/>.</summary>
    public required MeshData Mesh { get; init; }

    /// <summary>
    /// The mesh as built, on the mannequin T-pose. Same faces and UVs as <see cref="Mesh"/>; the painter
    /// works from these positions, which is what all the paint landmarks refer to.
    /// </summary>
    public required MeshData DesignMesh { get; init; }

    /// <summary>Landmarks for the texture painter.</summary>
    public required PaintContext Paint { get; init; }

    /// <summary>A finished texture used as is instead of painting one (pack characters).</summary>
    public TextureImage? FixedTexture { get; init; }

    /// <summary>Paints the character's texture at the given size (see <see cref="TextureBaker"/>).</summary>
    public TextureImage BakeTexture(int size) => FixedTexture ?? TextureBaker.Bake(this, size);
}

public static class CharacterGenerator
{
    /// <summary>Crease angle used for smooth shading: tubes stay smooth, box edges and caps stay hard.</summary>
    public const float SmoothCreaseDegrees = 62f;

    public static CharacterModel Generate(CharacterSpec spec, bool includeIkBones = true)
    {
        // The painter keeps reading the options while it bakes, possibly on another thread.
        spec = spec.Clone();
        if (spec.Body == BodyModel.Pack) return Kit.KitBuilder.Build(spec);
        var skeleton = Skeleton.CreateMannequinTPose(includeIkBones);
        // The realistic body has modelled fingers, so it gets the mannequin's finger bones.
        if (spec.Rig == RigTarget.Mannequin && spec.Body == BodyModel.Realistic) skeleton.AddFingers();
        var context = new BuildContext(spec, skeleton);

        BodyBuilder.Build(context);
        HeadBuilder.BuildExtras(context);
        GearBuilder.Build(context);

        var crease = spec.FlatShaded ? 0f : SmoothCreaseDegrees;
        (MeshData Design, Skeleton Skeleton, MeshData Mesh) Finish()
        {
            var built = context.Mesh.Build(crease);
            var (rig, mesh) = spec.Rig == RigTarget.ToonSoldiers
                ? ToonRig.Convert(skeleton, built, crease)
                : spec.Gender == Gender.Female
                    ? BodyFrame.Apply(skeleton, built, BuildContext.FemaleHeightScale, BuildContext.FemaleShoulderShift, includeIkBones, crease)
                    : (skeleton, built);
            return (built, rig, mesh);
        }

        // Leave out the triangles hidden inside other parts. The toon proportions move parts against
        // each other, so a triangle goes only if it is hidden in both shapes.
        var (design, outSkeleton, outMesh) = Finish();
        bool[] Hidden(bool[]? occluders, bool[]? candidates)
        {
            var hidden = HiddenFaces.Find(design.Positions, design.Faces, occluders, candidates);
            if (outMesh != design)
            {
                var hiddenOut = HiddenFaces.Find(outMesh.Positions, outMesh.Faces, occluders, candidates);
                for (var f = 0; f < hidden.Length; f++) hidden[f] &= hiddenOut[f];
            }
            return hidden;
        }
        var hidden = Hidden(null, null);
        // A triangle hidden only behind other hidden triangles would show once they are gone: check
        // again against what stays, putting back the ones that see out, until nothing changes.
        while (hidden.Contains(true))
        {
            var again = Hidden(Array.ConvertAll(hidden, h => !h), hidden);
            if (again.AsSpan().SequenceEqual(hidden)) break;
            hidden = again;
        }
        if (hidden.Contains(true))
        {
            context.Mesh.RemoveFaces(hidden);
            (design, outSkeleton, outMesh) = Finish();
        }

        return new CharacterModel
        {
            Spec = spec,
            Skeleton = outSkeleton,
            Mesh = outMesh,
            DesignMesh = design,
            Paint = context.Paint,
        };
    }
}

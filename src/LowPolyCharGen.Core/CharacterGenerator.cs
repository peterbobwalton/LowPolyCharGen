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

    /// <summary>Paints the character's texture at the given size (see <see cref="TextureBaker"/>).</summary>
    public TextureImage BakeTexture(int size) => TextureBaker.Bake(this, size);
}

public static class CharacterGenerator
{
    /// <summary>Crease angle used for smooth shading: tubes stay smooth, box edges and caps stay hard.</summary>
    public const float SmoothCreaseDegrees = 62f;

    public static CharacterModel Generate(CharacterSpec spec, bool includeIkBones = true)
    {
        // The painter keeps reading the options while it bakes, possibly on another thread.
        spec = spec.Clone();
        var skeleton = Skeleton.CreateMannequinTPose(includeIkBones);
        var context = new BuildContext(spec, skeleton);

        BodyBuilder.Build(context);
        HeadBuilder.BuildExtras(context);
        GearBuilder.Build(context);

        var crease = spec.FlatShaded ? 0f : SmoothCreaseDegrees;
        var design = context.Mesh.Build(crease);
        var (outSkeleton, outMesh) = spec.Rig == RigTarget.ToonSoldiers
            ? ToonRig.Convert(skeleton, design, crease)
            : (skeleton, design);

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

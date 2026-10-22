using System.Numerics;

namespace LowPolyCharGen.Texturing;

/// <summary>
/// Where a scalp covering (hair, a cap) ends, as head heights (0 = chin, 1 = crown) at the front of
/// the head, the temples, behind the ears and the back.
/// </summary>
public readonly record struct ScalpEdge(float Front, float Temple, float BehindEar, float Back)
{
    public ScalpEdge(float all) : this(all, all, all, all) { }

    /// <summary>
    /// How far (head heights) the hairline recedes at the corners of the forehead, above the outer
    /// ends of the eyebrows: a little for most men, a touch for women. It breaks up the straight
    /// line across the forehead that made short hair look like a bowl cut.
    /// </summary>
    public float Recession { get; init; }

    /// <param name="frontness">-1 at the very front of the head to +1 at the very back.</param>
    public float At(float frontness)
    {
        var s = frontness;
        var corners = Recession * MathF.Exp(-MathF.Pow((s + 0.8f) / 0.1f, 2f));
        return Edge(s) + corners;
    }

    private float Edge(float s)
    {
        if (s <= -0.92f) return Front;
        if (s <= -0.38f) return Geo.Lerp(Front, Temple, (s + 0.92f) / 0.54f);
        if (s <= 0.38f) return Geo.Lerp(Temple, BehindEar, (s + 0.38f) / 0.76f);
        if (s <= 0.92f) return Geo.Lerp(BehindEar, Back, (s - 0.38f) / 0.54f);
        return Back;
    }
}

/// <summary>
/// The lips of a modelled mouth, measured from the face in columns out from the middle: the height of
/// the line where the lips meet, of the upper lip's top edge and of the lower lip's bottom edge.
/// </summary>
public sealed class LipProfile(float step, float[] seam, float[] upper, float[] lower, float corner, float frontY)
{
    /// <summary>Distance out from the middle where the lips end, in centimetres.</summary>
    public float Corner { get; } = corner;

    /// <summary>Depth just behind the lips: texels further back (inside the mouth) are not lips.</summary>
    public float FrontY { get; } = frontY;

    public (float Seam, float Upper, float Lower) At(float x)
    {
        var f = Math.Clamp(MathF.Abs(x) / step, 0f, seam.Length - 1.001f);
        var i = (int)f;
        var t = f - i;
        return (Geo.Lerp(seam[i], seam[i + 1], t), Geo.Lerp(upper[i], upper[i + 1], t), Geo.Lerp(lower[i], lower[i + 1], t));
    }
}

/// <summary>
/// The landmarks the texture painter needs: where the eyes, hems, cuffs and boot tops are on this
/// particular character. Filled in while the mesh is built.
/// </summary>
public sealed class PaintContext
{
    public required CharacterSpec Spec { get; init; }
    public bool Female { get; init; }

    // ---- head ----
    public Vector3 HeadCenter { get; init; }
    public float HeadScale { get; init; }
    public float ChinZ { get; init; }
    public float HeadHeight { get; init; }
    public float HeadHalfWidth { get; init; }
    public float EyeX { get; init; }
    public float EyeZ { get; init; }
    public float BrowZ { get; init; }
    public float NoseBaseZ { get; init; }
    public float NoseTipZ { get; init; }

    /// <summary>Left nostril in face centimetres (across, above the chin), set from the nose mesh.</summary>
    public Vector2 Nostril { get; set; } = new(0.85f, 8.45f);
    public float MouthZ { get; set; }

    /// <summary>The modelled mouth's lips, measured off the face (null: the lips are painted to a fixed shape).</summary>
    public LipProfile? Lips { get; set; }

    /// <summary>Hair painted straight onto the scalp (null when bald).</summary>
    public ScalpEdge? ScalpHair { get; set; }

    /// <summary>How solidly the painted hair covers the scalp (a buzz cut lets skin show through).</summary>
    public float ScalpHairDensity { get; set; } = 1f;

    /// <summary>Left ear: centre and the two axes of its plane (towards its back rim, and up).</summary>
    public Vector3 EarCenter { get; set; }
    public Vector3 EarAcross { get; set; }
    public Vector3 EarUp { get; set; }
    public float EarScale { get; set; } = 1f;

    // ---- torso and arms ----
    public float NeckBaseZ { get; init; }
    public Vector3 NeckCenter { get; init; }
    public float NeckRadius { get; init; }
    public float ArmScale { get; init; }

    // ---- realistic hands (mannequin rig): hand space is the template's, see HandTemplate ----
    /// <summary>True when the hands are the modelled hand with fingers (not the toon fist).</summary>
    public bool ModelledHands { get; set; }
    /// <summary>Where the left hand's space starts (its wrist) and how much it is scaled.</summary>
    public Vector3 HandOrigin { get; set; }
    public float HandScale { get; set; } = 1f;
    public IReadOnlyList<Parts.HandTemplate.Nail> Nails { get; set; } = [];

    // ---- realistic face: the modelled eyes and lids replace the painted ones ----
    public bool ModelledFace { get; set; }
    /// <summary>Centre of the left eyeball (the right is its mirror) and its radius.</summary>
    public Vector3 EyeCenter { get; set; }
    public float EyeRadius { get; set; } = 1.2f;

    /// <summary>Left ankle joint.</summary>
    public Vector3 Ankle { get; init; }

    /// <summary>Height where the shirt ends and the trousers begin.</summary>
    public float HemZ { get; set; }
    public float BeltBottomZ { get; init; }
    public float BeltTopZ { get; init; }
    public float ArmY { get; init; }
    public float ArmZ { get; init; }
    public float ShoulderX { get; init; }
    public float ElbowX { get; init; }
    public float WristX { get; init; }

    /// <summary>Distance along the arm where the sleeve ends (0 = no sleeve).</summary>
    public float SleeveEndX { get; set; }

    // ---- legs ----
    public required Func<float, Vector3> LegCenter { get; init; }
    public float KneeZ { get; init; }

    /// <summary>Height where the trouser leg (or shorts) ends.</summary>
    public float TrouserEndZ { get; set; }
    public float BootTopZ { get; set; }

    /// <summary>Top of a visible sock, or 0.</summary>
    public float SockTopZ { get; set; }
    public bool SkirtWorn { get; set; }
}

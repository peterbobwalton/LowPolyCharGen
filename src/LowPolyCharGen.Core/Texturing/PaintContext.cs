using System.Numerics;

namespace LowPolyCharGen.Texturing;

/// <summary>
/// Where a scalp covering (hair, a cap) ends, as head heights (0 = chin, 1 = crown) at the front of
/// the head, the temples, behind the ears and the back.
/// </summary>
public readonly record struct ScalpEdge(float Front, float Temple, float BehindEar, float Back)
{
    public ScalpEdge(float all) : this(all, all, all, all) { }

    /// <param name="frontness">-1 at the very front of the head to +1 at the very back.</param>
    public float At(float frontness)
    {
        var s = frontness;
        if (s <= -0.92f) return Front;
        if (s <= -0.38f) return Geo.Lerp(Front, Temple, (s + 0.92f) / 0.54f);
        if (s <= 0.38f) return Geo.Lerp(Temple, BehindEar, (s + 0.38f) / 0.76f);
        if (s <= 0.92f) return Geo.Lerp(BehindEar, Back, (s - 0.38f) / 0.54f);
        return Back;
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
    public float MouthZ { get; init; }

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

using System.Numerics;
using LowPolyCharGen.Texturing;

namespace LowPolyCharGen.Parts;

/// <summary>Shared measurements, surfaces and weight functions for one character build.</summary>
internal sealed class BuildContext
{
    /// <summary>Sides of the free-standing gear lofts.</summary>
    public const int Sides = 8;
    public const float FlatFront = MathF.PI / Sides;   // angle offset that puts a flat face front and back

    /// <summary>
    /// Columns of the left half of the head, as degrees round from the front. They are denser near
    /// the middle of the face so the nose has its own vertices.
    /// </summary>
    public static readonly float[] HeadAngles = [0f, 11f, 30f, 56f, 86f, 120f, 152f, 180f];

    /// <summary>Rows of the head as heights above the jaw line (0) up to the crown ring.</summary>
    public static readonly float[] HeadRows = [0f, 0.20f, 0.33f, 0.41f, 0.52f, 0.62f, 0.76f, 0.90f, 0.975f];

    public CharacterSpec Spec { get; }
    public Skeleton Skeleton { get; }
    public MeshBuilder Mesh { get; }
    public Skinning Skin { get; }
    public PaintContext Paint { get; }

    public bool Female { get; }
    public float WidthScale { get; }
    public float DepthScale { get; }
    public float ArmScale { get; }
    public float LegScale { get; }

    /// <summary>Hips, torso and the base of the neck as stacked cross-sections.</summary>
    public Profile Torso { get; }
    public Profile Hips => Torso;
    public Profile Head { get; }
    public Limb ArmL { get; }
    public Limb LegL { get; }

    /// <summary>Pelvis, spine, neck and head blended by height.</summary>
    public WeightFn Spine { get; }
    public WeightFn HeadRigid { get; }

    // Arm line (T-pose): the arm runs along +X at this Y and Z.
    public float ArmY { get; }
    public float ArmZ { get; }
    public float ShoulderX { get; }
    public float ElbowX { get; }
    public float WristX { get; }

    // Head metrics.
    public float HeadScale { get; }
    public float HeadHeight { get; }
    public float ChinZ { get; }
    public float HeadHalfWidth { get; }
    public float HeadHalfDepth { get; }
    public float HeadExp { get; }
    public Vector3 HeadCenter { get; }
    public float NeckRadius { get; }

    /// <summary>Height of the jaw line at each head column (0 = chin level, as a fraction of the head).</summary>
    public float[] JawLine { get; }

    // Face sculpting amounts, in centimetres at head scale 1.
    public float NoseLength { get; }
    public float NoseHalfWidth { get; }
    public float BrowRidge { get; }
    public float ChinForward { get; }

    /// <summary>Height on the head; 0 = chin, 1 = crown.</summary>
    public float HeadZ(float t) => ChinZ + t * HeadHeight;

    /// <summary>Head height (0..1) of a row at a column: rows are squeezed above the jaw line.</summary>
    public float HeadRowT(int row, int column) => JawLine[column] + HeadRows[row] * (1f - JawLine[column]);

    /// <summary>Parameter angle on the head's cross-sections for an angle measured round from the front.</summary>
    public static float HeadTheta(float degreesFromFront) => Geo.Deg(270f + degreesFromFront);

    public BuildContext(CharacterSpec spec, Skeleton skeleton)
    {
        Spec = spec;
        Skeleton = skeleton;
        Mesh = new MeshBuilder(skeleton);
        Skin = new Skinning(skeleton);

        Female = spec.Gender == Gender.Female;
        WidthScale = Geo.Lerp(0.92f, 1.14f, spec.Build);
        DepthScale = Geo.Lerp(0.90f, 1.20f, spec.Build);
        ArmScale = Geo.Lerp(0.90f, 1.14f, spec.Build) * (Female ? 0.86f : 1f);
        LegScale = Geo.Lerp(0.92f, 1.12f, spec.Build) * (Female ? 0.94f : 1f);

        Spine = Skin.Chain(p => p.Z, 3f,
            ("pelvis", float.NegativeInfinity),
            ("spine_01", skeleton["spine_01"].Position.Z),
            ("spine_02", skeleton["spine_02"].Position.Z),
            ("spine_03", skeleton["spine_03"].Position.Z),
            ("spine_04", skeleton["spine_04"].Position.Z),
            ("spine_05", skeleton["spine_05"].Position.Z),
            ("neck_01", skeleton["neck_01"].Position.Z),
            ("neck_02", skeleton["neck_02"].Position.Z),
            ("head", skeleton["head"].Position.Z));
        HeadRigid = Skin.Rigid("head");

        Torso = BuildTorso();

        var shoulder = skeleton["upperarm_l"].Position;
        ArmY = shoulder.Y;
        ArmZ = shoulder.Z;
        ShoulderX = shoulder.X;
        ElbowX = skeleton["lowerarm_l"].Position.X;
        WristX = skeleton["hand_l"].Position.X;
        ArmL = BuildArm();
        LegL = BuildLeg();

        // ---- head: scaled about the head joint so the neck keeps its anatomical pivot ----
        var joint = skeleton["head"].Position;
        var s = HeadScale = spec.HeadSize;
        HeadHeight = 23.9f * s;
        ChinZ = joint.Z - 6.0f * s;
        var centerFront = -joint.Y + 0.4f * s;
        HeadCenter = Geo.P(0, centerFront, HeadZ(0.5f));

        // Face shapes. Widths are half-widths in centimetres at scale 1.
        float skull = 7.7f, cheek = 7.1f, jaw = 6.2f, chin = 2.7f, jawExp = 2.3f, jawCorner = 0.19f;
        NoseLength = 2.2f;
        NoseHalfWidth = 1.75f;
        BrowRidge = 0.55f;
        ChinForward = 0.5f;
        switch (spec.HeadShape)
        {
            case HeadShape.Square:
                jaw = 7.0f; chin = 3.5f; jawExp = 3.0f; jawCorner = 0.16f; ChinForward = 0.8f;
                break;
            case HeadShape.Round:
                skull = 8.0f; cheek = 7.6f; jaw = 6.6f; chin = 3.1f; jawExp = 2.0f; jawCorner = 0.21f; ChinForward = 0.2f; BrowRidge = 0.3f;
                break;
            case HeadShape.Slim:
                skull = 7.2f; cheek = 6.5f; jaw = 5.5f; chin = 2.2f; jawExp = 2.1f; NoseLength = 2.4f; NoseHalfWidth = 1.55f;
                break;
            case HeadShape.Heavy:
                skull = 7.9f; cheek = 7.4f; jaw = 6.9f; chin = 3.2f; jawExp = 2.7f; jawCorner = 0.17f;
                BrowRidge = 1.0f; NoseLength = 2.5f; NoseHalfWidth = 2.0f; ChinForward = 0.7f;
                break;
        }
        if (Female)
        {
            jaw *= 0.91f; chin *= 0.82f; cheek *= 0.97f; skull *= 0.97f;
            jawExp = MathF.Min(jawExp, 2.4f);
            BrowRidge *= 0.4f; NoseLength *= 0.88f; NoseHalfWidth *= 0.86f; ChinForward *= 0.6f;
        }
        HeadHalfWidth = skull * s;
        HeadHalfDepth = 9.6f * s;
        HeadExp = 2.2f;
        NeckRadius = (Female ? 4.8f : 5.5f) * Geo.Lerp(0.95f, 1.1f, spec.Build);
        JawLine = [0f, 0.004f, 0.03f, 0.10f, jawCorner, 0.27f, 0.27f, 0.25f];

        Head = new Profile();
        void Sec(float t, float halfWidth, float front, float back, float exp) =>
            Head.Add(HeadZ(t), centerFront, halfWidth * s, front * s, back * s, exp);
        Sec(0.00f, chin, 8.3f, 4.0f, jawExp);
        Sec(0.10f, Geo.Lerp(chin, jaw, 0.72f), 9.0f, 4.6f, jawExp);
        Sec(0.20f, jaw, 9.3f, 6.2f, jawExp);
        Sec(0.33f, Geo.Lerp(jaw, cheek, 0.75f), 9.4f, 8.4f, 2.3f);
        Sec(0.42f, cheek, 9.3f, 9.2f, 2.25f);
        Sec(0.52f, Geo.Lerp(cheek, skull, 0.5f), 9.0f, 9.7f, 2.2f);
        Sec(0.62f, skull * 0.985f, 9.3f, 9.9f, 2.2f);
        Sec(0.76f, skull, 8.9f, 9.6f, 2.2f);
        Sec(0.90f, skull * 0.82f, 6.7f, 7.6f, 2.2f);
        Sec(0.975f, skull * 0.45f, 3.5f, 4.0f, 2.2f);
        Sec(1.00f, 0.05f, 0.05f, 0.05f, 2.2f);

        Paint = new PaintContext
        {
            Spec = spec,
            Female = Female,
            HeadCenter = HeadCenter,
            HeadScale = s,
            ChinZ = ChinZ,
            HeadHeight = HeadHeight,
            HeadHalfWidth = HeadHalfWidth,
            EyeX = 3.15f * s * (skull / 7.7f),
            EyeZ = HeadZ(0.52f),
            BrowZ = HeadZ(0.595f),
            NoseBaseZ = HeadZ(0.33f),
            NoseTipZ = HeadZ(0.41f),
            MouthZ = HeadZ(0.215f),
            NeckBaseZ = 153.2f,
            NeckCenter = Geo.P(0, 0.3f, 156f),
            NeckRadius = NeckRadius,
            ArmScale = ArmScale,
            Ankle = skeleton["foot_l"].Position,
            HemZ = 101.5f,
            BeltBottomZ = 99.4f,
            BeltTopZ = 103.2f,
            ArmY = ArmY,
            ArmZ = ArmZ,
            ShoulderX = ShoulderX,
            ElbowX = ElbowX,
            WristX = WristX,
            LegCenter = LegL.Center,
            KneeZ = skeleton["calf_l"].Position.Z,
        };
    }

    private Profile BuildTorso()
    {
        var p = new Profile();
        void Sec(float z, float centerFront, float halfWidth, float front, float back) =>
            p.Add(z, centerFront, halfWidth * WidthScale, front * DepthScale, back * DepthScale, 2.4f);
        if (Female)
        {
            Sec(81.5f, 2.0f, 18.6f, 9.4f, 10.8f);
            Sec(89f, 2.0f, 18.8f, 10.0f, 12.0f);
            Sec(96f, 2.1f, 16.8f, 9.8f, 10.6f);
            Sec(102f, 2.2f, 13.6f, 9.2f, 9.0f);
            Sec(111f, 2.5f, 12.6f, 9.0f, 8.6f);
            Sec(121f, 2.4f, 13.4f, 12.8f, 9.2f);    // under the bust
            Sec(130f, 2.0f, 15.2f, 18.6f, 9.6f);    // bust
            Sec(137.5f, 1.4f, 15.6f, 15.0f, 9.8f);
            Sec(143.6f, 0.8f, 15.4f, 10.0f, 9.8f);
            Sec(149.5f, 0.2f, 12.6f, 8.0f, 9.0f);
            Sec(153.2f, -0.2f, 6.4f, 5.6f, 6.4f);
        }
        else
        {
            // Muscular: deep chest and lats over a narrow waist (the V-taper), heavy traps.
            Sec(81.5f, 2.0f, 17.8f, 9.6f, 10.4f);
            Sec(89f, 2.0f, 17.4f, 10.2f, 11.4f);
            Sec(96f, 2.1f, 15.2f, 10.0f, 10.2f);
            Sec(102f, 2.2f, 13.2f, 9.8f, 9.0f);
            Sec(111f, 2.5f, 12.8f, 10.0f, 8.8f);    // waist
            Sec(121f, 2.4f, 15.8f, 12.4f, 11.0f);
            Sec(130f, 2.0f, 18.8f, 14.8f, 12.6f);   // pecs and lats
            Sec(137.5f, 1.4f, 19.0f, 14.0f, 12.6f);
            Sec(143.6f, 0.8f, 18.4f, 12.0f, 11.8f);
            Sec(149.5f, 0.2f, 16.0f, 9.2f, 10.8f);  // traps
            Sec(153.2f, -0.2f, 7.2f, 6.2f, 7.2f);
        }
        return p;
    }

    private Limb BuildArm()
    {
        var arm = new Limb
        {
            Center = x => new Vector3(x, ArmY, ArmZ),
            U = Geo.Back,
            V = Geo.Up,
            Scale = ArmScale,
        };
        // (distance along the arm, radius front/back, radius up, radius down)
        // Men get rounder deltoids, which is what makes the shoulders read broad.
        var delt = Female ? 1f : 1.25f;
        arm.Add(16.5f, 6.3f * delt, 6.2f * delt, 6.2f)
           .Add(22.5f, 5.9f * delt, 6.0f * delt, 5.6f)
           .Add(31f, 5.2f, 5.1f, 5.2f)
           .Add(ElbowX - 3.5f, 4.7f, 4.6f, 4.7f)
           .Add(ElbowX, 4.5f, 4.4f, 4.5f)
           .Add(ElbowX + 3.5f, 4.8f, 4.6f, 4.8f)
           .Add(57f, 4.9f, 4.4f, 4.6f)
           .Add(67.5f, 3.8f, 3.1f, 3.2f)
           .Add(WristX, 3.3f, 2.5f, 2.6f, 2.4f);
        return arm;
    }

    private Limb BuildLeg()
    {
        var hip = Skeleton["thigh_l"].Position;
        var knee = Skeleton["calf_l"].Position;
        var ankle = Skeleton["foot_l"].Position;
        Vector3 Center(float z)
        {
            if (z >= hip.Z) return new Vector3(hip.X, hip.Y, z);
            var p = z >= knee.Z
                ? Vector3.Lerp(knee, hip, (z - knee.Z) / (hip.Z - knee.Z))
                : Vector3.Lerp(ankle, knee, (z - ankle.Z) / (knee.Z - ankle.Z));
            return new Vector3(p.X, p.Y, z);
        }
        var leg = new Limb { Center = Center, U = Geo.Left, V = Geo.Back, Scale = LegScale };
        // (height, radius sideways, radius back, radius front)
        leg.Add(74f, 7.4f, 8.6f, 8.4f)
           .Add(64f, 7.0f, 7.9f, 7.9f)
           .Add(knee.Z + 4f, 6.3f, 6.6f, 6.8f)
           .Add(knee.Z, 5.9f, 6.0f, 6.4f)
           .Add(knee.Z - 4f, 5.7f, 6.2f, 5.8f)
           .Add(36f, 5.8f, 6.8f, 5.5f)
           .Add(22f, 4.6f, 5.0f, 4.5f)
           .Add(12f, 4.1f, 4.4f, 4.1f)
           .Add(7f, 4.3f, 4.6f, 4.3f);
        return leg;
    }
}

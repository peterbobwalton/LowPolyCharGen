using System.Numerics;
using static LowPolyCharGen.Geo;

namespace LowPolyCharGen.Parts;

/// <summary>
/// The body: hips, torso, neck, arms, hands and legs as one continuous surface (the head is welded
/// on by <see cref="HeadBuilder"/>). Only the left half is modelled; the builder mirrors it and
/// shares the vertices on the centre line. Clothing is mostly paint: the mesh only changes where a
/// garment changes the silhouette (loose trouser legs, cuffs, hems).
/// </summary>
internal static class BodyBuilder
{
    private const int HalfColumns = 7;          // front centre .. back centre, 30 degrees apart
    private const int ArmpitRow = 7;            // torso rows 7..9 surround the arm hole
    private const int LimbSides = 8;

    public static void Build(BuildContext c)
    {
        var legRings = ConfigureLegs(c);
        var armRings = ConfigureArms(c);

        c.Mesh.MirroredWelded(m =>
        {
            var torso = BuildTorsoHalf(c, m);
            HeadBuilder.BuildSkullHalf(c, m, torso[^1]);
            BuildArm(c, m, torso, armRings);
            BuildLeg(c, m, torso[0], legRings);
            BuildFoot(c, m);
            BuildPads(c, m);
        });

        if (c.Spec.Legs == LegsStyle.Skirt) BuildSkirt(c);
        if (c.Spec.Legs == LegsStyle.Thobe) BuildThobe(c);
        if (c.Spec.Scarf || c.Spec.FaceCover == FaceCover.Shemagh) BuildScarf(c);
    }

    // ---- weights -------------------------------------------------------------------------

    /// <summary>Pelvis weights that hand over to the thighs towards the outside of the crotch.</summary>
    public static WeightFn HipWeights(BuildContext c)
    {
        var thighL = c.Skin.Rigid("thigh_l");
        var thighR = c.Skin.Rigid("thigh_r");
        return Skinning.Blend(c.Spine, p => p.X >= 0 ? thighL(p) : thighR(p),
            p => 0.6f * Saturate((MathF.Abs(p.X) - 2f) / 7f) * SmoothStep((94f - p.Z) / 9f));
    }

    /// <summary>Spine weights with the hips following the thighs and the shoulder following the clavicle.</summary>
    private static WeightFn TorsoWeights(BuildContext c)
    {
        var clavicle = c.Skin.Rigid("clavicle_l");
        return Skinning.Blend(HipWeights(c), clavicle,
            p => 0.9f * Saturate((p.X - 9f) / 7f) * SmoothStep((p.Z - 133f) / 6f));
    }

    private static WeightFn ArmWeights(BuildContext c) => c.Skin.Chain(p => p.X, 3.2f,
        ("clavicle_l", float.NegativeInfinity),
        ("upperarm_l", c.ShoulderX),
        ("lowerarm_l", c.ElbowX),
        ("hand_l", c.WristX));

    private static WeightFn LegWeights(BuildContext c) => c.Skin.Chain(p => -p.Z, 3.5f,
        ("pelvis", float.NegativeInfinity),
        ("thigh_l", -c.Skeleton["thigh_l"].Position.Z),
        ("calf_l", -c.Skeleton["calf_l"].Position.Z),
        ("foot_l", -c.Skeleton["foot_l"].Position.Z));

    // ---- clothing ------------------------------------------------------------------------

    /// <summary>A ring of a limb: where it is and how thick the clothing is there.</summary>
    private readonly record struct LimbRing(float T, float Clothing);

    /// <summary>Sets up the sleeves: their thickness, where they end, and the rings of the arm.</summary>
    private static List<LimbRing> ConfigureArms(BuildContext c)
    {
        var e = c.ElbowX;
        var w = c.WristX;
        var rings = new List<LimbRing>();
        void Plain(float thickness, params float[] xs) => rings.AddRange(xs.Select(x => new LimbRing(x, thickness)));

        switch (c.Spec.Torso)
        {
            case TorsoStyle.TShirt or TorsoStyle.Polo:
                c.Paint.SleeveEndX = 33f;
                Plain(0.6f, 22.5f, 33f);
                Plain(0f, 33f, e - 3.5f, e, e + 3.5f, 57f, 67.5f, w);
                break;
            case TorsoStyle.RolledSleeves:
                c.Paint.SleeveEndX = e + 6.5f;
                Plain(0.6f, 22.5f, 31f, e - 3.5f, e);
                Plain(1.3f, e + 3f, e + 6.5f);          // the roll
                Plain(0f, e + 6.5f, 57f, 67.5f, w);
                break;
            case TorsoStyle.LongSleeve or TorsoStyle.CheckShirt or TorsoStyle.Jacket or TorsoStyle.Hoodie:
                var thick = c.Spec.Torso switch { TorsoStyle.Jacket => 0.9f, TorsoStyle.Hoodie => 1.0f, _ => 0.6f };
                c.Paint.SleeveEndX = w - 2.2f;
                Plain(thick, 22.5f, 31f, e - 3.5f, e, e + 3.5f, 57f, 67.5f);
                Plain(thick + 0.3f, w - 2.2f);          // cuff
                Plain(0f, w - 2.2f, w);
                break;
            default:   // tank top: bare arms
                c.Paint.SleeveEndX = 0f;
                Plain(0f, 22.5f, 31f, e - 3.5f, e, e + 3.5f, 57f, 67.5f, w);
                break;
        }
        var end = c.Paint.SleeveEndX;
        var sleeve = rings.Where(r => r.T < end).Select(r => r.Clothing).DefaultIfEmpty(0f).Max();
        c.ArmL.Clothing = x => x < end ? sleeve : 0f;
        return rings;
    }

    /// <summary>Sets up the trouser legs and boots and returns the rings of the leg, top down.</summary>
    private static List<LimbRing> ConfigureLegs(BuildContext c)
    {
        var paint = c.Paint;
        var knee = paint.KneeZ;
        var rings = new List<LimbRing>();
        Func<float, float> clothing;
        float[] zs;

        switch (c.Spec.Legs)
        {
            case LegsStyle.Cargo:
                // Bloused into the boots.
                paint.BootTopZ = paint.TrouserEndZ = 22f;
                clothing = z => z > 22f ? 1.0f + 0.9f * MathF.Exp(-MathF.Pow((z - 26.5f) / 4f, 2)) : 0.7f;
                zs = [74f, 64f, knee + 4f, knee, knee - 4f, 36f, 27f, 22f, 12f, 7f];
                break;
            case LegsStyle.TallBoots:
                paint.BootTopZ = paint.TrouserEndZ = 40f;
                clothing = z => z > 40f ? 0.8f + 0.6f * MathF.Exp(-MathF.Pow((z - 43f) / 3f, 2)) : 0.8f;
                zs = [74f, 64f, knee + 4f, knee, knee - 4f, 43f, 40f, 30f, 22f, 12f, 7f];
                break;
            case LegsStyle.Shorts:
                paint.TrouserEndZ = 58f;
                paint.BootTopZ = 16f;
                paint.SockTopZ = 21f;
                clothing = z => z >= 58f ? 1.3f : z <= 16f ? 0.6f : z <= 21f ? 0.3f : 0f;
                zs = [74f, 64f, 58f, knee + 4f, knee, knee - 4f, 36f, 22f, 16f, 12f, 7f];
                break;
            case LegsStyle.Thobe:
                paint.TrouserEndZ = 200f;   // under the robe: no trouser legs
                paint.BootTopZ = 16f;
                paint.SkirtWorn = true;
                clothing = z => z <= 16f ? 0.6f : 0f;
                zs = [74f, 64f, knee + 4f, knee, knee - 4f, 36f, 22f, 16f, 12f, 7f];
                break;
            case LegsStyle.Skirt:
                paint.TrouserEndZ = 200f;   // no trouser legs at all
                paint.BootTopZ = 16f;
                paint.SkirtWorn = true;
                clothing = z => z <= 16f ? 0.6f : 0f;
                zs = [74f, 64f, knee + 4f, knee, knee - 4f, 36f, 22f, 16f, 12f, 7f];
                break;
            default:
                // Straight legs that fall over the boots.
                paint.TrouserEndZ = 11.5f;
                paint.BootTopZ = 11.5f;
                clothing = z => z >= 11.5f ? 0.9f + 1.15f * SmoothStep((48f - z) / 26f) : 0.5f;
                zs = [74f, 64f, knee + 4f, knee, knee - 4f, 36f, 22f, 11.5f, 7f];
                break;
        }

        c.LegL.Clothing = clothing;
        foreach (var z in zs)
        {
            rings.Add(new LimbRing(z, clothing(z)));
            // A hem: the garment stops here, so add the ring of what is underneath it.
            var below = clothing(z - 0.05f);
            if (MathF.Abs(below - clothing(z)) > 0.25f) rings.Add(new LimbRing(z, below));
        }
        return rings;
    }

    // ---- torso ---------------------------------------------------------------------------

    /// <summary>
    /// Left half of the hips, torso and neck as a grid of vertex indices (rows bottom-up; columns from
    /// the front centre line round to the back centre line). The last row is the top of the neck.
    /// </summary>
    private static int[][] BuildTorsoHalf(BuildContext c, MeshBuilder m)
    {
        var weights = TorsoWeights(c);
        var zs = c.Torso.Sections.Select(s => s.Z).ToArray();
        var jacket = c.Spec.Torso is TorsoStyle.Jacket or TorsoStyle.Hoodie;
        if (jacket) c.Paint.HemZ = 93f;

        // The eight torso vertices around the shoulder become the first ring of the arm.
        var hole = c.ArmL.RingAt(16.5f, c.ArmL.Clothing(22.5f)).Points(LimbSides, 0);
        var holeCells = new (int Row, int Column)[]
        {
            (ArmpitRow + 1, 4), (ArmpitRow + 2, 4), (ArmpitRow + 2, 3), (ArmpitRow + 2, 2),
            (ArmpitRow + 1, 2), (ArmpitRow, 2), (ArmpitRow, 3), (ArmpitRow, 4),
        };
        hole[2].X += 1.0f;   // shoulder top sits further out than the armpit
        hole[6].X -= 0.6f;

        var grid = new int[zs.Length + 1][];
        for (var r = 0; r < zs.Length; r++)
        {
            grid[r] = new int[HalfColumns];
            // A jacket hangs loose over the hips.
            var loose = jacket ? Lerp(1.0f, 0.4f, Saturate((zs[r] - 96f) / 20f)) : 0f;
            for (var j = 0; j < HalfColumns; j++)
            {
                var p = c.Torso.Surface(zs[r], Deg(270f + 30f * j), jacket && zs[r] > 92f ? loose : 0f);
                var cell = Array.IndexOf(holeCells, (r, j));
                if (cell >= 0) p = hole[cell];
                // The vertex in the middle of the arm hole is never used; it reuses its neighbour.
                grid[r][j] = r == ArmpitRow + 1 && j == 3 ? grid[r][2] : m.AddVertex(p, weights);
            }
        }

        // Top of the neck: a tilted ring from the throat under the chin up to the nape.
        var neckFront = -c.HeadCenter.Y - 0.6f * c.HeadScale;
        var throatZ = c.ChinZ - 0.3f * c.HeadScale;
        var napeZ = c.HeadZ(0.20f);
        grid[zs.Length] = new int[HalfColumns];
        for (var j = 0; j < HalfColumns; j++)
        {
            var (sin, cos) = MathF.SinCos(Deg(30f * j));
            var p = P(c.NeckRadius * sin, neckFront + c.NeckRadius * cos, Lerp(throatZ, napeZ, (1f - cos) / 2f));
            grid[zs.Length][j] = m.AddVertex(p, c.Spine);
        }

        var slots = Enumerable.Repeat(Slot.Body, zs.Length - 1).Append(Slot.Neck).ToArray();
        m.AddGridFacesOutward(grid, false, slots, new Vector3(0, c.Torso.At(120f).CenterY, 120f),
            (r, col) => (r == ArmpitRow || r == ArmpitRow + 1) && (col == 2 || col == 3));
        return grid;
    }

    // ---- arms and hands ------------------------------------------------------------------

    private static void BuildArm(BuildContext c, MeshBuilder m, int[][] torso, List<LimbRing> armRings)
    {
        var weights = ArmWeights(c);
        var k = c.ArmScale;
        var w = c.WristX;

        var rows = new List<int[]>
        {
            // The arm hole, in the order of the arm's ring vertices: back, back-top, top, ... round to back-bottom.
            new[]
            {
                torso[ArmpitRow + 1][4], torso[ArmpitRow + 2][4], torso[ArmpitRow + 2][3], torso[ArmpitRow + 2][2],
                torso[ArmpitRow + 1][2], torso[ArmpitRow][2], torso[ArmpitRow][3], torso[ArmpitRow][4],
            },
        };
        var slots = new List<Slot>();
        foreach (var ring in armRings)
        {
            rows.Add(Array.ConvertAll(c.ArmL.RingAt(ring.T, ring.Clothing).Points(LimbSides, 0), p => m.AddVertex(p, weights)));
            slots.Add(Slot.Arm);
        }

        // Hand: a flat palm and finger block; the fingers themselves are painted on. Toon characters
        // get the pack's closed block fist instead.
        var toon = c.Spec.Rig == RigTarget.ToonSoldiers;
        Ring HandRing(float dx, float halfWidth, float halfThickness, float drop, float exp = 3f) =>
            new(new Vector3(w + dx * k, c.ArmY - 0.3f * k, c.ArmZ - drop * k), Back, Up, halfWidth * k, halfThickness * k, exp);
        Ring[] hand = toon
            ? [HandRing(2.5f, 4.2f, 3.4f, 0f, 3.2f), HandRing(5.5f, 5.4f, 5.0f, 0.3f, 3.6f), HandRing(10f, 5.6f, 5.2f, 0.5f, 3.6f), HandRing(13.2f, 4.8f, 4.4f, 0.6f, 3.2f)]
            : [HandRing(4.5f, 4.4f, 1.7f, 0.2f), HandRing(9.5f, 4.5f, 1.45f, 0.4f), HandRing(14.5f, 4.1f, 1.15f, 1.0f), HandRing(18.2f, 3.1f, 0.85f, 1.8f)];
        foreach (var ring in hand)
        {
            rows.Add(Array.ConvertAll(ring.Points(LimbSides, 0), p => m.AddVertex(p, weights)));
            slots.Add(Slot.Hand);
        }

        m.AddGridFacesOutward(rows.ToArray(), true, slots, new Vector3(c.ElbowX, c.ArmY, c.ArmZ));
        m.Density = 1.3f;
        var tip = m.AddVertex(hand[^1].Center + Left * 0.9f * k, weights);
        m.AddFan(tip, rows[^1], true, Slot.Hand, FanFacesAway(m, rows[^1], tip, hand[^2].Center));

        if (toon)
        {
            m.Density = 1f;
            return;
        }

        // Thumb.
        var hand0 = c.Skin.Rigid("hand_l");
        var thumb = new List<Vector3>
        {
            new(w + 2.4f * k, c.ArmY - 3.4f * k, c.ArmZ - 0.5f * k),
            new(w + 5.6f * k, c.ArmY - 6.2f * k, c.ArmZ - 0.9f * k),
            new(w + 8.8f * k, c.ArmY - 7.4f * k, c.ArmZ - 1.5f * k),
        };
        float[] radius = [1.4f, 1.25f, 0.95f];
        m.AddPathLoft(thumb, i => (radius[i] * k, radius[i] * k * 0.85f), Up, 5, 0, Slot.Hand, hand0, 2.4f, Cap.None, Cap.Flat, 0.7f * k);
        m.Density = 1f;
    }

    /// <summary>Whether a fan (centre, ring[i], ring[i+1]) has to be reversed to face away from a point.</summary>
    private static bool FanFacesAway(MeshBuilder m, int[] ring, int center, Vector3 inside)
    {
        var a = m.Positions[center];
        var normal = Vector3.Zero;
        for (var i = 0; i < ring.Length; i++)
            normal += Vector3.Cross(m.Positions[ring[i]] - a, m.Positions[ring[(i + 1) % ring.Length]] - a);
        return Vector3.Dot(normal, a - inside) < 0;
    }

    // ---- legs and feet -------------------------------------------------------------------

    private static void BuildLeg(BuildContext c, MeshBuilder m, int[] hipRow, List<LimbRing> legRings)
    {
        var weights = LegWeights(c);
        var hips = c.Torso.Sections[0];
        // The lowest point of the crotch, shared by both legs.
        var crotch = m.AddVertex(new Vector3(0, hips.CenterY + 0.5f, hips.Z - 3.2f), c.Spine);

        // The hip ring's left half plus the crotch point is the top of the leg. Leg ring vertices run
        // front-inner, front, front-outer, outer, back-outer, back, back-inner, inner.
        var rows = new List<int[]> { hipRow.Append(crotch).ToArray() };
        foreach (var ring in legRings)
            rows.Add(Array.ConvertAll(c.LegL.RingAt(ring.T, ring.Clothing).Points(LimbSides, Deg(225f)), p => m.AddVertex(p, weights)));
        m.AddGridFacesOutward(rows.ToArray(), true, Slot.Leg, c.LegL.Center(c.Paint.KneeZ));
    }

    private static void BuildFoot(BuildContext c, MeshBuilder m)
    {
        var ankle = c.Skeleton["foot_l"].Position;
        var ball = c.Skeleton["ball_l"].Position;
        var weights = c.Skin.Chain(p => -p.Y, 2.5f, ("foot_l", float.NegativeInfinity), ("ball_l", -ball.Y));
        var k = c.LegScale;
        var ankleFront = -ankle.Y;
        var ballFront = -ball.Y;
        Ring FootRing(float front, float halfWidth, float top)
        {
            var t = (front - ankleFront) / (ballFront - ankleFront);
            return new Ring(P(Lerp(ankle.X, ball.X, t), front, top / 2), Left, Up, halfWidth * k, top / 2, 3.4f);
        }
        m.AddLoft(
            [
                FootRing(-8.6f, 3.7f, 7.0f), FootRing(-6.5f, 4.9f, 10.2f), FootRing(-0.5f, 5.3f, 11.4f),
                FootRing(5f, 5.4f, 8.6f), FootRing(11f, 5.6f, 6.6f), FootRing(17f, 5.4f, 5.4f), FootRing(20.8f, 4.3f, 4.0f),
            ],
            LimbSides, MathF.PI / LimbSides, Slot.Foot, weights, Cap.Flat, Cap.Flat, 0.6f, 1.1f);
    }

    private static void BuildPads(BuildContext c, MeshBuilder m)
    {
        m.Detail = PaintDetail.Panel;
        if (c.Spec.KneePads)
        {
            var z = c.Paint.KneeZ;
            var centre = c.LegL.Center(z);
            var k = c.LegScale;
            m.AddBox(new Vector3(centre.X, centre.Y - c.LegL.RadiusV(z).Neg - 0.4f, z), new Vector3(4.6f * k, 5.2f * k, 1.2f),
                Slot.GearTrim, Skinning.RigidAt(LegWeights(c), new Vector3(0, 0, z - 1.5f)),
                Basis(Left, Up, Front), new Vector2(0.75f, 0.75f));
        }
        if (c.Spec.ElbowPads)
        {
            var k = c.ArmScale;
            m.AddBox(new Vector3(c.ElbowX, c.ArmY + c.ArmL.RadiusU(c.ElbowX) + 0.4f, c.ArmZ), new Vector3(4.2f * k, 4.4f * k, 1.1f),
                Slot.GearTrim, Skinning.RigidAt(ArmWeights(c), new Vector3(c.ElbowX + 1.5f, 0, 0)),
                Basis(Up, Left, Back), new Vector2(0.75f, 0.75f));
        }
        m.Detail = PaintDetail.None;
    }

    // ---- garments that are their own shell -----------------------------------------------

    private static void BuildSkirt(BuildContext c)
    {
        // From the waist to above the knee; the hem follows the thighs.
        var thighL = c.Skin.Rigid("thigh_l");
        var thighR = c.Skin.Rigid("thigh_r");
        // Each side follows its own thigh; across the middle the two thighs share the cloth.
        var thighs = Skinning.Blend(thighR, thighL, p => 0.5f + p.X / 8f);
        var weights = Skinning.Blend(c.Spine, thighs, p => 0.9f * SmoothStep((97f - p.Z) / 16f));
        var widest = c.Torso.At(89f);
        Ring Hem(float z, float grow) => new(new Vector3(0, widest.CenterY, z), Left, Back,
            widest.HalfWidth + grow, widest.Back + grow, widest.Front + grow, 2.3f);
        c.Mesh.AddLoft([Hem(60f, 3.0f), Hem(76f, 2.0f), c.Torso.RingAt(89f, 0.8f), c.Torso.RingAt(96f, 0.7f), c.Torso.RingAt(102.5f, 0.6f)],
            12, 0, Slot.Trousers, weights, Cap.Flat, Cap.None);
    }

    /// <summary>Ankle-length robe (thobe) from the waist down, in the shirt's cloth, flaring at the hem.</summary>
    private static void BuildThobe(BuildContext c)
    {
        var thighL = c.Skin.Rigid("thigh_l");
        var thighR = c.Skin.Rigid("thigh_r");
        var thighs = Skinning.Blend(thighR, thighL, p => 0.5f + p.X / 10f);
        var weights = Skinning.Blend(c.Spine, thighs, p => 0.9f * SmoothStep((97f - p.Z) / 16f));
        var widest = c.Torso.At(89f);
        Ring Hem(float z, float grow) => new(new Vector3(0, widest.CenterY, z), Left, Back,
            widest.HalfWidth + grow, widest.Back + grow, widest.Front + grow, 2.3f);
        c.Mesh.AddLoft([Hem(15f, 6.5f), Hem(35f, 5.0f), Hem(60f, 3.2f), Hem(76f, 2.0f), c.Torso.RingAt(89f, 0.9f), c.Torso.RingAt(96f, 0.8f), c.Torso.RingAt(102.5f, 0.7f)],
            12, 0, Slot.Shirt, weights, Cap.Flat, Cap.None);
    }

    private static void BuildScarf(BuildContext c)
    {
        var mesh = c.Mesh;
        var n = c.NeckRadius;
        Ring ScarfRing(float z, float r) => new(P(0, 0.4f, z), Left, Back, r, r, 2.2f);
        var top = c.ChinZ + 0.6f;
        mesh.AddLoft([ScarfRing(150.5f, n + 2.2f), ScarfRing(152.5f, n + 3.8f), ScarfRing(top - 1.5f, n + 3.6f), ScarfRing(top, n + 1.4f)],
            BuildContext.Sides, BuildContext.FlatFront, Slot.Scarf, c.Spine, Cap.Flat, Cap.Flat);
        // Loose end hanging on the chest.
        const float x = 3.8f;
        var tail = new List<Vector3> { P(x, 0.4f + n + 3.0f, 152.5f) };
        foreach (var z in new[] { 147f, 142f, 136f })
            tail.Add(new Vector3(x, c.Torso.FrontY(z, x) - 0.7f, z));
        const float root2 = 1.41421356f;
        mesh.AddPathLoft(tail, i => ((i == 3 ? 2.0f : 2.5f) * root2, 0.55f * root2), Left, 4, MathF.PI / 4, Slot.Scarf, c.Spine);
    }
}

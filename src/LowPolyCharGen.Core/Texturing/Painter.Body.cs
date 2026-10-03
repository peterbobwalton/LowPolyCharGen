using System.Numerics;
using static LowPolyCharGen.Geo;

namespace LowPolyCharGen.Texturing;

/// <summary>Torso, arms, hands, legs and feet: skin and the clothes painted over it.</summary>
internal sealed partial class Painter
{
    private bool BeltWorn => _spec.Belt != BeltStyle.None && _c.HemZ >= _c.BeltBottomZ;

    /// <summary>Distance from a point to a line segment, in the plane.</summary>
    private static float Segment(float x, float y, float x0, float y0, float x1, float y1)
    {
        float dx = x1 - x0, dy = y1 - y0;
        var k = Saturate(((x - x0) * dx + (y - y0) * dy) / (dx * dx + dy * dy));
        var ex = x - (x0 + k * dx);
        var ey = y - (y0 + k * dy);
        return MathF.Sqrt(ex * ex + ey * ey);
    }

    /// <summary>Signed distance to the outline of an axis-aligned rectangle (negative inside).</summary>
    private static float Box(float x, float y, float halfWidth, float halfHeight) =>
        MathF.Max(MathF.Abs(x) - halfWidth, MathF.Abs(y) - halfHeight);

    // ---- torso and hips ------------------------------------------------------------------

    private Vector3 Body(in Texel t)
    {
        var z = t.P.Z;
        if (BeltWorn && z >= _c.BeltBottomZ && z <= _c.BeltTopZ) return Belt(t);
        return z >= _c.HemZ ? Upper(t) : Lower(t);
    }

    private Vector3 Belt(in Texel t)
    {
        var p = t.P;
        var ax = MathF.Abs(p.X);
        var front = t.N.Y < -0.3f;
        var v = (p.Z - _c.BeltBottomZ) / (_c.BeltTopZ - _c.BeltBottomZ);   // 0..1 up the belt

        var color = Leather(_accent * 0.8f, p);
        color *= 1f - 0.25f * (Line(v - 0.12f, 0.03f, 0.02f) + Line(v - 0.88f, 0.03f, 0.02f));   // stitched edges
        color *= 0.9f + 0.1f * MathF.Sin(v * MathF.PI);                                          // rounded
        // Belt loops in the trouser cloth.
        var around = MathF.Atan2(p.X, -(p.Y - _c.NeckCenter.Y)) * 180f / MathF.PI;               // 0 at the front
        if (Line(Fract((around + 22f) / 45f) - 0.5f, 0.045f, 0.01f) > 0.5f && ax > 3f)
            color = Cloth(_trousers * 0.82f, p, CamoBottom);
        if (front)
        {
            // Buckle: a steel frame with the belt showing through.
            var frame = Box(p.X, v - 0.5f, 2.1f, 0.62f);
            var inner = Box(p.X, v - 0.5f, 1.45f, 0.36f);
            if (frame < 0f) color = inner < 0f ? color * 0.75f : Steel * (0.75f + 0.3f * v);
            color = Mix(color, Steel * 0.9f, Line(p.X - 0.2f, 0.16f, 0.04f) * (frame < 0f ? 1f : 0f));   // prong
        }
        return color;
    }

    private Vector3 Upper(in Texel t)
    {
        var p = t.P;
        var z = p.Z;
        var ax = MathF.Abs(p.X);
        var front = t.N.Y < -0.2f;
        var back = t.N.Y > 0.2f;
        var style = _spec.Torso;

        var cloth = style == TorsoStyle.CheckShirt ? Cloth(Plaid(_shirt, p), p) : Cloth(_shirt, p, CamoTop);
        // Creases where the shirt gathers at the waist.
        cloth *= 1f + 0.06f * MathF.Sin(z * 1.6f + 7f * Noise.Fbm(p * 0.18f)) * (1f - SmoothStep(104f, 118f, z));
        var trim = _shirt * 0.74f;

        // Distance from the neck axis, for necklines.
        var neck = new Vector2(p.X, p.Y - _c.NeckCenter.Y).Length();
        var neckBase = _c.NeckRadius + 1.9f;

        switch (style)
        {
            case TorsoStyle.TankTop:
            {
                // Scooped neck and cut-away shoulders: cloth only below the scoop and on the straps.
                const float strapInner = 5.2f, strapBase = 141.5f;
                var scoop = strapBase - (front ? 5.5f : 2.5f) * MathF.Sqrt(MathF.Max(0f, 1f - ax * ax / (strapInner * strapInner)));
                var armhole = 10.8f + 1.3f * MathF.Max(0f, strapBase - z);      // outer edge of the cloth
                var covered = ax <= armhole && (ax >= strapInner || z <= scoop);
                if (!covered) return SkinTone(p);
                var rim = MathF.Min(armhole - ax, ax < strapInner ? scoop - z : z > strapBase ? ax - strapInner : 99f);
                return Mix(cloth, trim, 1f - SmoothStep(0.55f, 0.75f, rim));
            }
            case TorsoStyle.Polo:
            {
                // Collar (painted on the neck) and a short buttoned placket.
                if (front && z > 140.5f && ax < 1.4f)
                {
                    cloth = Cloth(_shirt * 1.05f, p) * (1f - 0.3f * Line(ax - 1.4f, 0.07f));
                    foreach (var bz in (ReadOnlySpan<float>)[143.2f, 146.6f])
                        cloth = Mix(cloth, new Vector3(0.88f, 0.86f, 0.8f), Disc(p.X, z - bz, 0.38f));
                }
                if (front && ax < 1.4f) cloth *= 1f - 0.3f * Line(z - 140.5f, 0.06f);
                if (z > 150.5f) cloth = Cloth(_shirt * 0.92f, p);
                break;
            }
            case TorsoStyle.Hoodie:
            {
                if (z > 149.5f) cloth = Cloth(trim, p) * (1f + 0.08f * MathF.Sin(MathF.Atan2(p.X, p.Y) * 9f));   // hood bunched round the neck
                if (z < _c.HemZ + 3.6f)
                    cloth = Cloth(trim, p) * (1f + 0.09f * MathF.Sin(MathF.Atan2(p.X, p.Y) * 46f));   // ribbed hem
                if (front)
                {
                    // Kangaroo pocket with slanted openings, and the hood's drawstrings.
                    var pocketTop = 117f - 0.25f * ax;
                    if (z > 104f && z < pocketTop && ax < 9.5f - 0.35f * (z - 104f)) cloth *= 0.95f;
                    if (ax < 6.4f) cloth *= 1f - 0.32f * Line(z - pocketTop, 0.08f);
                    cloth *= 1f - 0.4f * Line(Segment(ax, z, 6.2f, 116.5f, 9.6f, 105f), 0.12f);
                    if (ax < 9.5f) cloth *= 1f - 0.25f * Line(z - 104f, 0.06f);
                    var cord = Line(ax - 2.1f - 0.04f * (149f - z), 0.18f, 0.06f) * Band(z, 136.5f, 149.5f, 0.2f);
                    cloth = Mix(cloth, new Vector3(0.86f, 0.84f, 0.78f), cord);
                    cloth = Mix(cloth, Steel * 0.5f, Disc(ax - 2.6f, z - 136.4f, 0.4f));   // aglets
                }
                break;
            }
            case TorsoStyle.TShirt:
            {
                // Crew neck, a little lower at the front.
                var opening = neckBase + (front ? 1.3f : 0f);
                if (z > 148f && neck < opening) return SkinTone(p);
                if (z > 147f && neck < opening + 1.1f) return Cloth(trim, p) * (1f + 0.08f * MathF.Sin(neck * 14f));
                break;
            }
            case TorsoStyle.LongSleeve or TorsoStyle.RolledSleeves or TorsoStyle.CheckShirt:
            {
                // Open collar: a V of skin, collar wings either side, a button placket and chest pockets.
                var v = (z - 144.5f) * 0.42f;
                if (front && z > 144.5f && ax < v) return SkinTone(p) * 0.94f;
                if (front && z > 146.5f && ax < v + 3.0f)
                    return cloth * 1.07f * (1f - 0.35f * Line(ax - (v + 3.0f), 0.1f));
                if (z > 151f) return cloth * 0.9f;
                if (front)
                {
                    if (ax < 1.25f) cloth *= 1.04f;
                    cloth *= 1f - 0.25f * Line(ax - 1.25f, 0.05f);
                    var button = Fract((z - 104.5f) / 6.4f) * 6.4f - 3.2f;
                    if (z > 102f && z < 144f) cloth = Mix(cloth, _shirt * 0.4f, Disc(p.X, button, 0.4f));

                    var pocket = Box(ax - 7.4f, z - 131f, 3.7f, 4.5f);
                    cloth *= 1f - 0.28f * Line(pocket, 0.07f);
                    if (pocket < 0f)
                    {
                        cloth *= 1f - 0.3f * Line(z - 133.2f, 0.07f);              // flap
                        cloth *= 1f - 0.13f * Line(ax - 7.4f, 0.05f) * (z < 133.2f ? 1f : 0f);   // pleat
                        cloth = Mix(cloth, _shirt * 0.4f, Disc(ax - 7.4f, z - 134.1f, 0.33f));
                    }
                }
                break;
            }
            case TorsoStyle.Jacket:
            {
                if (z > 150f) cloth = Cloth(trim, p);
                // Ribbed hem.
                if (z < _c.HemZ + 3.2f)
                    cloth = Cloth(trim, p) * (1f + 0.09f * MathF.Sin(MathF.Atan2(p.X, p.Y) * 46f));
                if (front)
                {
                    // Zip with its tape either side, and two slanted pockets.
                    cloth *= 1f - 0.2f * Line(ax - 1.2f, 0.05f);
                    if (ax < 0.32f) cloth = Steel * (Fract(z / 0.45f) < 0.5f ? 0.55f : 0.36f);
                    cloth *= 1f - 0.4f * Line(Segment(ax, z, 6.5f, 111f, 11.5f, 103f), 0.14f);
                }
                break;
            }
        }
        if (back) cloth *= 1f - 0.05f * Line(p.X, 0.05f);   // centre back seam
        return cloth;
    }

    private Vector3 Lower(in Texel t)
    {
        var p = t.P;
        var z = p.Z;
        var ax = MathF.Abs(p.X);
        var front = t.N.Y < -0.2f;
        var back = t.N.Y > 0.2f;
        var top = _c.HemZ;

        if (_c.SkirtWorn) return Cloth(_trousers * 0.6f, p);
        var cloth = Cloth(_trousers, p, CamoBottom);

        if (!BeltWorn && z > top - 3.2f)
        {
            // Waistband with its button.
            cloth *= 0.86f;
            cloth *= 1f - 0.25f * Line(z - (top - 3.2f), 0.06f);
            if (front) cloth = Mix(cloth, Steel * 0.6f, Disc(p.X, z - (top - 1.6f), 0.5f));
        }
        if (front)
        {
            cloth *= 1f - 0.30f * Line(p.X, 0.06f);                                              // fly
            cloth *= 1f - 0.16f * Line(p.X - 2.4f, 0.05f) * Band(z, 90f, top - 3.2f, 0.4f);      // fly stitching
            cloth *= 1f - 0.34f * Line(Segment(ax, z, 11.5f, top - 3.2f, 16.2f, 91f), 0.09f);    // slant pockets
        }
        if (back)
        {
            cloth *= 1f - 0.16f * Line(p.X, 0.05f);                                              // seat seam
            var pocket = Box(ax - 8.6f, z - 91.5f, 3.6f, 3.9f);
            cloth *= 1f - 0.24f * Line(pocket, 0.07f);
            if (pocket < 0f) cloth *= 1f - 0.24f * Line(z - 93.6f, 0.06f);                       // flap
        }
        return cloth;
    }

    // ---- arms and hands ------------------------------------------------------------------

    private Vector3 GloveColor(Vector3 p) => Cloth(_gear * 0.5f, p);

    private Vector3 Arm(in Texel t)
    {
        var p = t.P;
        var x = MathF.Abs(p.X);
        var end = _c.SleeveEndX;

        if (_spec.Gloves && x > _c.WristX - 5.5f)
        {
            // Gauntlet cuff with a wrist strap.
            var glove = GloveColor(p);
            glove *= 1f - 0.3f * Line(x - (_c.WristX - 5.5f), 0.12f);
            return Mix(glove, _gear * 0.3f, Band(x, _c.WristX - 2.6f, _c.WristX - 1.4f));
        }

        if (x >= end)
        {
            var skin = SkinTone(p);
            skin *= 1f - 0.07f * Blob((x - _c.ElbowX) / 3.2f, 0f);          // elbow
            // The end of a sleeve throws a little shadow, and its opening is dark inside.
            if (end > 0f) skin *= 1f - 0.25f * (1f - SmoothStep(end, end + 1.3f, x));
            return skin;
        }

        var cloth = _spec.Torso == TorsoStyle.CheckShirt ? Cloth(Plaid(_shirt, p), p) : Cloth(_shirt, p, CamoTop);
        cloth *= 1f + 0.07f * MathF.Sin(x * 2.3f + 6f * Noise.Fbm(p * 0.2f)) * MathF.Exp(-MathF.Pow((x - _c.ElbowX) / 6f, 2));   // elbow creases
        cloth *= 1f - 0.14f * Line(x - 18.6f, 0.06f);                         // shoulder seam

        switch (_spec.Torso)
        {
            case TorsoStyle.TShirt or TorsoStyle.Polo:
                cloth *= 1f - 0.2f * Line(x - (end - 1.3f), 0.05f);           // hem stitch
                break;
            case TorsoStyle.RolledSleeves:
                if (x > end - 3.7f)
                {
                    // The roll shows the paler inside of the cloth.
                    cloth = Cloth(_shirt * 1.16f, p);
                    cloth *= 1f - 0.22f * Line(Fract((x - end) / 1.25f) - 0.5f, 0.06f, 0.03f);
                }
                break;
            case TorsoStyle.LongSleeve or TorsoStyle.CheckShirt:
                if (x > end - 3.2f)
                {
                    cloth = Cloth(_shirt * 0.84f, p, CamoTop);
                    cloth *= 1f - 0.28f * Line(x - (end - 3.2f), 0.06f);
                    if (t.N.Z > 0.5f) cloth = Mix(cloth, _shirt * 0.4f, Disc(x - (end - 1.6f), p.Y - _c.ArmY, 0.36f));
                }
                break;
            case TorsoStyle.Jacket or TorsoStyle.Hoodie:
                if (x > end - 3.4f)
                    cloth = Cloth(_shirt * 0.74f, p) * (1f + 0.09f * MathF.Sin(MathF.Atan2(p.Y - _c.ArmY, p.Z - _c.ArmZ) * 30f));
                break;
        }
        return cloth;
    }

    private Vector3 Hand(in Texel t)
    {
        var p = t.P;
        var k = _c.ArmScale;
        var along = (MathF.Abs(p.X) - _c.WristX) / k;        // from the wrist to the fingertips
        var across = (p.Y - (_c.ArmY - 0.3f * k)) / k;       // negative towards the thumb
        var onTop = t.N.Z > 0.35f;
        var underneath = t.N.Z < -0.35f;
        var gloved = _spec.Gloves;

        var color = gloved ? GloveColor(p) : SkinTone(p);
        if (!gloved && underneath) color *= new Vector3(1.05f, 0.97f, 0.95f);   // palm

        if (MathF.Abs(across) < 4.7f && (onTop || underneath))
        {
            // Gaps between the four fingers, knuckles and nails.
            var gap = 0f;
            foreach (var g in (ReadOnlySpan<float>)[-2.25f, 0.0f, 2.2f])
                gap = MathF.Max(gap, Line(across - g, 0.11f, 0.06f));
            gap *= SmoothStep(8.6f, 9.6f, along);
            color = Mix(color, color * 0.38f, gap);

            if (onTop)
            {
                var finger = MathF.Abs(Fract((across + 4.5f) / 2.25f) - 0.5f) * 2.25f;   // distance from a finger's centre line
                color *= 1f - 0.16f * Blob(finger / 0.8f, (along - 9.4f) / 0.8f);        // knuckles
                color *= 1f - 0.10f * Line(along - 13.6f, 0.08f, 0.06f) * (finger < 0.85f ? 1f : 0f);
                if (!gloved && along > 16.2f && along < 17.8f && finger < 0.6f)
                    color = Mix(color, _skin * new Vector3(1.08f, 0.96f, 0.94f) + new Vector3(0.05f), 0.6f);   // nails
                if (gloved) color = Mix(color, _gear * 0.34f, Band(along, 6.4f, 9.0f, 0.2f) * (MathF.Abs(across) < 4f ? 1f : 0f));   // knuckle guard
            }
            else
            {
                color *= 1f - 0.12f * Line(along - 5.2f + 0.25f * across, 0.08f, 0.07f);   // palm crease
            }
        }
        return color;
    }

    // ---- legs and feet -------------------------------------------------------------------

    private Vector3 Leg(in Texel t)
    {
        var p = t.P;
        var z = p.Z;
        // Position round the leg: across the leg (positive = outside) and to the front.
        var centre = _c.LegCenter(z);
        var dx = MathF.Abs(p.X) - centre.X;
        var dy = p.Y - centre.Y;
        var front = dy < 0f;

        // The underside of a hem looks into the dark inside of the garment.
        if (t.N.Z < -0.75f && (MathF.Abs(z - _c.TrouserEndZ) < 0.4f)) return Ink;

        if (z >= _c.TrouserEndZ)
        {
            var jeans = _spec.Legs == LegsStyle.Jeans;
            var wear = Blob(dx / 5f, (z - 70f) / 14f) * (front ? 1f : 0.3f) + 0.7f * Blob(dx / 4f, (z - _c.KneeZ) / 5f) * (front ? 1f : 0f);
            var cloth = jeans ? Cloth(Denim(_trousers, p, wear), p) : Cloth(_trousers, p, CamoBottom);
            cloth *= 1f - 0.18f * Line(dy, 0.06f) * (dx > 0f ? 1f : 0.6f);    // side seams, outside and inside
            if (jeans)
            {
                var stitch = new Vector3(0.78f, 0.55f, 0.25f);
                if (dx > 0f) cloth = Mix(cloth, stitch, 0.7f * Line(dy - 0.35f, 0.05f, 0.03f));                 // outseam topstitch
                cloth = Mix(cloth, stitch, 0.7f * Line(z - (_c.TrouserEndZ + 1.4f), 0.05f, 0.03f));             // hem
                if (front)
                {
                    cloth = Mix(cloth, stitch, 0.75f * Line(Segment(dx, z, -2.0f, 92f, 4.5f, 85f), 0.06f, 0.03f));   // front pocket
                }
                else
                {
                    var pocket = Box(dx + 0.5f, z - 84f, 3.6f, 3.8f);
                    cloth *= 1f - 0.2f * Line(pocket, 0.06f);
                    if (z < 87.8f) cloth = Mix(cloth, stitch, 0.7f * Line(pocket + 0.35f, 0.05f, 0.03f));         // back pocket
                }
            }
            // Creases behind and around the knee.
            cloth *= 1f + 0.07f * MathF.Sin(z * 1.5f + 6f * Noise.Fbm(p * 0.2f)) * MathF.Exp(-MathF.Pow((z - _c.KneeZ) / 9f, 2));
            cloth *= 1f - 0.2f * Line(z - (_c.TrouserEndZ + 1.6f), 0.05f);     // hem stitch

            if (_spec.Legs == LegsStyle.Cargo)
            {
                if (dx > 0f)
                {
                    // Bellows pocket on the outside of the thigh.
                    var pocket = Box(dy, z - 68.5f, 5.2f, 6.4f);
                    cloth *= 1f - 0.34f * Line(pocket, 0.09f);
                    if (pocket < 0f)
                    {
                        cloth *= 1.05f;
                        cloth *= 1f - 0.34f * Line(z - 71.6f, 0.08f);
                        cloth *= 1f - 0.14f * Band(z, 70.4f, 71.5f, 0.3f);
                        cloth = Mix(cloth, _trousers * 0.45f, Disc(dy, z - 73.0f, 0.4f));
                    }
                }
                if (front)
                {
                    var knee = Box(dx, z - (_c.KneeZ - 0.5f), 4.6f, 7.2f);
                    cloth *= 1f - 0.2f * Line(knee, 0.06f) - 0.12f * Line(knee + 0.5f, 0.04f);   // knee patch, double stitched
                }
            }
            return cloth;
        }

        if (z <= _c.BootTopZ) return Boot(t, dx, dy, z, _c.BootTopZ);

        if (z <= _c.SockTopZ)
        {
            var sock = new Vector3(0.62f, 0.62f, 0.58f) * (1f + 0.08f * MathF.Sin(MathF.Atan2(dx, dy) * 34f));
            return sock * (1f - 0.2f * Line(z - _c.SockTopZ, 0.3f, 0.2f));
        }

        var skin = SkinTone(p);
        skin *= 1f - 0.06f * Blob(dx / 4f, (z - _c.KneeZ) / 4f) * (front ? 1f : 0f);            // knee cap
        skin *= 1f - 0.22f * (1f - SmoothStep(0f, 1.4f, _c.TrouserEndZ - z));                    // shadow of a shorts hem
        return skin;
    }

    /// <summary>Boot leather with a lacing panel up the front.</summary>
    /// <summary>The colour that stands out on a trainer: dark on a pale shoe, white on a dark one.</summary>
    private Vector3 TrainerContrast => Vector3.Dot(_boots, new Vector3(0.3f, 0.59f, 0.11f)) > 0.6f
        ? new Vector3(0.12f, 0.16f, 0.28f) : new Vector3(0.92f, 0.92f, 0.9f);

    private Vector3 Boot(in Texel t, float dx, float dy, float z, float top)
    {
        if (_spec.Footwear == Footwear.Trainers)
        {
            // High-top canvas: padded collar, white laces down the front.
            var shoe = Cloth(_boots, t.P);
            shoe = Mix(shoe, TrainerContrast, Band(z, top - 1.4f, top + 0.1f, 0.12f));
            if (dy < -1f && MathF.Abs(dx) < 2.2f)
                shoe = Mix(shoe * 0.9f, new Vector3(0.92f, 0.92f, 0.9f), Line(Fract(z / 1.7f) - 0.5f, 0.14f, 0.04f));
            return shoe;
        }
        var color = Leather(_boots, t.P);
        color *= 1f - 0.22f * Band(z, top - 1.7f, top + 0.1f, 0.15f);            // padded collar
        color *= 1f - 0.2f * Line(z - (top - 1.7f), 0.05f);
        if (dy < -1f && MathF.Abs(dx) < 2.9f)
        {
            color *= 0.84f;                                                       // tongue
            var lace = Line(Fract(z / 1.9f) - 0.5f, 0.11f, 0.04f) * (MathF.Abs(dx) < 2.5f ? 1f : 0f) * (z < top - 1.2f ? 1f : 0f);
            color = Mix(color, _boots * 1.5f + new Vector3(0.18f), lace);
        }
        color *= 1f - 0.25f * Line(MathF.Abs(dx) - 2.9f, 0.05f) * (dy < 0f ? 1f : 0f);   // edge of the lacing panel
        return color;
    }

    private Vector3 Foot(in Texel t)
    {
        var p = t.P;
        var z = p.Z;
        var ahead = -(p.Y - _c.Ankle.Y);                 // how far forward of the ankle
        var dx = MathF.Abs(p.X) - (_c.Ankle.X + 0.11f * ahead);

        if (_spec.Footwear == Footwear.Trainers)
        {
            // Rubber outsole, thick white midsole, then the upper with a side stripe and laces.
            if (z < 0.9f || t.N.Z < -0.6f) return new Vector3(0.28f, 0.28f, 0.3f);
            if (z < 3.2f) return new Vector3(0.93f, 0.93f, 0.91f) * (1f - 0.12f * Line(z - 2.0f, 0.06f));
            var shoe = Cloth(_boots, p);
            shoe *= 1f + 0.08f * SmoothStep(13f, 18f, ahead);                                           // toe box
            var stripe = Band(Fract((ahead * 0.55f + z) / 4.2f), 0.1f, 0.42f, 0.04f) * Band(ahead, -3f, 9f, 0.5f) * Band(z, 3.6f, 9f, 0.3f);
            if (MathF.Abs(t.N.X) > 0.4f) shoe = Mix(shoe, TrainerContrast, stripe);
            if (t.N.Z > 0.25f && ahead > -1.5f && ahead < 10.5f && MathF.Abs(dx) < 2.2f)
                shoe = Mix(shoe * 0.9f, new Vector3(0.92f, 0.92f, 0.9f), Line(Fract(ahead / 1.7f) - 0.5f, 0.14f, 0.04f));
            return shoe;
        }

        // Sole and welt.
        if (z < 1.6f || t.N.Z < -0.6f)
            return new Vector3(0.11f, 0.105f, 0.10f) * (1f + 0.25f * (Fract(ahead / 1.4f) < 0.5f ? 1f : 0f) * (z < 1.1f ? 1f : 0f));
        if (z < 2.1f) return Mix(_boots * 0.6f, new Vector3(0.3f, 0.26f, 0.2f), 0.5f);

        var color = Leather(_boots, p);
        color *= 1f - 0.3f * Line(ahead - 14.2f, 0.07f) * (z > 2.4f ? 1f : 0f);          // toe cap
        color *= 1f + 0.10f * SmoothStep(14.5f, 19f, ahead) * SmoothStep(0.2f, 0.8f, t.N.Z);   // polished toe
        color *= 1f - 0.22f * Line(ahead + 5.6f, 0.06f);                                  // heel counter
        if (t.N.Z > 0.25f && ahead > -1.5f && ahead < 10.5f && MathF.Abs(dx) < 2.8f)
        {
            // Lacing over the instep.
            color *= 0.84f;
            var lace = Line(Fract(ahead / 1.8f) - 0.5f, 0.11f, 0.04f) * (MathF.Abs(dx) < 2.4f ? 1f : 0f);
            color = Mix(color, _boots * 1.5f + new Vector3(0.18f), lace);
        }
        return color;
    }
}

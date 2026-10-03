using System.Numerics;
using static LowPolyCharGen.Geo;

namespace LowPolyCharGen.Texturing;

/// <summary>One texel of the atlas, described by where it lies on the character.</summary>
internal readonly struct Texel(Vector3 position, Vector3 normal, float occlusion, Slot slot, PaintDetail detail, Vector2 uv, Vector2 size)
{
    /// <summary>Bind-pose position, centimetres.</summary>
    public readonly Vector3 P = position;
    public readonly Vector3 N = normal;

    /// <summary>Ambient occlusion: 1 = open, 0 = enclosed.</summary>
    public readonly float Ao = occlusion;
    public readonly Slot Slot = slot;
    public readonly PaintDetail Detail = detail;

    /// <summary>Position inside the texel's UV chart and the chart's size, in centimetres.</summary>
    public readonly Vector2 Uv = uv;
    public readonly Vector2 Size = size;
}

/// <summary>
/// Paints the character procedurally: every texel's colour is computed from its position on the
/// model, so features line up across UV seams and follow the body whatever its shape.
/// Colours are sRGB in 0..1.
/// </summary>
internal sealed partial class Painter
{
    private readonly PaintContext _c;
    private readonly CharacterSpec _spec;
    private readonly Vector3 _skin, _hair, _eye, _shirt, _trousers, _boots, _gear, _hat, _accent, _lips;

    private static readonly Vector3 Ink = new(0.10f, 0.09f, 0.09f);
    private static readonly Vector3 Steel = new(0.62f, 0.63f, 0.66f);

    public Painter(PaintContext context)
    {
        _c = context;
        _spec = context.Spec;
        _skin = V(_spec.SkinColor);
        _hair = V(_spec.HairColor);
        _eye = V(_spec.EyeColor);
        _shirt = V(_spec.ShirtColor);
        _trousers = V(_spec.TrousersColor);
        _boots = V(_spec.BootsColor);
        _gear = V(_spec.GearColor);
        _hat = V(_spec.HatColor);
        _accent = V(_spec.AccentColor);
        _lips = context.Female
            ? Mix(_skin, new Vector3(0.74f, 0.27f, 0.31f), 0.66f)
            : Mix(_skin, new Vector3(0.58f, 0.32f, 0.29f), 0.42f);
    }

    public Vector3 Shade(in Texel t)
    {
        var color = t.Slot switch
        {
            Slot.Head => Head(t),
            Slot.Ear => Ear(t),
            Slot.Neck => Neck(t),
            Slot.Body => Body(t),
            Slot.Arm => Arm(t),
            Slot.Hand => Hand(t),
            Slot.Leg => Leg(t),
            Slot.Foot => Foot(t),
            _ => Detail(t, Material(t)),
        };
        if (_spec.Rig == RigTarget.ToonSoldiers) return Vector3.Clamp(ToonFinish(color, t), Vector3.Zero, Vector3.One);

        // Baked soft lighting: occlusion, and surfaces facing up a touch brighter than those facing down.
        var depth = t.Slot is Slot.Head or Slot.Ear or Slot.Neck or Slot.Hand ? 0.30f : 0.44f;
        color *= (1f - depth + depth * t.Ao) * (0.94f + 0.08f * t.N.Z);
        return Vector3.Clamp(color, Vector3.Zero, Vector3.One);
    }

    /// <summary>
    /// The Toon Soldiers pack's hand-painted look: deep baked occlusion, strong top light, broad
    /// brush-like value patches and slightly muted, warm colours.
    /// </summary>
    private Vector3 ToonFinish(Vector3 color, in Texel t)
    {
        var skinLike = t.Slot is Slot.Head or Slot.Ear or Slot.Neck or Slot.Hand;
        var depth = skinLike ? 0.45f : 0.62f;
        var ao = MathF.Pow(t.Ao, 1.3f);
        color *= (1f - depth + depth * ao) * (0.84f + 0.24f * t.N.Z);

        // Broad strokes: a low-frequency value break-up stretched along the height, like brush work.
        var stroke = Noise.Fbm(new Vector3(t.P.X * 0.09f, t.P.Y * 0.09f, t.P.Z * 0.045f) + new Vector3(17, 5, 3)) - 0.5f;
        color *= 1f + (skinLike ? 0.10f : 0.22f) * stroke;

        // Muted and warm: pull towards a warm grey of the same brightness.
        // Snow kit stays cool and clean instead.
        var luma = Vector3.Dot(color, new Vector3(0.299f, 0.587f, 0.114f));
        var snow = _spec.Edition == Edition.Snow && !skinLike;
        var warm = snow ? new Vector3(luma * 0.98f, luma, luma * 1.03f) : new Vector3(luma * 1.08f, luma, luma * 0.84f);
        return Mix(color, warm, skinLike ? 0.12f : snow ? 0.10f : 0.22f);
    }

    // ---- helpers -------------------------------------------------------------------------

    private static Vector3 V(Rgb c) => new(c.R / 255f, c.G / 255f, c.B / 255f);

    private static Vector3 Mix(Vector3 a, Vector3 b, float t) => a + (b - a) * Saturate(t);

    /// <summary>1 on a line of the given half width, fading to 0 just outside it.</summary>
    private static float Line(float distance, float halfWidth, float soft = 0.05f) =>
        1f - SmoothStep(halfWidth - soft, halfWidth + soft, MathF.Abs(distance));

    /// <summary>Soft blob: 1 at the centre of the unit circle, 0 at its edge.</summary>
    private static float Blob(float u, float v)
    {
        var d = MathF.Sqrt(u * u + v * v);
        return SmoothStep(1f - Saturate(d));
    }

    /// <summary>Hard-edged disc with a soft rim; (u, v) is the offset from its centre.</summary>
    private static float Disc(float u, float v, float radius, float soft = 0.05f) =>
        1f - SmoothStep(radius - soft, radius + soft, MathF.Sqrt(u * u + v * v));

    /// <summary>1 inside the range, with soft ends.</summary>
    private static float Band(float x, float from, float to, float soft = 0.05f) =>
        SmoothStep(from - soft, from + soft, x) * (1f - SmoothStep(to - soft, to + soft, x));

    private static float Fract(float x) => x - MathF.Floor(x);

    private Vector3 SkinTone(Vector3 p) => _skin * (1f + 0.05f * (Noise.Fbm(p * 0.35f) - 0.5f));

    /// <summary>Woven cloth: broad folds, a fine weave and optionally the camouflage print.</summary>
    private Vector3 Cloth(Vector3 color, Vector3 p, bool camouflage = false)
    {
        if (camouflage) color = Camouflage(color, p);
        var folds = Noise.Fbm(new Vector3(p.X * 0.22f, p.Y * 0.22f, p.Z * 0.08f)) - 0.5f;
        var weave = Noise.Value(p * 7f) - 0.5f;
        return color * (1f + 0.18f * folds + 0.05f * weave);
    }

    private bool CamoTop => _spec.CamoCoverage != CamoCoverage.TrousersOnly;
    private bool CamoBottom => _spec.CamoCoverage != CamoCoverage.TopOnly;

    /// <summary>Knitted balaclava: the accent colour with vertical ribs.</summary>
    private Vector3 Knit(Vector3 p)
    {
        var rib = MathF.Sin(MathF.Atan2(p.X, p.Y - _c.HeadCenter.Y) * 34f);
        return Cloth(_accent * 1.15f + new Vector3(0.03f), p) * (1f + 0.07f * rib);
    }

    /// <summary>Shemagh: sand cotton with a lattice of small diamonds in the accent colour, softly folded.</summary>
    private Vector3 Shemagh(Vector3 p)
    {
        var u = p.X / 1.15f + p.Z / 1.15f;
        var v = p.X / 1.15f - p.Z / 1.15f + 0.7f * p.Y / 1.15f;
        var lattice = MathF.Max(Line(Fract(u) - 0.5f, 0.07f, 0.03f), Line(Fract(v) - 0.5f, 0.07f, 0.03f));
        var dot = Disc(Fract(u) - 0.5f, Fract(v) - 0.5f, 0.16f, 0.04f);
        var cloth = Mix(new Vector3(0.84f, 0.80f, 0.69f), _accent, 0.85f * MathF.Max(lattice, dot));
        var folds = Noise.Fbm(new Vector3(p.X * 0.35f, p.Y * 0.35f, p.Z * 0.9f)) - 0.5f;
        return cloth * (1f + 0.22f * folds);
    }

    /// <summary>Check shirt: broad darker bands and a thin pale line, both ways.</summary>
    private static Vector3 Plaid(Vector3 color, Vector3 p)
    {
        var a = Fract((p.X + 0.7f * p.Y) / 7f);
        var b = Fract(p.Z / 7f);
        var dark = 0.5f * (Band(a, 0f, 0.36f, 0.02f) + Band(b, 0f, 0.36f, 0.02f));
        var line = MathF.Max(Line(a - 0.62f, 0.035f, 0.01f), Line(b - 0.62f, 0.035f, 0.01f));
        return Mix(color * (1f - 0.38f * dark), new Vector3(0.9f, 0.88f, 0.8f), 0.45f * line);
    }

    /// <summary>Denim: diagonal twill, paler where it is worn (thigh fronts, knees).</summary>
    private static Vector3 Denim(Vector3 color, Vector3 p, float wear)
    {
        var twill = Line(Fract((p.Z + 0.6f * (p.X + p.Y)) / 0.42f) - 0.5f, 0.16f, 0.08f);
        var slub = Noise.Fbm(new Vector3(p.X * 0.6f, p.Y * 0.6f, p.Z * 2.5f)) - 0.5f;
        var c = color * (1f - 0.12f * twill + 0.10f * slub);
        return Mix(c, c * 1.35f + new Vector3(0.05f, 0.06f, 0.08f), 0.55f * wear);
    }

    private Vector3 Camouflage(Vector3 color, Vector3 p)
    {
        if (_spec.Camouflage == LowPolyCharGen.Camouflage.None) return color;
        var dark = color * 0.55f;
        var brown = Mix(color, new Vector3(0.33f, 0.25f, 0.16f), 0.75f);
        var light = Mix(color, new Vector3(0.78f, 0.74f, 0.60f), 0.5f);
        switch (_spec.Edition)
        {
            case Edition.Desert:   // three-colour desert: sand, khaki ground, brown and a little chocolate
                light = new Vector3(0.84f, 0.78f, 0.62f); brown = new Vector3(0.55f, 0.42f, 0.27f); dark = new Vector3(0.38f, 0.29f, 0.20f);
                break;
            case Edition.Snow:     // white ground with pale and mid grey
                light = new Vector3(0.96f, 0.96f, 0.97f); brown = new Vector3(0.77f, 0.79f, 0.81f); dark = new Vector3(0.56f, 0.59f, 0.62f);
                break;
            case Edition.Militia:  // urban greys
                light = new Vector3(0.62f, 0.63f, 0.64f); brown = new Vector3(0.36f, 0.37f, 0.39f); dark = new Vector3(0.16f, 0.16f, 0.17f);
                break;
            case Edition.Jungle:   // greens with brown and near-black
                light = new Vector3(0.44f, 0.50f, 0.27f); brown = new Vector3(0.35f, 0.27f, 0.18f); dark = new Vector3(0.13f, 0.15f, 0.11f);
                break;
        }

        var soft = 0.012f;
        if (_spec.Camouflage == LowPolyCharGen.Camouflage.Digital)
        {
            // Snap to square cells for the pixelated look.
            const float cell = 1.2f;
            p = new Vector3(MathF.Floor(p.X / cell), MathF.Floor(p.Y / cell), MathF.Floor(p.Z / cell)) * cell;
            soft = 0.0001f;
        }
        var q = p * 0.085f;
        var a = Noise.Fbm(q + new Vector3(3, 7, 1));
        var b = Noise.Fbm(q * 1.3f + new Vector3(40, 12, 9));
        var d = Noise.Fbm(q * 0.8f + new Vector3(11, 50, 23));
        color = Mix(color, light, 1f - SmoothStep(0.41f - soft, 0.41f + soft, d));
        color = Mix(color, brown, SmoothStep(0.55f - soft, 0.55f + soft, b));
        color = Mix(color, dark, SmoothStep(0.60f - soft, 0.60f + soft, a));
        return color;
    }

    private Vector3 HairStrands(Vector3 p)
    {
        var strands = Noise.Value(new Vector3(p.X * 3.2f, p.Y * 3.2f, p.Z * 0.45f));
        var clumps = Noise.Fbm(new Vector3(p.X * 0.5f, p.Y * 0.5f, p.Z * 0.15f));
        return _hair * (0.72f + 0.38f * strands + 0.25f * (clumps - 0.5f));
    }

    private Vector3 Leather(Vector3 color, Vector3 p)
    {
        var grain = Noise.Fbm(p * 1.6f) - 0.5f;
        var scuff = SmoothStep(0.62f, 0.8f, Noise.Fbm(p * 0.45f + new Vector3(5, 5, 5)));
        return Mix(color * (1f + 0.16f * grain), color * 1.5f + new Vector3(0.04f), 0.25f * scuff);
    }

    // ---- plain materials -----------------------------------------------------------------

    private Vector3 Material(in Texel t)
    {
        var p = t.P;
        switch (t.Slot)
        {
            case Slot.Skin: return SkinTone(p);
            case Slot.Hair: return HairStrands(p);
            case Slot.Dark: return Ink * (1f + 0.3f * (Noise.Value(p * 2f) - 0.5f));
            case Slot.Lens:
            {
                // Dark glass with a diagonal reflection.
                var streak = Line(Fract((t.Uv.X + t.Uv.Y) / MathF.Max(1f, t.Size.X + t.Size.Y) * 2.4f) - 0.4f, 0.09f, 0.04f);
                return new Vector3(0.06f, 0.07f, 0.09f) + new Vector3(0.16f, 0.18f, 0.2f) * streak;
            }
            case Slot.Shirt: return Cloth(_shirt, p, CamoTop);
            case Slot.ShirtTrim: return Cloth(_shirt * 0.78f, p);
            case Slot.Trousers: return Cloth(_trousers, p, CamoBottom);
            case Slot.TrousersTrim: return Cloth(_trousers * 0.8f, p);
            case Slot.Boots: return Leather(_boots, p);
            case Slot.Accent: return Cloth(_accent, p);
            case Slot.Gear: return Cloth(_gear, p);
            case Slot.GearTrim: return Cloth(_gear * 0.74f, p);
            case Slot.Belt: return Leather(_accent * 0.8f, p);
            case Slot.Hat: return Cloth(_hat, p, CamoTop);
            case Slot.HatTrim: return Cloth(_hat * 0.72f, p);
            case Slot.Metal: return Steel * (0.8f + 0.25f * t.N.Z);
            case Slot.Scarf: return _spec.FaceCover == FaceCover.Shemagh || _spec.Edition == Edition.Militia ? Shemagh(p) : Cloth(_accent, p);
            default: return new Vector3(1, 0, 1);
        }
    }

    /// <summary>Stitching, flaps and webbing drawn in a primitive's own chart.</summary>
    private static Vector3 Detail(in Texel t, Vector3 color)
    {
        if (t.Detail == PaintDetail.None) return color;
        var edge = MathF.Min(MathF.Min(t.Uv.X, t.Uv.Y), MathF.Min(t.Size.X - t.Uv.X, t.Size.Y - t.Uv.Y));

        switch (t.Detail)
        {
            case PaintDetail.Panel or PaintDetail.Pouch or PaintDetail.Webbing:
                // Worn, darker border with a row of stitches inside it.
                color *= 1f - 0.24f * (1f - SmoothStep(0f, 0.8f, edge));
                if (MathF.Min(t.Size.X, t.Size.Y) > 3f)
                    color *= 1f - 0.2f * Line(edge - 0.85f, 0.07f) * (Fract((t.Uv.X + t.Uv.Y) / 0.55f) < 0.6f ? 1f : 0f);
                break;
        }

        switch (t.Detail)
        {
            case PaintDetail.Pouch when t.Size.Y > 2.5f && MathF.Abs(t.N.Z) < 0.6f:
            {
                // Flap over the top third, closed with a snap.
                var flap = t.Size.Y * 0.62f;
                if (t.Uv.Y > flap) color *= 1.07f;
                color *= 1f - 0.4f * Line(t.Uv.Y - flap, 0.12f);
                color *= 1f - 0.18f * Band(t.Uv.Y, flap - 0.7f, flap - 0.12f, 0.2f);   // shadow under the flap
                if (t.Size.X > 3f)
                    color = Mix(color, Steel * 0.6f, Disc(t.Uv.X - t.Size.X / 2, t.Uv.Y - flap - 0.9f, 0.36f));
                break;
            }
            case PaintDetail.Webbing:
            {
                // Rows of load-bearing webbing with bar tacks.
                var row = Fract(t.P.Z / 2.6f);
                if (row < 0.5f)
                {
                    color *= 0.86f;
                    color *= 1f - 0.3f * Line(Fract(t.Uv.X / 3.8f) - 0.5f, 0.025f, 0.01f);
                }
                color *= 1f - 0.2f * Line(row - 0.5f, 0.03f, 0.015f) - 0.2f * Line(row, 0.03f, 0.015f);
                break;
            }
            case PaintDetail.Strap:
                // Ribbed nylon weave across the strap.
                color *= 1f + 0.07f * MathF.Sin(t.Uv.Y * MathF.Tau / 0.45f);
                break;
            case PaintDetail.Ribbed:
                color *= 1f + 0.09f * MathF.Sin(t.Uv.X * MathF.Tau / 0.75f);
                break;
        }
        return color;
    }
}

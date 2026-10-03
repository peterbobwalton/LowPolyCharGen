using System.Numerics;
using static LowPolyCharGen.Geo;

namespace LowPolyCharGen.Texturing;

/// <summary>Face, scalp hair, facial hair, ears and neck.</summary>
internal sealed partial class Painter
{
    private const float HeadHeightCm = 23.9f;   // chin to crown at head scale 1

    private Vector3 Head(in Texel t)
    {
        var p = t.P;
        var s = _c.HeadScale;

        // Face coordinates in centimetres at head scale 1: fx across (signed), fz above the chin.
        var fx = p.X / s;
        var ax = MathF.Abs(fx);
        var fz = (p.Z - _c.ChinZ) / s;
        var th = fz / HeadHeightCm;

        // Direction round the head: -1 looking at the face, +1 at the back of the head.
        var radial = new Vector2(p.X, p.Y - _c.HeadCenter.Y);
        var frontness = radial.LengthSquared() > 1e-6f ? radial.Y / radial.Length() : -1f;
        var angle = MathF.Acos(Math.Clamp(-frontness, -1f, 1f)) * 180f / MathF.PI;   // 0 = front, 180 = back
        var face = 1f - SmoothStep(-0.55f, -0.2f, frontness);                          // 1 on the face, 0 from the ears back
        var under = SmoothStep(0.3f, 0.65f, -t.N.Z);                                   // underside of the jaw

        var color = SkinTone(p);
        var male = !_c.Female;

        var eyeX = _c.EyeX / s;
        var eyeZ = (_c.EyeZ - _c.ChinZ) / s;
        var mouthZ = (_c.MouthZ - _c.ChinZ) / s;

        // ---- skin shading: what a portrait painter would put in before the features ----
        color = Mix(color, _skin * new Vector3(1.06f, 0.88f, 0.86f), 0.30f * face * Blob((ax - 4.4f) / 2.4f, (fz - 8.2f) / 2.0f));   // cheeks
        color *= 1f - (male ? 0.20f : 0.11f) * face * Blob((ax - eyeX) / 2.6f, (fz - eyeZ - 0.3f) / 1.7f);                          // eye sockets
        color *= 1f - 0.10f * face * Line(ax - 1.5f, 0.4f, 0.25f) * Band(fz, 8.6f, 12.6f, 0.5f);                                     // sides of the nose
        color *= 1f + 0.06f * face * Blob(ax / 0.9f, (fz - 9.9f) / 1.0f);                                                            // nose tip
        color = Mix(color, _skin * 0.36f, face * MathF.Min(1f, 1.7f * Blob((ax - 0.85f) / 0.55f, (fz - 8.45f) / 0.34f)));            // nostrils
        color *= 1f - 0.09f * face * Blob(ax / 1.9f, (fz - mouthZ + 1.3f) / 0.55f);                                                  // under the lower lip
        color *= 1f + 0.05f * face * Blob(ax / 3.2f, (fz - 16.6f) / 2.2f);                                                           // forehead
        color *= 1f - 0.07f * SmoothStep(4.6f, 7.2f, ax) * Band(fz, 1f, 11f, 2f);                                                    // sides of the face
        color *= 1f - 0.08f * under;

        // ---- facial hair (under the lips, so the mouth stays visible) ----
        var beard = FacialHair(ax, fz, angle, mouthZ, p, under);
        if (beard > 0f)
        {
            var strands = Noise.Value(new Vector3(p.X * 5f, p.Y * 5f, p.Z * 1.2f));
            color = Mix(color, _hair * (0.7f + 0.5f * strands), beard);
        }

        // ---- lips ----
        if (face > 0f)
        {
            var halfWidth = male ? 2.5f : 2.3f;
            var q = ax / halfWidth;
            if (q < 1.15f)
            {
                var dz = fz - mouthZ;
                var span = MathF.Max(0f, 1f - q * q);
                var upper = (male ? 0.36f : 0.46f) * MathF.Pow(span, 0.7f) * (1f - 0.22f * MathF.Exp(-(ax / 0.4f) * (ax / 0.4f)));
                var lower = (male ? 0.50f : 0.62f) * MathF.Pow(span, 0.55f);
                const float soft = 0.04f;
                var onUpper = Saturate(dz / soft) * Saturate((upper - dz) / soft);
                var onLower = Saturate(-dz / soft) * Saturate((lower + dz) / soft);
                var strength = face * (beard > 0.5f ? 0.75f : 1f);
                color = Mix(color, _lips * 0.86f, onUpper * strength);
                color = Mix(color, _lips * (1f + 0.10f * Blob(ax / 1.2f, (dz + 0.26f) / 0.2f)), onLower * strength);
                color = Mix(color, new Vector3(0.24f, 0.11f, 0.10f), face * Line(dz, 0.055f, 0.03f) * (1f - SmoothStep(0.95f, 1.08f, q)));
            }
            color *= 1f - 0.12f * face * Blob((ax - halfWidth) / 0.5f, (fz - mouthZ) / 0.4f);   // corners
        }

        // ---- eyes ----
        if (face > 0f && MathF.Abs(ax - eyeX) < 2.2f)
        {
            const float halfWidth = 1.6f;
            var u = (ax - eyeX) / halfWidth;                    // -1 inner corner .. +1 outer corner
            var dz = fz - (eyeZ + 0.12f * u);                   // outer corner sits a little higher
            var span = MathF.Max(0f, 1f - u * u);
            var upper = 0.62f * MathF.Pow(span, 0.75f);
            var lower = -0.38f * MathF.Pow(span, 0.9f);
            const float soft = 0.035f;
            var open = Saturate((upper - dz) / soft) * Saturate((dz - lower) / soft) * (MathF.Abs(u) < 1f ? 1f : 0f);

            if (open > 0f)
            {
                // Eyeball: white, iris with a dark rim, pupil and a catch light.
                var lx = fx - MathF.Sign(fx) * eyeX;            // signed, so both catch lights sit on the same side
                var white = new Vector3(0.91f, 0.88f, 0.84f) * (0.72f + 0.28f * span);
                white *= 1f - 0.22f * SmoothStep(upper - 0.22f, upper, dz);   // shadow of the upper lid
                var d = MathF.Sqrt(lx * lx + (dz - 0.08f) * (dz - 0.08f));
                var iris = _eye * (0.55f + 0.75f * (1f - d / 0.66f));
                iris *= 1f - 0.5f * SmoothStep(0.50f, 0.64f, d);
                var eyeball = Mix(white, iris, 1f - SmoothStep(0.63f, 0.69f, d));
                eyeball = Mix(eyeball, new Vector3(0.03f), 1f - SmoothStep(0.24f, 0.29f, d));
                eyeball = Mix(eyeball, Vector3.One, Disc(lx - 0.2f, dz - 0.24f, 0.085f, 0.03f));
                color = Mix(color, eyeball, open * face);
            }

            // Lash line along the upper lid, a softer lower lid and the crease above.
            var lash = (male ? 0.13f : 0.21f) * (0.55f + 0.45f * Saturate(u + 1f));
            var onLash = Band(dz, upper, upper + lash, 0.03f) * (1f - SmoothStep(1.05f, 1.2f, MathF.Abs(u)));
            color = Mix(color, new Vector3(0.11f, 0.08f, 0.07f), onLash * face);
            color *= 1f - 0.22f * face * Band(dz, lower - 0.08f, lower, 0.03f) * (MathF.Abs(u) < 1f ? 1f : 0f);
            color *= 1f - 0.14f * face * Line(dz - (upper + 0.4f), 0.05f, 0.04f) * span;
        }

        // ---- eyebrows ----
        if (face > 0f)
        {
            const float inner = 0.8f, outer = 4.75f;
            var v = (ax - inner) / (outer - inner);
            if (v > -0.1f && v < 1.1f)
            {
                var browZ = (_c.BrowZ - _c.ChinZ) / s;
                var vs = Saturate(v);
                var centre = male
                    ? browZ - 0.25f * (1f - vs) + 0.30f * MathF.Sin(MathF.PI * MathF.Pow(vs, 0.8f))
                    : browZ - 0.10f + 0.45f * MathF.Sin(MathF.PI * MathF.Pow(vs, 0.7f));
                var half = (male ? 0.40f : 0.23f) * (1f - 0.62f * vs * vs);
                var mask = Line(fz - centre, half, 0.07f) * SmoothStep(-0.03f, 0.06f, v) * (1f - SmoothStep(0.92f, 1.03f, v));
                mask *= 0.72f + 0.5f * Noise.Value(new Vector3(p.X * 9f, p.Y * 2f, p.Z * 3f));
                color = Mix(color, _hair * 0.8f, mask * face);
            }
        }

        // ---- hair on the scalp ----
        if (_c.ScalpHair is { } scalp)
        {
            var edge = scalp.At(frontness) + (Noise.Value(p * 1.6f) - 0.5f) * 0.03f;
            var mask = SmoothStep(edge - 0.012f, edge + 0.014f, th);
            if (_c.ScalpHairDensity >= 0.7f)
            {
                // Sideburn in front of the ear.
                var sideburn = Line(angle - 74f, 5f, 4f) * Band(th, 0.44f, edge + 0.02f, 0.035f);
                mask = MathF.Max(mask, sideburn);
            }
            color = Mix(color, HairStrands(p), mask * _c.ScalpHairDensity);
        }

        // ---- face coverings ----
        switch (_spec.FaceCover)
        {
            case LowPolyCharGen.FaceCover.Balaclava:
            {
                // Everything is knit except a slit across the eyes, with a rolled edge.
                var slit = face * Band(fz, eyeZ - 1.25f, eyeZ + 1.35f, 0.12f) * (1f - SmoothStep(eyeX + 2.3f, eyeX + 2.7f, ax));
                var knit = Knit(p) * (1f - 0.25f * Line(fz - (eyeZ - 1.25f), 0.12f) - 0.25f * Line(fz - (eyeZ + 1.35f), 0.12f));
                color = Mix(knit, color * 0.92f, slit);
                break;
            }
            case LowPolyCharGen.FaceCover.Shemagh:
            {
                // Wrapped over the mouth and nose, up to just under the eyes, all the way round.
                var top = (_c.NoseTipZ - _c.ChinZ) / s + 0.2f + 0.8f * SmoothStep(60f, 120f, angle);
                var cover = 1f - SmoothStep(top - 0.15f, top + 0.15f, fz);
                var wrap = Shemagh(p) * (1f - 0.3f * Line(fz - top, 0.25f, 0.2f));
                color = Mix(color, wrap, cover);
                break;
            }
        }
        return color;
    }

    /// <summary>How strongly facial hair covers the skin at a point of the face (0 = none).</summary>
    private float FacialHair(float ax, float fz, float angle, float mouthZ, Vector3 p, float under)
    {
        var style = _spec.FacialHair;
        if (style == LowPolyCharGen.FacialHair.None) return 0f;
        var wobble = (Noise.Value(p * 2.5f) - 0.5f) * 0.5f;

        // Upper lip.
        var moustache = Band(fz, mouthZ + 0.3f, mouthZ + 2.25f - 0.28f * ax + wobble * 0.4f, 0.12f) * (1f - SmoothStep(2.7f, 3.1f, ax));
        // Down past the corners of the mouth to the chin.
        var corners = Band(ax, 2.2f, 3.3f, 0.2f) * Band(fz, 2.6f, mouthZ + 1f, 0.3f);
        // Chin tuft.
        var chin = (1f - SmoothStep(2.2f, 2.9f, ax)) * (1f - SmoothStep(3.9f + wobble, 4.5f + wobble, fz));
        // Along the jaw, climbing to the ear, and on under the jaw.
        var jawTop = 4.3f + 1.6f * SmoothStep(20f, 70f, angle) + 5.0f * SmoothStep(58f, 86f, angle) + wobble;
        var jaw = (1f - SmoothStep(jawTop - 0.3f, jawTop + 0.3f, fz)) * (1f - SmoothStep(96f, 104f, angle));
        var throat = 1f - SmoothStep(_c.HeadCenter.Y - 5.5f * _c.HeadScale, _c.HeadCenter.Y - 2.5f * _c.HeadScale, p.Y);
        jaw = MathF.Max(jaw * (1f - under), under * throat);

        switch (style)
        {
            case LowPolyCharGen.FacialHair.Moustache:
                return moustache * 0.95f;
            case LowPolyCharGen.FacialHair.Goatee:
                return MathF.Max(MathF.Max(moustache, corners), chin * (1f - under) + under * throat * (1f - SmoothStep(2.2f, 2.9f, ax))) * 0.95f;
            case LowPolyCharGen.FacialHair.Beard:
                return MathF.Max(MathF.Max(moustache, corners), jaw) * 0.96f;
            default:   // stubble: the beard's area, speckled and faint
                var speckle = Noise.Value(p * 14f);
                return MathF.Max(MathF.Max(moustache, corners), jaw) * (0.2f + 0.3f * speckle);
        }
    }

    private Vector3 Ear(in Texel t)
    {
        // Mirror onto the left ear, then into the ear's own plane.
        var p = new Vector3(MathF.Abs(t.P.X), t.P.Y, t.P.Z);
        var n = new Vector3(t.P.X < 0 ? -t.N.X : t.N.X, t.N.Y, t.N.Z);
        var d = (p - _c.EarCenter) / _c.EarScale;
        var a = Vector3.Dot(d, _c.EarAcross);
        var u = Vector3.Dot(d, _c.EarUp);
        var hollowSide = Vector3.Dot(n, Vector3.Cross(_c.EarAcross, _c.EarUp)) > 0f;

        if (_spec.FaceCover == LowPolyCharGen.FaceCover.Balaclava) return Knit(t.P) * (hollowSide ? 0.8f : 1f);
        var color = SkinTone(t.P) * new Vector3(1.03f, 0.94f, 0.92f);
        if (!hollowSide) return color * 0.9f;

        color *= 1f - 0.40f * Blob((a + 0.35f) / 1.05f, (u - 0.1f) / 1.8f);                 // the bowl
        var ridge = MathF.Sqrt((a + 0.1f) * (a + 0.1f) / (1.3f * 1.3f) + (u - 0.1f) * (u - 0.1f) / (2.4f * 2.4f));
        color *= 1f + 0.12f * Line(ridge - 0.74f, 0.08f, 0.06f);                            // inner fold
        color *= 1f - 0.12f * Line(ridge - 0.92f, 0.07f, 0.06f);                            // groove inside the rim
        color = Mix(color, _skin * 0.3f, 0.75f * Blob((a + 0.95f) / 0.4f, (u - 0.2f) / 0.5f));   // ear canal
        return color;
    }

    private Vector3 Neck(in Texel t)
    {
        var p = t.P;
        var ax = MathF.Abs(p.X);
        var h = p.Z - _c.NeckBaseZ;                 // height above the base of the neck
        var front = p.Y < _c.NeckCenter.Y;
        var skin = SkinTone(p);
        if (_spec.FaceCover == LowPolyCharGen.FaceCover.Balaclava) return Knit(p);
        if (_spec.FaceCover == LowPolyCharGen.FaceCover.Shemagh) return Shemagh(p);

        switch (_spec.Torso)
        {
            case TorsoStyle.LongSleeve or TorsoStyle.CheckShirt or TorsoStyle.Polo:
            {
                // Fold-down collar, open at the throat.
                var open = front && ax < 1.6f + h * 0.6f;
                if (!open && h < 2.4f)
                {
                    var collar = Cloth(_shirt * 1.06f, p, CamoTop);
                    return collar * (1f - 0.3f * Line(h - 2.4f, 0.12f));
                }
                break;
            }
            case TorsoStyle.Hoodie:
                // The hood lies in folds round the neck, open at the front.
                if (h < 4.2f && !(front && ax < 1.4f + h * 0.5f))
                    return Cloth(_shirt * 0.82f, p) * (1f + 0.1f * MathF.Sin(MathF.Atan2(p.X, p.Y) * 8f + h)) * (1f - 0.3f * Line(h - 4.2f, 0.15f));
                break;
            case TorsoStyle.Jacket:
                // Stand-up collar with the zip running into it.
                if (h < 3.2f)
                {
                    var collar = Cloth(_shirt * 0.8f, p);
                    if (front && ax < 0.3f) return Steel * 0.45f;
                    return collar * (1f - 0.3f * Line(h - 3.2f, 0.12f));
                }
                break;
        }
        return skin;
    }
}

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
        // A modelled face has its own sockets, nose and nostrils: the painted shading only hints at them.
        var modelled = _c.ModelledFace;
        color *= 1f - (male ? 0.20f : 0.11f) * (modelled ? 0.4f : 1f) * face * Blob((ax - eyeX) / 2.6f, (fz - eyeZ - 0.3f) / 1.7f);   // eye sockets
        if (!modelled)
        {
            color *= 1f - 0.10f * face * Line(ax - 1.5f, 0.4f, 0.25f) * Band(fz, 8.6f, 12.6f, 0.5f);                                 // sides of the nose
            color *= 1f + 0.06f * face * Blob(ax / 0.9f, (fz - 9.9f) / 1.0f);                                                        // nose tip
            // Light bounced up off the lip into the underside of the nose, so it is not a dark bar and the nostrils read.
            var noseUnder = face * SmoothStep(-0.1f, 0.35f, -t.N.Z) * (1f - SmoothStep(1.6f, 2.4f, ax)) * Band(fz, _c.Nostril.Y - 1.2f, _c.Nostril.Y + 1.6f, 0.3f);
            color *= 1f + 0.35f * noseUnder;
            var nostrils = MathF.Min(1f, 1.5f * Blob((ax - _c.Nostril.X) / 0.34f, (fz - _c.Nostril.Y) / 0.42f)) * SmoothStep(-0.25f, 0.15f, -t.N.Z);
            color = Mix(color, _skin * 0.36f, face * nostrils);                                                                      // nostrils, under the tip
        }
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
        if (modelled)
        {
            // The lids' rims and lashes: darker skin right round the eyeball, most along the upper lid.
            var centre = _c.EyeCenter with { X = MathF.Sign(p.X) * _c.EyeCenter.X };
            var gap = Vector3.Distance(p, centre) - _c.EyeRadius;
            var upperLid = SmoothStep(-0.25f, 0.15f, p.Z - centre.Z);
            var rim = (1f - SmoothStep(0.05f, male ? 0.32f : 0.42f, gap)) * (p.Y < centre.Y ? 1f : 0f);
            color = Mix(color, new Vector3(0.09f, 0.07f, 0.06f), face * rim * Geo.Lerp(0.25f, male ? 0.75f : 0.95f, upperLid));
        }
        else if (face > 0f && MathF.Abs(ax - eyeX) < 2.4f)
            color = Eye(color, fx, ax, fz, eyeX, eyeZ, face, male);

        // ---- eyebrows ----
        if (face > 0f)
            color = Eyebrow(color, ax, fz, (_c.BrowZ - _c.ChinZ) / s, eyeX, face, male, p);

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

        // ---- fatigue and stress ----
        var fatigue = _spec.Fatigue * face;
        if (fatigue > 0f)
        {
            var bag = Blob((ax - eyeX) / 2.1f, (fz - (eyeZ - 1.05f)) / 0.85f);
            color *= 1f - 0.5f * fatigue * bag;                                                                    // dark rings and bags
            color = Mix(color, new Vector3(0.62f, 0.30f, 0.30f), 0.25f * fatigue * Line(MathF.Abs(ax - eyeX) - 0.0f, 1.9f, 0.3f) * Band(fz, eyeZ - 0.55f, eyeZ + 0.75f, 0.12f));   // red lids
            color *= 1f - 0.26f * fatigue * Blob((ax - 4.9f) / 1.6f, (fz - 6.4f) / 1.7f);                           // hollow cheeks
            var luma = Vector3.Dot(color, new Vector3(0.3f, 0.59f, 0.11f));
            color = Mix(color, new Vector3(luma * 1.02f, luma, luma * 0.86f), 0.22f * fatigue);                    // sallow, drained
        }
        var stress = _spec.Stress * face;
        if (stress > 0f)
        {
            var browZ = (_c.BrowZ - _c.ChinZ) / s;
            color *= 1f - 0.5f * stress * Line(ax - 0.55f, 0.09f, 0.08f) * Band(fz, browZ - 0.9f, browZ + 1.3f, 0.3f);     // furrows between the brows
            var lines = Line(Fract((fz - browZ - 1.6f) / 1.05f) - 0.5f, 0.06f, 0.05f) * Band(fz, browZ + 1.4f, browZ + 4.8f, 0.4f) * (1f - SmoothStep(3.6f, 4.6f, ax));
            color *= 1f - 0.32f * stress * lines;                                                                   // forehead lines
            color *= 1f - 0.42f * stress * Line(Segment(ax, fz, 1.75f, 8.6f, 3.1f, mouthZ - 0.4f), 0.13f, 0.12f);   // nose-to-mouth folds
            var sweat = SmoothStep(0.72f, 0.86f, Noise.Value(p * 2.6f)) * Band(fz, browZ + 0.8f, 19f, 1f);
            color = Mix(color, color * 1.25f + new Vector3(0.06f), 0.5f * stress * sweat);                        // beads of sweat
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

    /// <summary>
    /// One eye, in face centimetres: an almond opening whose upper lid peaks towards the nose and lower
    /// lid dips towards the ear, a shaded eyeball with the iris tucked under both lids, then the lash
    /// lines, the lid crease, the lower lid's wet rim and the shadow under the eye.
    /// </summary>
    /// <summary>
    /// The realistic body's eyeball: white, iris and pupil laid out by angle from straight ahead, so
    /// they sit on the ball wherever the lids leave it showing.
    /// </summary>
    private Vector3 EyeBall(in Texel t)
    {
        var p = t.P;
        var centre = _c.EyeCenter with { X = MathF.Sign(p.X) * _c.EyeCenter.X };
        var dir = Vector3.Normalize(p - centre);
        // Looking straight ahead, a touch down.
        var gaze = Vector3.Normalize(new Vector3(0f, -1f, -0.1f));
        var arc = MathF.Acos(Math.Clamp(Vector3.Dot(dir, gaze), -1f, 1f)) * _c.EyeRadius;   // cm over the ball from the centre of the pupil
        var side = Vector3.Normalize(Vector3.Cross(gaze, Vector3.UnitZ));
        var lx = Vector3.Dot(dir, side) * MathF.Sign(p.X);
        var ly = dir.Z;

        var white = new Vector3(0.84f, 0.81f, 0.77f) * (1f - 0.35f * SmoothStep(0.6f, 1.6f, arc));
        white = Mix(white, new Vector3(0.82f, 0.6f, 0.57f), 0.4f * SmoothStep(0.9f, 1.5f, arc) * SmoothStep(0.2f, 0.7f, MathF.Abs(lx)));
        const float radius = 0.64f;
        var angle = MathF.Atan2(ly, lx);
        var fibres = 0.8f + 0.4f * Noise.Value(new Vector3(MathF.Cos(angle) * 5f, MathF.Sin(angle) * 5f, arc * 1.5f));
        var iris = _eye * (0.6f + 0.6f * (1f - arc / radius)) * fibres;
        iris = Mix(iris, _eye * 1.35f + new Vector3(0.04f), 0.45f * Line(arc - 0.28f, 0.06f, 0.05f));
        iris *= 1f - 0.55f * SmoothStep(radius - 0.14f, radius - 0.02f, arc);
        var colour = Mix(white, iris, 1f - SmoothStep(radius - 0.03f, radius + 0.02f, arc));
        colour = Mix(colour, new Vector3(0.02f), 1f - SmoothStep(0.2f, 0.24f, arc));                       // pupil
        colour = Mix(colour, Vector3.One, 0.85f * Disc(lx * _c.EyeRadius - 0.17f, ly * _c.EyeRadius - 0.2f, 0.07f, 0.025f));   // catch light
        colour *= 1f - 0.45f * SmoothStep(-0.2f, 0.45f, ly * _c.EyeRadius);                                   // the upper lid's shadow
        return colour;
    }

    private Vector3 Eye(Vector3 color, float fx, float ax, float fz, float eyeX, float eyeZ, float face, bool male)
    {
        const float halfWidth = 1.5f;
        var u = (ax - eyeX) / halfWidth;                    // -1 inner corner .. +1 outer corner
        var dz = fz - (eyeZ + 0.10f * u);                   // outer corner sits a little higher
        var inside = MathF.Abs(u) < 1f ? 1f : 0f;
        var edge = 1f - SmoothStep(1.0f, 1.12f, MathF.Abs(u));
        var span = MathF.Max(0f, 1f - u * u);
        var uUp = u + 0.22f * span;                         // skews the upper lid's peak inwards
        var uLo = u - 0.18f * span;                         // and the lower lid's dip outwards
        var spanUp = MathF.Max(0f, 1f - uUp * uUp);
        var spanLo = MathF.Max(0f, 1f - uLo * uLo);
        var upper = (male ? 0.54f : 0.60f) * MathF.Pow(spanUp, 0.7f);
        var lower = -0.38f * MathF.Pow(spanLo, 0.85f);
        const float soft = 0.035f;
        var open = Saturate((upper - dz) / soft) * Saturate((dz - lower) / soft) * inside;

        if (open > 0f)
        {
            // The white is a ball: darker and pinker towards the corners, in the upper lid's shadow.
            var white = new Vector3(0.80f, 0.77f, 0.73f) * (0.62f + 0.38f * MathF.Pow(span, 0.6f));
            white = Mix(white, new Vector3(0.80f, 0.58f, 0.55f), 0.35f * SmoothStep(0.55f, 0.95f, MathF.Abs(u)));

            // Iris: radial fibres, a lighter ring round the pupil and a dark rim; the lids cover its top and bottom.
            var lx = fx - MathF.Sign(fx) * eyeX;            // signed, so both catch lights sit on the same side
            var iy = dz - 0.02f;
            var d = MathF.Sqrt(lx * lx + iy * iy);
            const float radius = 0.62f;
            var dir = d > 1e-4f ? new Vector2(lx, iy) / d : Vector2.UnitX;
            var fibres = 0.8f + 0.4f * Noise.Value(new Vector3(dir.X * 5f, dir.Y * 5f, d * 1.5f));
            var iris = _eye * (0.6f + 0.6f * (1f - d / radius)) * fibres;
            iris = Mix(iris, _eye * 1.35f + new Vector3(0.04f), 0.45f * Line(d - 0.3f, 0.06f, 0.05f));
            iris *= 1f - 0.55f * SmoothStep(radius - 0.16f, radius - 0.02f, d);
            var eyeball = Mix(white, iris, 1f - SmoothStep(radius - 0.03f, radius + 0.02f, d));
            eyeball = Mix(eyeball, new Vector3(0.02f), 1f - SmoothStep(0.19f, 0.23f, d));
            eyeball *= 1f - 0.35f * SmoothStep(upper - 0.3f, upper, dz);                                        // upper lid's shadow
            eyeball = Mix(eyeball, new Vector3(0.78f, 0.46f, 0.43f), Blob((u + 0.93f) / 0.12f, dz / 0.2f));     // tear duct
            eyeball = Mix(eyeball, Vector3.One, 0.9f * Disc(lx - 0.18f, iy - 0.18f, 0.07f, 0.025f));            // catch light
            color = Mix(color, eyeball, open * face);
        }

        // Upper lash line, thickening towards the outer corner and flicking up past it.
        var lash = (male ? 0.10f : 0.15f) * (0.6f + 0.5f * Saturate((u + 1f) / 2f));
        var onLash = Band(dz, upper - 0.01f, upper + lash, 0.025f) * edge;
        onLash = MathF.Max(onLash, (male ? 0.5f : 0.9f) * Line(dz - (0.05f + 0.35f * (u - 0.9f)), 0.035f, 0.03f) * Band(u, 0.88f, 1.16f, 0.04f));
        color = Mix(color, new Vector3(0.09f, 0.07f, 0.06f), onLash * face);

        // Lid skin up to the crease, which follows the lid but rounder, then the shadow under the brow bone.
        var crease = (male ? 0.30f : 0.40f) + (male ? 0.54f : 0.60f) * MathF.Pow(spanUp, 0.5f);
        var creaseSpan = MathF.Pow(MathF.Max(0f, 1f - 0.8f * uUp * uUp), 0.5f);
        color = Mix(color, color * new Vector3(1.02f, 0.94f, 0.93f), 0.5f * face * Band(dz, upper + lash, crease, 0.05f) * edge);
        color *= 1f - 0.22f * face * Line(dz - crease, 0.04f, 0.06f) * creaseSpan;
        color *= 1f - 0.08f * face * Band(dz, crease, crease + 0.4f, 0.15f) * creaseSpan;

        // Lower lid: a light wet rim, a faint lash line and a soft shadow underneath.
        color = Mix(color, color * new Vector3(1.08f, 0.95f, 0.93f) + new Vector3(0.03f), 0.6f * face * Band(dz, lower - 0.08f, lower, 0.02f) * inside);
        color *= 1f - 0.25f * face * Line(dz - (lower - 0.1f), 0.025f, 0.025f) * Saturate((u + 0.6f) / 1.2f) * inside;
        color *= 1f - 0.08f * face * Blob((u - 0.1f) / 1.1f, (dz - lower + 0.45f) / 0.35f);

        // Corners.
        color *= 1f - 0.20f * face * Blob((u - 1.0f) / 0.12f, dz / 0.1f);
        color *= 1f - 0.15f * face * Blob((u + 1.05f) / 0.15f, dz / 0.2f);
        return color;
    }

    /// <summary>
    /// One eyebrow: a blunt, sparse head above the inner corner of the eye, a dense body rising to the arch
    /// above the outer edge of the iris, and a tail that thins to a point past the outer corner. The lower
    /// edge is crisp, the upper edge soft, and the hairs point up at the head and out along the rest.
    /// </summary>
    private Vector3 Eyebrow(Vector3 color, float ax, float fz, float browZ, float eyeX, float face, bool male, Vector3 p)
    {
        var inner = eyeX - 1.85f;
        var outer = eyeX + 1.85f;
        color *= 1f + 0.05f * face * Blob((ax - eyeX - 0.6f) / 1.6f, (fz - browZ + 0.75f) / 0.4f);   // brow bone

        var v = (ax - inner) / (outer - inner);
        if (v < -0.12f || v > 1.1f) return color;
        var vs = Saturate(v);
        var peak = male ? 0.66f : 0.62f;
        var arch = male ? 0.32f : 0.55f;
        var drop = male ? 0.95f : 1.15f;
        var rise = vs < peak ? MathF.Sin(MathF.PI / 2f * vs / peak) : 1f - drop * MathF.Pow((vs - peak) / (1f - peak), 1.6f);
        var centre = browZ - (male ? 0.2f : 0.18f) + arch * rise;
        var half = (male ? 0.42f : 0.22f) * (1f - 0.15f * vs) * (1f - 0.8f * SmoothStep(peak - 0.1f, 1f, vs));

        // Hair direction: up at the head, along the brow in the body, down and out in the tail.
        var angle = v < 0.25f ? Lerp(75f, 20f, SmoothStep(0f, 0.25f, v)) : Lerp(20f, -18f, SmoothStep(peak, 1f, v));
        var (sin, cos) = MathF.SinCos(angle * MathF.PI / 180f);
        var across = -ax * sin + fz * cos;
        var along = ax * cos + fz * sin;
        var strokes = Noise.Value(new Vector3(across * 14f, along * 3f, p.X > 0f ? 7.3f : 2.1f));

        var lo = centre - half;
        var hi = centre + half + 0.06f * (strokes - 0.5f);
        var mask = SmoothStep(lo - 0.04f, lo + 0.04f, fz) * (1f - SmoothStep(hi - 0.1f, hi + 0.08f, fz));
        mask *= SmoothStep(-0.1f, 0.06f, v) * (1f - SmoothStep(0.93f, 1.05f, v));
        var density = Lerp(0.5f, 0.95f, SmoothStep(0f, 0.3f, vs)) * (1f - 0.15f * SmoothStep(0.7f, 1f, vs));
        var alpha = mask * density * (0.65f + 0.35f * SmoothStep(0.35f, 0.7f, strokes));
        var brow = Mix(_hair * 0.7f, new Vector3(0.08f, 0.06f, 0.05f), 0.15f);
        return Mix(color, brow, alpha * face);
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
            case LowPolyCharGen.FacialHair.BushyBeard:
            {
                // Full and thick, up onto the cheeks; the beard mesh carries the bulk.
                var cheeks = (1f - SmoothStep(jawTop + 2.6f, jawTop + 3.4f, fz)) * (1f - SmoothStep(98f, 106f, angle));
                return MathF.Max(MathF.Max(moustache * 1.1f, corners), MathF.Max(jaw, cheeks));
            }
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

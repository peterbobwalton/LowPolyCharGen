using System.Numerics;
using LowPolyCharGen.Texturing;

namespace LowPolyCharGen.Kit;

/// <summary>
/// A pack character's own face texture: the pack's face tile, upscaled, recoloured for skin and hair and
/// repainted for gender, eyes, brows, lips and facial hair, in the pack's painted style.
/// </summary>
/// <remarks>
/// Each pack head's face triangles (UVs in the face tiles along the top of the pack texture) are moved
/// to this texture, which keeps the tile's layout at twice its resolution. Painting works through the
/// 3D position under every texel on the pack's face, which all heads share: features are drawn as
/// shapes seen from the front (u = |x| because the two halves share the tile, z = height, in cm).
/// The strip on the right holds hair for the hair pieces (ponytail, bun).
/// </remarks>
public sealed class FacePainter
{
    public const int Size = 512;
    /// <summary>Left edge (texture u) of the hair strip.</summary>
    public const float HairStripU = 0.8f;

    private static readonly Rgb DefaultSkin = Rgb.FromHex("#D9A274");
    private static readonly Rgb DefaultHair = Rgb.FromHex("#4A3222");

    private readonly float _x0, _y0, _scale;   // pack pixels -> face texels
    private readonly int _packSize;

    public TextureImage Texture { get; }

    public static bool IsFaceUv(Vector2 uv)
    {
        var u = uv.X - MathF.Floor(uv.X);
        var v = uv.Y - MathF.Floor(uv.Y);
        return v > 0.86f && u < 0.5f;
    }

    /// <summary>The face texture's UV for a pack UV inside the face tile.</summary>
    public Vector2 Remap(Vector2 uv)
    {
        var px = (uv.X - MathF.Floor(uv.X)) * _packSize;
        var py = (1 - (uv.Y - MathF.Floor(uv.Y))) * _packSize;
        return new Vector2((px - _x0) * _scale / Size, 1 - (py - _y0) * _scale / Size);
    }

    public FacePainter(CharacterSpec spec, PackPart head, TextureImage pack)
    {
        _packSize = pack.Size;
        var tris = head.Triangles.Where(t => t.All(i => IsFaceUv(head.Uvs[i]))).ToList();
        if (tris.Count == 0) throw new InvalidDataException($"{head.Name} has no face triangles");
        Vector2 Px(int i)
        {
            var uv = head.Uvs[i];
            return new Vector2((uv.X - MathF.Floor(uv.X)) * _packSize, (1 - (uv.Y - MathF.Floor(uv.Y))) * _packSize);
        }
        float minX = float.MaxValue, minY = float.MaxValue, maxX = 0, maxY = 0;
        foreach (var t in tris)
            foreach (var i in t)
            {
                var p = Px(i);
                minX = MathF.Min(minX, p.X); minY = MathF.Min(minY, p.Y);
                maxX = MathF.Max(maxX, p.X); maxY = MathF.Max(maxY, p.Y);
            }
        _x0 = minX - 3; _y0 = minY - 3;
        _scale = MathF.Min(Size * HairStripU / (maxX - minX + 6), Size / (maxY - minY + 6));

        // Base: the pack tile, bilinear.
        var color = new Vector3[Size * Size];
        for (var j = 0; j < Size; j++)
            for (var i = 0; i < Size; i++)
                color[j * Size + i] = Sample(pack, _x0 + (i + 0.5f) / _scale, _y0 + (j + 0.5f) / _scale);

        // The 3D point under each texel (on the pack's own face), grown a few texels past the edges.
        var pos = new Vector3[Size * Size];
        var has = new bool[Size * Size];
        foreach (var t in tris)
        {
            Vector2 a = (Px(t[0]) - new Vector2(_x0, _y0)) * _scale, b = (Px(t[1]) - new Vector2(_x0, _y0)) * _scale, c = (Px(t[2]) - new Vector2(_x0, _y0)) * _scale;
            var den = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (MathF.Abs(den) < 1e-9f) continue;
            int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)))), x1 = Math.Min(Size - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
            int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)))), y1 = Math.Min(Size - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    float gx = x + 0.5f, gy = y + 0.5f;
                    var w0 = ((b.Y - c.Y) * (gx - c.X) + (c.X - b.X) * (gy - c.Y)) / den;
                    var w1 = ((c.Y - a.Y) * (gx - c.X) + (a.X - c.X) * (gy - c.Y)) / den;
                    var w2 = 1 - w0 - w1;
                    if (w0 < -0.02f || w1 < -0.02f || w2 < -0.02f) continue;
                    pos[y * Size + x] = head.Positions[t[0]] * w0 + head.Positions[t[1]] * w1 + head.Positions[t[2]] * w2;
                    has[y * Size + x] = true;
                }
        }
        var inside = (bool[])has.Clone();
        for (var pass = 0; pass < 6; pass++)
        {
            var grown = (bool[])has.Clone();
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < Size; x++)
                {
                    if (has[y * Size + x]) continue;
                    var sum = Vector3.Zero; var n = 0;
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= Size || yy >= Size || !has[yy * Size + xx]) continue;
                            sum += pos[yy * Size + xx]; n++;
                        }
                    if (n > 0) { pos[y * Size + x] = sum / n; grown[y * Size + x] = true; }
                }
            has = grown;
        }

        var paint = new Paint(spec);
        for (var k = 0; k < color.Length; k++)
            if (has[k]) color[k] = paint.Texel(color[k], pos[k]);
        Pad(color, inside, 24);
        paint.HairStrip(color);

        var rgb = new byte[Size * Size * 3];
        for (var k = 0; k < color.Length; k++)
        {
            rgb[k * 3] = (byte)Math.Clamp((int)MathF.Round(color[k].X * 255), 0, 255);
            rgb[k * 3 + 1] = (byte)Math.Clamp((int)MathF.Round(color[k].Y * 255), 0, 255);
            rgb[k * 3 + 2] = (byte)Math.Clamp((int)MathF.Round(color[k].Z * 255), 0, 255);
        }
        Texture = new TextureImage(Size, rgb);
    }

    /// <summary>
    /// Fills the texels no face triangle covers with the colour of the covered ones next to them, so
    /// the gaps between the tile's pieces (dark in the pack texture) cannot bleed in at lower mips.
    /// </summary>
    private static void Pad(Vector3[] color, bool[] covered, int passes)
    {
        var done = (bool[])covered.Clone();
        for (var pass = 0; pass < passes; pass++)
        {
            var next = (bool[])done.Clone();
            for (var y = 0; y < Size; y++)
                for (var x = 0; x < (int)(HairStripU * Size); x++)
                {
                    if (done[y * Size + x]) continue;
                    var sum = Vector3.Zero; var n = 0;
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= Size || yy >= Size || !done[yy * Size + xx]) continue;
                            sum += color[yy * Size + xx]; n++;
                        }
                    if (n > 0) { color[y * Size + x] = sum / n; next[y * Size + x] = true; }
                }
            done = next;
        }
    }

    private static Vector3 Sample(TextureImage t, float x, float y)
    {
        x = Math.Clamp(x - 0.5f, 0, t.Size - 1.001f);
        y = Math.Clamp(y - 0.5f, 0, t.Size - 1.001f);
        int ix = (int)x, iy = (int)y;
        float fx = x - ix, fy = y - iy;
        Vector3 P(int px, int py)
        {
            var o = (py * t.Size + px) * t.Channels;
            return new Vector3(t.Rgb[o], t.Rgb[o + 1], t.Rgb[o + 2]) / 255f;
        }
        return Vector3.Lerp(Vector3.Lerp(P(ix, iy), P(ix + 1, iy), fx), Vector3.Lerp(P(ix, iy + 1), P(ix + 1, iy + 1), fx), fy);
    }

    internal static Vector3 ToVec(Rgb c) => new(c.R / 255f, c.G / 255f, c.B / 255f);

    /// <summary>The paint applied to each texel, from the options.</summary>
    private sealed class Paint
    {
        private readonly CharacterSpec _spec;
        private readonly bool _female;
        private readonly Vector3 _skinRatio, _hairRatio, _skin, _hair, _eye;

        // The pack's skin, measured on its face tile (cheek, lit).
        private static readonly Vector3 PackSkin = new(0.918f, 0.635f, 0.380f);

        public Paint(CharacterSpec spec)
        {
            _spec = spec;
            _female = spec.Gender == Gender.Female;
            _skinRatio = ToVec(spec.SkinColor) / ToVec(DefaultSkin);
            _hairRatio = ToVec(spec.HairColor) / ToVec(DefaultHair);
            _skin = PackSkin * _skinRatio;
            _hair = ToVec(spec.HairColor);
            _eye = ToVec(spec.EyeColor);
        }

        private static float S(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3 - 2 * t);
        }

        private static Vector3 Mix(Vector3 a, Vector3 b, float k) => Vector3.Lerp(a, b, Math.Clamp(k, 0f, 1f));

        private static float Lum(Vector3 c) => 0.3f * c.X + 0.59f * c.Y + 0.11f * c.Z;

        private static float Ellipse(float u, float z, float cu, float cz, float ru, float rz, float soft)
        {
            var d = MathF.Sqrt(((u - cu) / ru) * ((u - cu) / ru) + ((z - cz) / rz) * ((z - cz) / rz));
            return S((1 - d) / soft);
        }

        /// <summary>A soft line along the points, its width (cm) varying from point to point.</summary>
        private static float Stroke(float u, float z, ReadOnlySpan<Vector3> pts, float soft)
        {
            float best = float.MaxValue, width = 0;
            for (var i = 0; i + 1 < pts.Length; i++)
            {
                Vector2 a = new(pts[i].X, pts[i].Y), b = new(pts[i + 1].X, pts[i + 1].Y), d = b - a;
                var t = Math.Clamp(Vector2.Dot(new Vector2(u, z) - a, d) / d.LengthSquared(), 0f, 1f);
                var dist = Vector2.Distance(new Vector2(u, z), a + d * t);
                if (dist < best) { best = dist; width = float.Lerp(pts[i].Z, pts[i + 1].Z, t); }
            }
            return S((width / 2 - best) / soft);
        }

        /// <summary>Hair line height (z) round the head, from the brow (front, y = -12) to the nape.</summary>
        private float HairLine(float y, float u)
        {
            var front = _female ? 173.2f : 172.6f;
            var temple = _female ? 166.2f : 167f;
            var h = y < -12 ? front
                : y < -4 ? float.Lerp(front, temple, (y + 12) / 8)
                : y < 2 ? float.Lerp(temple, 165f, (y + 4) / 6)
                : float.Lerp(165f, _female ? 159f : 161f, Math.Clamp((y - 2) / 6, 0, 1));
            return h;
        }

        public Vector3 Texel(Vector3 c, Vector3 p)
        {
            float u = MathF.Abs(p.X), z = p.Z, y = p.Y;
            var front = S((-y - 9f) / 4f);
            // Skin is the pack's orange; straps, balaclavas and helmets are grey and stay as they are.
            var skinness = S((c.X - c.Z - 0.12f) / 0.1f);
            var hairness = S((z - HairLine(y, u)) / 1.2f) * S((c.X - c.Z - 0.04f) / 0.06f);

            // Skin tone and hair colour, keeping the painted shading.
            c *= Vector3.Lerp(Vector3.One, _skinRatio, skinness * (1 - hairness));
            if (_spec.Hair == HairStyle.Bald)
                c = Mix(c, _skin * (0.9f - 0.08f * S((y + 4) / 8)), hairness);
            else if (_female)
            {
                // Smooth, pulled back hair: the colour, shaded by the pack's own hair shading, with strands.
                var shade = Math.Clamp(Lum(c) / 0.3f, 0.65f, 1.25f);
                var strands = 0.92f + 0.08f * MathF.Sin(p.X * 5.1f + z * 0.7f) * MathF.Sin(y * 3.3f);
                c = Mix(c, _hair * shade * strands, hairness);
            }
            else
            {
                // The colour, shaded by the pack's own hair shading (a ratio would blow light colours out).
                var shade = Math.Clamp(Lum(c) / 0.26f, 0.55f, 1.2f);
                c = Mix(c, _hair * shade, hairness);
            }

            var k = front * skinness * (1 - hairness);
            if (k <= 0.001f) return Vector3.Clamp(c, Vector3.Zero, Vector3.One);

            if (_female) Female(ref c, u, z, k);
            else Male(ref c, u, z, k);
            Eyes(ref c, u, z, k);
            return Vector3.Clamp(c, Vector3.Zero, Vector3.One);
        }

        private void Female(ref Vector3 c, float u, float z, float k)
        {
            // The pack's heavy male brow and socket shadow, nostril and jaw shading, softened.
            c = Mix(c, _skin * 0.97f, Ellipse(u, z, 4.2f, 167.6f, 4.6f, 2.4f, 0.5f) * k * 0.85f);
            c = Mix(c, _skin * 0.95f, Ellipse(u, z, 2.2f, 163.2f, 1.9f, 3.6f, 0.7f) * k * 0.85f);   // shadow down the nose sides
            c = Mix(c, _skin, Ellipse(u, z, 1.4f, 160.6f, 2.4f, 0.7f, 0.6f) * k * 0.55f);
            c = Mix(c, _skin, Ellipse(u, z, 5f, 152.5f, 6f, 2f, 0.6f) * k * 0.45f);
            // Thin arched brows.
            c = Mix(c, _hair * 1.1f, Stroke(u, z, [new(1.5f, 168f, 0.35f), new(3.2f, 168.9f, 0.5f), new(4.6f, 169.1f, 0.4f), new(6.3f, 168.4f, 0.15f)], 0.12f) * k);
            // Lips.
            const float mz = 156.7f;
            var up = z > mz && u < 2.6f ? S(((0.55f * (1 - u * u / 6.76f) + 0.12f * MathF.Exp(-((u - 0.9f) / 0.5f) * ((u - 0.9f) / 0.5f))) - (z - mz)) / 0.1f) : 0;
            var lo = z <= mz && u < 2.3f ? S((0.75f * (1 - u * u / 5.29f) - (mz - z)) / 0.12f) : 0;
            var lip = _skin * new Vector3(0.82f, 0.55f, 0.6f) + new Vector3(0.06f, 0, 0);
            c = Mix(c, lip * 0.9f, up * k * 0.85f);
            c = Mix(c, lip, lo * k * 0.85f);
            c = Mix(c, lip * 1.15f, Ellipse(u, z, 0.6f, mz - 0.4f, 0.9f, 0.25f, 0.6f) * k * 0.5f);
            c = Mix(c, lip * 0.35f, Stroke(u, z, [new(0, mz, 0.12f), new(1.4f, mz + 0.05f, 0.12f), new(2.6f, mz + 0.15f, 0.04f)], 0.08f) * k);
            // A little colour on the cheeks.
            c = Mix(c, _skin * new Vector3(1f, 0.72f, 0.75f), Ellipse(u, z, 5.5f, 159.5f, 2.4f, 1.6f, 0.9f) * k * 0.25f);
        }

        private void Male(ref Vector3 c, float u, float z, float k)
        {
            // Lighter eye sockets, so the eyes read; the brow keeps its weight.
            c = Mix(c, _skin * 0.9f, Ellipse(u, z, 4.1f, 166.4f, 2.6f, 1.3f, 0.6f) * k * 0.55f);

            var facial = _spec.FacialHair;
            if (facial == FacialHair.None) return;
            var beardColor = _hair * 0.85f;
            var noise = 0.75f + 0.25f * MathF.Sin(u * 37.1f + z * 11.3f) * MathF.Sin(u * 13.7f - z * 29.9f);
            var lips = Ellipse(u, z, 0, 156.7f, 2.5f, 0.75f, 0.4f);
            var jawArea = S((158.2f - z) / 0.6f) * S((z - 149.6f) / 0.6f) * (1 - lips);
            var moustache = Stroke(u, z, [new(0, 158f, 0.9f), new(2.2f, 157.8f, 0.8f), new(3f, 156.6f, 0.45f)], 0.15f);
            var goatee = Ellipse(u, z, 0, 152.6f, 2.4f, 2.6f, 0.4f) * (1 - lips);
            switch (facial)
            {
                case FacialHair.Stubble:
                    c = Mix(c, beardColor, (jawArea * 0.32f + moustache * 0.1f) * noise * k);
                    break;
                case FacialHair.Moustache:
                    c = Mix(c, beardColor, moustache * 0.92f * k);
                    break;
                case FacialHair.Goatee:
                    c = Mix(c, beardColor, MathF.Max(moustache, goatee) * 0.9f * noise * k);
                    break;
                case FacialHair.Beard:
                case FacialHair.BushyBeard:
                    var full = MathF.Max(jawArea, moustache) * (facial == FacialHair.BushyBeard ? 1f : 0.88f);
                    c = Mix(c, beardColor * (facial == FacialHair.BushyBeard ? 0.85f : 1f), full * (0.85f + 0.15f * noise) * k);
                    break;
            }
        }

        private void Eyes(ref Vector3 c, float u, float z, float k)
        {
            // An almond eye: white, iris and pupil, a heavy upper lid line (winged on a woman).
            float ex = 4.0f, ez = 166.4f, ew = _female ? 2.1f : 1.85f, eh = _female ? 0.9f : 0.72f;
            var t = (u - ex) / ew;
            var lens = MathF.Abs(t) < 1 ? S((eh * (1 - t * t) - MathF.Abs(z - ez + 0.1f * t)) / 0.12f) : 0;
            var white = _female ? new Vector3(0.93f, 0.88f, 0.82f) : new Vector3(0.82f, 0.76f, 0.7f);
            c = Mix(c, white, lens * k);
            var iris = Ellipse(u, z, ex - 0.2f, ez - 0.05f, 0.75f, 0.8f, 0.15f) * lens;
            c = Mix(c, _eye, iris * k);
            c = Mix(c, new Vector3(0.05f, 0.03f, 0.02f), Ellipse(u, z, ex - 0.2f, ez - 0.05f, 0.35f, 0.38f, 0.2f) * lens * k);
            c = Mix(c, Vector3.One, Ellipse(u, z, ex - 0.45f, ez + 0.3f, 0.16f, 0.16f, 0.4f) * lens * k);
            var wing = _female ? 0.35f : 0.05f;
            c = Mix(c, new Vector3(0.08f, 0.05f, 0.04f), Stroke(u, z,
                [new(ex - ew - 0.1f, ez + 0.05f, 0.15f), new(ex - ew * 0.5f, ez + eh * 0.8f, 0.3f), new(ex, ez + eh * 0.95f, _female ? 0.35f : 0.3f),
                 new(ex + ew * 0.6f, ez + eh * 0.7f, 0.3f), new(ex + ew + wing, ez + (_female ? 0.45f : 0.1f), 0.08f)], 0.08f) * k);
            c = Mix(c, _skin * 0.75f, Stroke(u, z,
                [new(ex - ew * 0.6f, ez - eh * 0.65f, 0.12f), new(ex, ez - eh * 0.95f, 0.12f), new(ex + ew * 0.8f, ez - eh * 0.5f, 0.12f)], 0.1f) * k * 0.6f);
        }

        /// <summary>Hair for the hair pieces: the colour with strands running down (v).</summary>
        public void HairStrip(Vector3[] color)
        {
            var x0 = (int)(HairStripU * Size) + 2;
            for (var y = 0; y < Size; y++)
                for (var x = x0; x < Size; x++)
                {
                    var s = 0.82f + 0.12f * MathF.Sin(x * 0.9f) * MathF.Sin(x * 0.37f + 1.3f) + 0.06f * MathF.Sin(y * 0.05f + x * 0.3f);
                    color[y * Size + x] = _hair * s;
                }
        }
    }
}

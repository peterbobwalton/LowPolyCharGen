using System.Numerics;
using LowPolyCharGen.Texturing;

namespace LowPolyCharGen.Kit;

/// <summary>
/// A character's own clothes texture (material slot 5): the body's and trousers' triangles get unique texels, laid out
/// as box-projected charts, painted over the pack's fabric shading. The pack's UVs mirror left and right and share
/// texels between garments, so they can't carry a tie on one side or a stain on one sleeve. Everything is drawn by 3D
/// position on the unmorphed part (+X = left, -Y = front, +Z = up, cm), as <see cref="FacePainter"/> does on the face:
/// outfits (shirts, suits, airline uniforms, flight suits), short sleeves and bare legs, dirt, blood, tears, bandages
/// and insignia. Boots and hands keep the pack texture.
/// </summary>
public sealed class GarmentPainter
{
    public const int Size = 1024;

    /// <summary>A body (group 1) or trousers (group 2) triangle: its position on the part and its pack texels.</summary>
    public sealed record Tri(int Face, int Group, Vector3[] P, Vector2[] Uv, TextureImage Texture);

    public TextureImage Texture { get; }
    private readonly Dictionary<int, Vector2[]> _uvs = [];

    /// <summary>True if the character needs a clothes texture at all.</summary>
    public static bool Applies(CharacterSpec spec) =>
        spec.Outfit != Outfit.None || spec.Injury != Injury.None || spec.Grime > 0 || spec.Insignia != Insignia.None || spec.Legs is LegsStyle.Skirt or LegsStyle.Shorts;

    /// <summary>The garment's UVs for a face, in corner order.</summary>
    public Vector2[] Uvs(int face) => _uvs[face];

    /// <param name="groupColor">Recolour per group (1 top, 2 trousers), as the kit material does it; null keeps the pack's colours.</param>
    public GarmentPainter(CharacterSpec spec, IReadOnlyList<Tri> tris, Func<int, Rgb?> groupColor)
    {
        // Charts: one per limb and box face.
        var charts = new Dictionary<(int Limb, int Axis), List<Tri>>();
        foreach (var t in tris)
        {
            var key = (Limb(t), Axis(t));
            if (!charts.TryGetValue(key, out var list)) charts[key] = list = [];
            list.Add(t);
        }
        var layouts = charts.Select(c => new Chart(c.Key.Axis, c.Value)).OrderByDescending(c => c.Height).ToList();
        var scale = 10f;
        while (!Pack(layouts, scale)) scale *= 0.94f;

        var mean = new float[3];
        for (var group = 1; group <= 2; group++)
        {
            var samples = tris.Where(t => t.Group == group).SelectMany(t => KitBuilder.ClothLuminance(t.Texture, t.Uv)).ToList();
            mean[group] = samples.Count > 0 ? MathF.Max(samples.Average(), 0.01f) : 0.1f;
        }
        var paint = new Paint(spec, groupColor, mean);

        var color = new Vector3[Size * Size];
        var has = new bool[Size * Size];
        foreach (var chart in layouts)
            foreach (var t in chart.Tris)
            {
                var a = chart.Texel(t.P[0], scale); var b = chart.Texel(t.P[1], scale); var c = chart.Texel(t.P[2], scale);
                _uvs[t.Face] = [new(a.X / Size, 1 - a.Y / Size), new(b.X / Size, 1 - b.Y / Size), new(c.X / Size, 1 - c.Y / Size)];
                var den = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
                if (MathF.Abs(den) < 1e-9f) continue;
                int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))) - 1), x1 = Math.Min(Size - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))) + 1);
                int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))) - 1), y1 = Math.Min(Size - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))) + 1);
                for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                    {
                        float gx = x + 0.5f, gy = y + 0.5f;
                        var w0 = ((b.Y - c.Y) * (gx - c.X) + (c.X - b.X) * (gy - c.Y)) / den;
                        var w1 = ((c.Y - a.Y) * (gx - c.X) + (a.X - c.X) * (gy - c.Y)) / den;
                        var w2 = 1 - w0 - w1;
                        // A little past the edges, so the seams between the triangles can't show.
                        const float margin = -0.04f;
                        if (w0 < margin || w1 < margin || w2 < margin) continue;
                        var p = t.P[0] * w0 + t.P[1] * w1 + t.P[2] * w2;
                        var uv = t.Uv[0] * w0 + t.Uv[1] * w1 + t.Uv[2] * w2;
                        var px = (uv.X - MathF.Floor(uv.X)) * t.Texture.Size;
                        var py = (1 - (uv.Y - MathF.Floor(uv.Y))) * t.Texture.Size;
                        color[y * Size + x] = paint.Texel(FacePainter.Sample(t.Texture, px, py), p, t.Group, chart.Axis);
                        has[y * Size + x] = true;
                    }
            }
        Pad(color, has, 6);

        var rgb = new byte[Size * Size * 3];
        for (var k = 0; k < color.Length; k++)
        {
            rgb[k * 3] = (byte)Math.Clamp((int)MathF.Round(color[k].X * 255), 0, 255);
            rgb[k * 3 + 1] = (byte)Math.Clamp((int)MathF.Round(color[k].Y * 255), 0, 255);
            rgb[k * 3 + 2] = (byte)Math.Clamp((int)MathF.Round(color[k].Z * 255), 0, 255);
        }
        Texture = new TextureImage(Size, rgb);
    }

    private static int Limb(Tri t)
    {
        var c = (t.P[0] + t.P[1] + t.P[2]) / 3;
        if (t.Group == 2) return c.X < 0 ? 3 : 4;
        return MathF.Abs(c.X) < 25f ? 0 : c.X < 0 ? 1 : 2;
    }

    /// <summary>The box face a triangle projects onto: 0/1 = -X/+X, 2/3 = -Y/+Y, 4/5 = -Z/+Z.</summary>
    private static int Axis(Tri t)
    {
        var n = Vector3.Cross(t.P[1] - t.P[0], t.P[2] - t.P[0]);
        float ax = MathF.Abs(n.X), ay = MathF.Abs(n.Y), az = MathF.Abs(n.Z);
        if (ax >= ay && ax >= az) return n.X < 0 ? 0 : 1;
        if (ay >= az) return n.Y < 0 ? 2 : 3;
        return n.Z < 0 ? 4 : 5;
    }

    private sealed class Chart
    {
        public int Axis { get; }
        public List<Tri> Tris { get; }
        public float Width { get; }
        public float Height { get; }
        public int X { get; set; }
        public int Y { get; set; }
        private readonly float _u0, _v0;

        public Chart(int axis, List<Tri> tris)
        {
            Axis = axis;
            Tris = tris;
            var points = tris.SelectMany(t => t.P).Select(Project).ToList();
            _u0 = points.Min(p => p.X); _v0 = points.Min(p => p.Y);
            Width = points.Max(p => p.X) - _u0;
            Height = points.Max(p => p.Y) - _v0;
        }

        private Vector2 Project(Vector3 p) => Axis switch
        {
            0 or 1 => new Vector2(p.Y, -p.Z),
            2 or 3 => new Vector2(p.X, -p.Z),
            _ => new Vector2(p.X, p.Y),
        };

        public Vector2 Texel(Vector3 p, float scale)
        {
            var q = Project(p);
            return new Vector2(X + (q.X - _u0) * scale, Y + (q.Y - _v0) * scale);
        }
    }

    /// <summary>Shelf packing, tallest first; false if the charts don't fit at this scale.</summary>
    private static bool Pack(List<Chart> charts, float scale)
    {
        const int gap = 8;
        int x = gap, y = gap, shelf = 0;
        foreach (var c in charts)
        {
            int w = (int)MathF.Ceiling(c.Width * scale), h = (int)MathF.Ceiling(c.Height * scale);
            if (x + w + gap > Size) { x = gap; y += shelf + gap; shelf = 0; }
            if (x + w + gap > Size || y + h + gap > Size) return false;
            c.X = x; c.Y = y;
            x += w + gap;
            shelf = Math.Max(shelf, h);
        }
        return true;
    }

    /// <summary>Grows the painted texels outwards, so nothing dark bleeds in at lower mips.</summary>
    private static void Pad(Vector3[] color, bool[] has, int passes)
    {
        for (var pass = 0; pass < passes; pass++)
        {
            var next = (bool[])has.Clone();
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
                            sum += color[yy * Size + xx]; n++;
                        }
                    if (n > 0) { color[y * Size + x] = sum / n; next[y * Size + x] = true; }
                }
            Array.Copy(next, has, has.Length);
        }
    }

    /// <summary>Stable across runs (string.GetHashCode isn't): the stains and tears stay where they were.</summary>
    internal static uint Seed(string name)
    {
        var h = 2166136261u;
        foreach (var ch in name) h = (h ^ ch) * 16777619u;
        return h;
    }

    /// <summary>Smooth value noise, 0 to 1.</summary>
    internal static float Noise(Vector3 p)
    {
        static float Hash(int x, int y, int z)
        {
            var h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
        int ix = (int)MathF.Floor(p.X), iy = (int)MathF.Floor(p.Y), iz = (int)MathF.Floor(p.Z);
        var f = p - new Vector3(ix, iy, iz);
        f = f * f * (new Vector3(3) - 2 * f);
        float X(int y, int z) => float.Lerp(Hash(ix, y, z), Hash(ix + 1, y, z), f.X);
        float Y(int z) => float.Lerp(X(iy, z), X(iy + 1, z), f.Y);
        return float.Lerp(Y(iz), Y(iz + 1), f.Z);
    }

    /// <summary>Two octaves of <see cref="Noise"/>.</summary>
    internal static float Blotches(Vector3 p, float seed) => 0.65f * Noise(p * 0.11f + new Vector3(seed)) + 0.35f * Noise(p * 0.31f + new Vector3(seed * 1.7f));

    private sealed class Paint
    {
        private static readonly Vector3 White = new(0.92f, 0.92f, 0.9f);
        private static readonly Vector3 Gold = new(0.8f, 0.62f, 0.22f);
        private static readonly Vector3 Reflective = new(0.8f, 0.82f, 0.78f);
        private static readonly Vector3 Black = new(0.05f, 0.05f, 0.055f);
        private static readonly Vector3 PackSkin = new(0.918f, 0.635f, 0.380f);

        private readonly CharacterSpec _spec;
        private readonly Func<int, Rgb?> _groupColor;
        private readonly float[] _mean;
        private readonly Vector3 _skin, _skinRatio, _shirt, _trousers, _accent, _gear;
        private readonly float _seed;
        private readonly bool _bandageLeft;
        // Where the stains and tears fall on this character: (position, radius).
        private readonly (Vector3 At, float R)[] _stains, _tears;

        public Paint(CharacterSpec spec, Func<int, Rgb?> groupColor, float[] mean)
        {
            _spec = spec;
            _groupColor = groupColor;
            _mean = mean;
            _skinRatio = FacePainter.ToVec(spec.SkinColor) / FacePainter.ToVec(Rgb.FromHex("#D9A274"));
            _skin = PackSkin * _skinRatio;
            _shirt = FacePainter.ToVec(spec.ShirtColor);
            _trousers = FacePainter.ToVec(spec.TrousersColor);
            _accent = FacePainter.ToVec(spec.AccentColor);
            _gear = FacePainter.ToVec(spec.GearColor);
            var seed = Seed(spec.Name);
            _seed = seed % 97u;
            var rng = new Random((int)seed);
            float Side() => rng.Next(2) == 0 ? -1 : 1;
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            _bandageLeft = rng.Next(2) == 0;
            _stains =
            [
                (new Vector3(Range(-14, 14), -15, Range(108, 134)), Range(4, 7)),
                (new Vector3(Side() * Range(40, 70), 0, 135), Range(3, 6)),
                (new Vector3(Side() * Range(8, 20), -10, Range(55, 85)), Range(3.5f, 6)),
            ];
            _tears =
            [
                (new Vector3(Side() * Range(60, 74), 0, 133), Range(3.5f, 5)),
                (new Vector3(Side() * Range(12, 20), -10, Range(62, 80)), Range(4, 6)),
                (new Vector3(Side() * Range(10, 20), -14, Range(100, 118)), Range(3, 5)),
            ];
        }

        private static float S(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3 - 2 * t);
        }

        private static Vector3 Mix(Vector3 a, Vector3 b, float k) => Vector3.Lerp(a, b, Math.Clamp(k, 0f, 1f));

        private static Vector3 Lin(Vector3 c) => new(MathF.Pow(c.X, 2.2f), MathF.Pow(c.Y, 2.2f), MathF.Pow(c.Z, 2.2f));

        private static Vector3 Srgb(Vector3 c) => new(MathF.Pow(Math.Clamp(c.X, 0, 1), 1 / 2.2f), MathF.Pow(Math.Clamp(c.Y, 0, 1), 1 / 2.2f), MathF.Pow(Math.Clamp(c.Z, 0, 1), 1 / 2.2f));

        private const float Soft = 0.18f;

        /// <summary>1 inside the band of half width <paramref name="half"/> round <paramref name="center"/>, soft at the edge.</summary>
        private static float Band(float v, float center, float half) => S((half - MathF.Abs(v - center)) / Soft);

        private static float Rect(float u, float z, float u0, float u1, float z0, float z1) =>
            S((u - u0) / Soft) * S((u1 - u) / Soft) * S((z - z0) / Soft) * S((z1 - z) / Soft);

        /// <summary>A rectangle's outline.</summary>
        private static float Outline(float u, float z, float u0, float u1, float z0, float z1, float w = 0.3f) =>
            Rect(u, z, u0 - w, u1 + w, z0 - w, z1 + w) * (1 - Rect(u, z, u0 + w, u1 - w, z0 + w, z1 - w));

        private static float Dot(float u, float z, float cu, float cz, float r) =>
            S((r - MathF.Sqrt((u - cu) * (u - cu) + (z - cz) * (z - cz))) / Soft);

        /// <summary>A soft line between two points.</summary>
        private static float Line(float u, float z, float u0, float z0, float u1, float z1, float w)
        {
            Vector2 a = new(u0, z0), d = new Vector2(u1, z1) - a, q = new(u, z);
            var t = Math.Clamp(Vector2.Dot(q - a, d) / d.LengthSquared(), 0f, 1f);
            return S((w / 2 - Vector2.Distance(q, a + d * t)) / Soft);
        }

        public Vector3 Texel(Vector3 c, Vector3 p, int group, int axis)
        {
            // The pack's cloth shading: its luminance over the garment's mean, as the kit material recolours.
            var linear = Lin(c);
            var chroma = (linear.X - linear.Y) / (linear.X + 0.02f);
            var skinMask = S((chroma - 0.35f) / 0.15f) * S((linear.X - 0.1f) / 0.1f);
            var lum = Vector3.Dot(linear, new Vector3(0.3f, 0.59f, 0.11f));
            var shade = float.Lerp(MathF.Pow(Math.Clamp(lum / _mean[group], 0.25f, 1.9f), 1 / 2.2f), 1f, skinMask);

            var result = Mix(c, Vector3.Clamp(c * _skinRatio, Vector3.Zero, Vector3.One), skinMask);
            if (_groupColor(group) is { } recolour)
                result = Mix(FacePainter.ToVec(recolour) * shade, result, skinMask);
            result = group == 1 ? Top(result, shade, skinMask, p) : Bottom(result, shade, p);
            if (_spec.Insignia != Insignia.None) result = Badge(result, p, axis);
            result = Damage(result, p, group);
            return Vector3.Clamp(result, Vector3.Zero, Vector3.One);
        }

        private Vector3 Top(Vector3 c, float s, float skinMask, Vector3 p)
        {
            var o = _spec.Outfit;
            if (o == Outfit.None) return c;
            float x = p.X, u = MathF.Abs(x), z = p.Z;
            var front = p.Y < 0;
            var arm = u > 25f;
            var skin = _skin * Math.Clamp(s, 0.75f, 1.05f);
            var dark = _shirt * s * 0.55f;
            var t = _shirt * s;

            switch (o)
            {
                case Outfit.TShirt or Outfit.StripedTee:
                    if (o == Outfit.StripedTee) t = Mix(t, _accent * s, Band(MathF.Abs((z % 7f + 7f) % 7f - 3.5f), 0, 1.3f));
                    t = Mix(t, dark, Band(u, 46f, 0.6f) * (arm ? 1 : 0));
                    if (u > 46f) t = skin;
                    t = Crew(t, skin, u, z, front);
                    break;
                case Outfit.Polo:
                    t = Mix(t, _accent * s, Band(u, 45f, 0.9f));
                    if (u > 46f) t = skin;
                    t = Collar(Crew(t, skin, u, z, front), u, z, front, 0.85f);
                    if (front && !arm)
                    {
                        t = Mix(t, _shirt * s * 0.85f, Rect(u, z, -1f, 1.1f, 134f, 146f));
                        t = Mix(t, White, MathF.Max(Dot(u, z, 0, 142f, 0.5f), Dot(u, z, 0, 137f, 0.5f)));
                    }
                    break;
                case Outfit.Shirt or Outfit.CheckShirt or Outfit.RolledShirt or Outfit.FloralShirt or Outfit.Blouse:
                    if (o == Outfit.CheckShirt)
                    {
                        float g(float v) => S((1.1f - MathF.Abs(((v % 6f) + 6f) % 6f - 3f)) / 0.3f);
                        t = Mix(t, _accent * s, MathF.Max(g(x), g(z)) * 0.7f);
                        t = Mix(t, _accent * s * 0.7f, g(x) * g(z));
                    }
                    if (o == Outfit.FloralShirt)
                    {
                        // Big loud flowers: one in each cell of a grid over the cloth, five petals round a dot.
                        const float cell = 9f;
                        float across = arm ? p.Y * 1.4f + x * 0.35f : x;   // across a sleeve, along it the grid runs on
                        float up = arm ? x : z;
                        float cx = MathF.Floor(across / cell), cz = MathF.Floor(up / cell);
                        var jitterX = (GarmentPainter.Noise(new Vector3(cx * 3.1f, cz * 2.3f, 1.5f)) - 0.5f) * 3.4f;
                        var jitterZ = (GarmentPainter.Noise(new Vector3(cx * 1.7f, cz * 4.1f, 7.5f)) - 0.5f) * 3.4f;
                        float dx = across - (cx + 0.5f) * cell - jitterX, dz = up - (cz + 0.5f) * cell - jitterZ;
                        var radius = MathF.Sqrt(dx * dx + dz * dz);
                        var petals = 2.5f * (0.68f + 0.32f * MathF.Cos(5 * MathF.Atan2(dz, dx) + cx * 2f + cz));
                        var flower = (int)(cx + cz * 3) % 2 == 0 ? _accent : _gear;
                        t = Mix(t, flower * s, S((petals - radius) / 0.2f));
                        t = Mix(t, (flower == _accent ? _gear : _accent) * s, S((0.8f - radius) / 0.15f));
                    }
                    if (o == Outfit.RolledShirt)
                    {
                        t = Mix(t, Mix(t, White, 0.3f), Band(u, 59f, 1.8f));
                        if (u > 61f) t = skin;
                    }
                    else if (o == Outfit.FloralShirt)
                    {
                        if (u > 46f) t = skin;
                    }
                    else if (o == Outfit.Blouse)
                    {
                        if (u > 68f) t = skin;
                    }
                    else t = Mix(t, _shirt * s * 0.75f, Band(u, 79f, 0.25f));
                    if (o == Outfit.Blouse)
                    {
                        // An open neck.
                        if (front && u < 1.2f + (z - 133f) * 0.34f && z > 133f) t = skin;
                    }
                    else
                    {
                        t = Crew(t, skin, u, z, front);
                        t = Placket(t, s, u, z, front);
                    }
                    t = Collar(t, u, z, front, 0.88f);
                    break;
                case Outfit.Suit or Outfit.SuitOpenNeck or Outfit.CabinBlazer or Outfit.PilotJacket:
                    if (o is Outfit.CabinBlazer or Outfit.PilotJacket)
                    {
                        // Gold rings round the cuff, one per rank, working up the sleeve.
                        var rings = _spec.Rank > 0 ? _spec.Rank : o == Outfit.PilotJacket ? 4 : 2;
                        for (var i = 0; i < rings; i++) t = Mix(t, Gold, Band(u, 77f - i * 2.1f, 0.5f));
                    }
                    else t = Mix(t, dark, Band(u, 79f, 0.2f));
                    if (!arm) t = Jacket(t, skin, s, x, u, z, front);
                    break;
                case Outfit.CabinShirt:
                    t = Crew(t, skin, u, z, front);
                    t = Placket(t, s, u, z, front);
                    t = Mix(t, _shirt * s * 0.75f, Band(u, 79f, 0.25f));
                    t = Collar(t, u, z, front, 0.88f);
                    t = _spec.Gender == Gender.Male ? Tie(t, s, u, z, front) : front ? Scarf(t, s, u, z) : t;
                    if (front && !arm && x > 0) t = Mix(t, Gold, Wings(u, z, 13f, 134.5f));
                    break;
                case Outfit.HiVisVest:
                    t = Crew(t, skin, u, z, front);
                    t = Mix(t, _shirt * s * 0.75f, Band(u, 79f, 0.25f));
                    t = Collar(t, u, z, front, 0.88f);
                    if (!arm)
                    {
                        // The vest, open at the neck, with reflective bands round the waist and over the shoulders.
                        var open = front ? Opening(z, 147f, 124f, 6.5f) : (z > 145f ? 8f : -1f);
                        if (u >= open && !(z > 148.5f && u < 9f))
                        {
                            t = _gear * s;
                            t = Mix(t, Reflective, MathF.Max(MathF.Max(Band(z, 110f, 1.7f), Band(z, 102f, 1.7f)), Band(u, 11.5f, 1.5f)));
                            if (front) t = Mix(t, _gear * s * 0.6f, Band(u, 0, 0.16f));
                        }
                    }
                    break;
                case Outfit.CabinWaistcoat:
                    if (arm) t = _gear * s;
                    else t = Waistcoat(t, s, u, z, front);
                    break;
                case Outfit.PilotShirt:
                    t = Crew(t, skin, u, z, front);
                    t = Placket(t, s, u, z, front);
                    t = Collar(t, u, z, front, 0.88f);
                    t = Tie(t, s, u, z, front);
                    if (front && !arm)
                    {
                        t = Mix(t, _shirt * s * 0.72f, Outline(u, z, 8.5f, 16.5f, 121f, 131f));
                        t = Mix(t, _shirt * s * 0.72f, Line(u, z, 8.5f, 128f, 16.5f, 128f, 0.45f));
                        if (x > 0) t = Mix(t, Gold, Wings(u, z, 12.5f, 134.5f));
                    }
                    t = Epaulette(t, u, z, p.Y, _spec.Rank > 0 ? _spec.Rank : 4);
                    break;
                case Outfit.FlightSuit:
                    if (front && !arm)
                    {
                        t = Mix(t, Black * 2.5f, Band(u, 0, 0.5f));
                        t = Mix(t, Black * 2.5f, Line(u, z, 7f, 124f, 17f, 131f, 0.5f));
                        if (x < 0) t = Mix(t, _shirt * 0.5f, Rect(u, z, 8f, 17f, 133f, 137f));
                        if (x > 0) t = Mix(t, _accent, Dot(u, z, 13f, 135.5f, 3f));
                    }
                    if (arm && x > 0) t = Mix(t, _shirt * s * 0.65f, Outline(u, p.Y, 35f, 45f, -5f, 5f));
                    t = Mix(t, Black * 2.5f, Band(u, 80f, 0.5f));
                    t = Collar(Crew(t, skin, u, z, front), u, z, front, 0.8f);
                    break;
                case Outfit.RescueSuit:
                    t = Mix(t, Reflective, MathF.Max(arm ? Band(u, 46f, 1.6f) : 0, arm ? 0 : Band(z, 122f, 1.6f)));
                    t = Mix(t, Black * 2f, Band(u, 80f, 1.2f));
                    if (arm) t = Mix(t, Black * 2.2f, Band(u, 58f, 3f) * S((p.Z - 132f) / 2f));
                    if (front && !arm) t = Mix(t, Black * 2.5f, Band(u, 0, 0.55f));
                    t = Crew(t, Black * 2f, u, z, front);
                    break;
            }
            if (_spec.FlightGear.HasFlag(FlightGear.Harness) && !arm) t = Harness(t, x, u, z, front);
            if (_spec.Apron && !arm) t = ApronTop(t, s, u, z, front);
            // The neck stays skin, above whatever covers the chest.
            if (skinMask > 0.5f && z > 149.5f) return Mix(t, skin, skinMask);
            return t;
        }

        /// <summary>A crew neck over the pack's V: skin only above the neckline.</summary>
        private static Vector3 Crew(Vector3 t, Vector3 skin, float u, float z, bool front)
        {
            if (!front) return z > 150f && u < 9f ? skin : t;
            var neckline = 146.2f + u * u * 0.03f;
            return u < 8.5f && z > neckline ? skin : t;
        }

        /// <summary>Collar leaves falling from the neck, in the shirt's colour a little shaded, and a band round the back.</summary>
        private Vector3 Collar(Vector3 t, float u, float z, bool front, float shade)
        {
            var collar = _shirt * shade;
            var edge = _shirt * 0.55f;
            if (_spec.Outfit is Outfit.Suit or Outfit.SuitOpenNeck or Outfit.CabinBlazer) collar = _gear * 0.95f;
            if (front)
            {
                // Two triangles: the neck corner, the point and the shoulder.
                var leaf = S((6.5f + (z - 142.5f) * 0.28f - u) / 0.4f) * S((u - (z - 142.5f) * 0.42f - 0.3f) / 0.4f) * S((149f - z) / 0.4f) * S((z - 142f - u * 0.05f) / 0.4f);
                t = Mix(t, collar, leaf);
                t = Mix(t, edge, Line(u, z, 0.5f, 145.6f, 6.4f, 142.2f, 0.28f));
                return t;
            }
            return Mix(t, collar, Rect(u, z, -1f, 10f, 146f, 151f));
        }

        private Vector3 Placket(Vector3 t, float s, float u, float z, bool front)
        {
            if (!front || u > 25f) return t;
            t = Mix(t, _shirt * s * 0.7f, MathF.Max(Band(u, 1.1f, 0.12f), 0));
            for (var b = 141f; b > 92f; b -= 7.5f) t = Mix(t, White * 0.95f, Dot(u, z, 0, b, 0.42f));
            return t;
        }

        private Vector3 Tie(Vector3 t, float s, float u, float z, bool front)
        {
            if (!front) return t;
            var width = z > 141.5f ? 1.3f + (z - 141.5f) * 0.25f : 1.05f + (141.5f - z) * 0.052f;
            var tie = S((width - u) / Soft) * S((z - 116f) / 0.3f) * S((146.8f - z) / 0.3f);
            // The point at the tip.
            tie *= S(((z - 116f) * 1.1f + 0.4f - u) / 0.2f);
            t = Mix(t, _accent * s, tie);
            return Mix(t, _accent * s * 0.6f, Band(z, 142f, 0.12f) * tie);
        }

        /// <summary>Half width of a jacket's or waistcoat's opening at height z.</summary>
        private static float Opening(float z, float top, float bottom, float wide) => z >= bottom ? float.Lerp(0.4f, wide, Math.Clamp((z - bottom) / (top - bottom), 0f, 1f)) : 0;

        private Vector3 Jacket(Vector3 t, Vector3 skin, float s, float x, float u, float z, bool front)
        {
            if (!front) return Mix(t, _shirt * s * 0.85f, Band(u, 0, 0.14f) * S((130f - z) / 0.3f));
            const float notch = 133f;
            var open = Opening(z, 148f, 116f, 7.2f);
            var collarSkin = u < 6f && z > 146.4f + u * u * 0.03f;
            if (u < open)
            {
                // The shirt under the jacket, and what's at its neck.
                var shirt = _gear * s;
                shirt = Mix(shirt, _gear * s * 0.72f, Band(u, 0, 0.12f));
                if (_spec.Outfit is Outfit.Suit or Outfit.PilotJacket || (_spec.Outfit == Outfit.CabinBlazer && _spec.Gender == Gender.Male))
                    shirt = Tie(shirt, 1, u, z, true);
                else if (_spec.Outfit == Outfit.CabinBlazer) shirt = Scarf(shirt, s, u, z);
                else if (z > 137f && u < (z - 137f) * 0.55f) shirt = skin;
                return collarSkin ? skin : shirt;
            }
            // Lapels with a notch, the closing line, buttons and pockets.
            var lapelWidth = 4.2f * S((z - 116f) / 8f);
            var lapel = S((open + lapelWidth - u) / 0.3f) * S((z - 116f) / 0.3f);
            t = Mix(t, _shirt * s * 0.84f, lapel);
            t = Mix(t, _shirt * s * 0.55f, Line(u, z, open + lapelWidth * 0.9f, notch, open + lapelWidth * 0.2f, notch + 1.8f, 0.3f));
            t = Mix(t, _shirt * s * 0.5f, S((open + 0.3f - u) / 0.2f));
            if (z < 116f) t = Mix(t, _shirt * s * 0.5f, Band(u, 0, 0.14f));
            t = Mix(t, _shirt * s * 0.4f, MathF.Max(Dot(u, z, 1.3f, 112f, 0.6f), Dot(u, z, 1.3f, 104.5f, 0.6f)));
            t = Mix(t, _shirt * s * 0.6f, Line(u, z, 9.5f, 102f, 18f, 102f, 0.5f));
            if (x > 0) t = Mix(t, _shirt * s * 0.6f, Line(u, z, 11f, 131f, 18f, 131f, 0.45f));
            if (_spec.Outfit is Outfit.CabinBlazer or Outfit.PilotJacket && x > 0) t = Mix(t, Gold, Wings(u, z, 14f, 135f));
            return t;
        }

        /// <summary>A neck scarf: a knot and two falling ends.</summary>
        private Vector3 Scarf(Vector3 t, float s, float u, float z)
        {
            var scarf = S((3.8f + (z - 139f) * 0.1f - u) / 0.3f) * S((z - 131f - u * 0.9f) / 0.3f) * S((147f - z) / 0.3f);
            t = Mix(t, _accent * s, scarf);
            return Mix(t, _accent * s * 0.65f, Line(u, z, 0, 143f, 3.4f, 141f, 0.3f) * scarf);
        }

        /// <summary>The apron's bib, neck straps and waist ties.</summary>
        private Vector3 ApronTop(Vector3 t, float s, float u, float z, bool front)
        {
            var apron = FacePainter.ToVec(_spec.ApronColor) * Math.Clamp(s, 0.75f, 1.05f);
            if (!front) return Mix(t, apron * 0.9f, MathF.Max(Band(z, 101f, 1f), Line(u, z, 7f, 150f, 12f, 104f, 1.2f)));
            var width = float.Lerp(15.5f, 8.5f, Math.Clamp((z - 108f) / 16f, 0f, 1f));
            var bib = S((width - u) / 0.3f) * S((132f - z) / 0.3f);
            t = Mix(t, apron, MathF.Max(bib, Line(u, z, 7.5f, 132f, 6.2f, 147f, 1.3f)));
            t = Mix(t, apron * 0.8f, Outline(u, z, 4f, 8f, 110f, 116f, 0.2f));
            return Mix(t, apron * 0.85f, Band(z, 101f, 1f));
        }

        private Vector3 Waistcoat(Vector3 t, float s, float u, float z, bool front)
        {
            if (!front) return Mix(t, _gear * s, S((u - 18f) / 0.3f) * S((z - 126f) / 0.3f));
            var open = Opening(z, 148f, 120f, 7f);
            if (u < open)
            {
                var shirt = u < 6f && z > 146.4f + u * u * 0.03f ? _skin : _gear * s;
                return Tie(shirt, 1, u, z, true);
            }
            t = Mix(t, _shirt * s * 0.55f, S((open + 0.3f - u) / 0.2f));
            if (z < 120f) t = Mix(t, _shirt * s * 0.5f, Band(u, 0, 0.14f));
            for (var b = 116f; b > 94f; b -= 6.5f) t = Mix(t, _accent, Dot(u, z, 1.2f, b, 0.5f));
            t = Mix(t, _shirt * s * 0.6f, Line(u, z, 9f, 106f, 16f, 106f, 0.45f));
            return Mix(t, _gear * s, S((u - 19f) / 0.3f) * S((z - 126f) / 0.3f));   // armholes
        }

        /// <summary>Winged badge: a pin and two swept wings.</summary>
        private static float Wings(float u, float z, float cu, float cz) =>
            MathF.Max(Dot(u, z, cu, cz, 0.7f), MathF.Max(Line(u, z, cu - 3.6f, cz + 0.7f, cu + 3.6f, cz + 0.7f, 0.45f), Line(u, z, cu - 2.5f, cz - 0.1f, cu + 2.5f, cz - 0.1f, 0.35f)));

        private static Vector3 Epaulette(Vector3 t, float u, float z, float y, int rank)
        {
            if (u < 13f || u > 31f || MathF.Abs(y) > 5.2f || z < 141f) return t;
            t = Black * 1.6f;
            for (var i = 0; i < rank; i++)
                t = Mix(t, Gold, Band(u, 25.5f - i * 2.5f, 0.65f));
            return t;
        }

        private Vector3 Harness(Vector3 t, float x, float u, float z, bool front)
        {
            var strap = Black * 2.6f;
            float k;
            if (front)
            {
                k = MathF.Max(Band(u, 10f, 1.6f) * S((z - 96f) / 0.3f), MathF.Max(Band(z, 127f, 1.3f), Band(z, 103f, 1.7f)));
                t = Mix(t, strap, k);
                t = Mix(t, Reflective * 0.7f, MathF.Max(Rect(u, z, -1.8f, 1.8f, 125f, 129f), Rect(u, z, -2.4f, 2.4f, 100.5f, 105.5f)));
                t = Mix(t, Reflective * 0.7f, Rect(u, z, 8f, 12f, 118f, 121f));
            }
            else
            {
                var cross = Line(u, z, 0, 124f, 11f, 148f, 2.8f);
                k = MathF.Max(cross, MathF.Max(Line(u, z, 0, 124f, 11f, 104f, 2.8f), Band(z, 103f, 1.7f)));
                t = Mix(t, strap, k);
            }
            return t;
        }

        private Vector3 Bottom(Vector3 c, float s, Vector3 p)
        {
            float x = p.X, u = MathF.Abs(x), z = p.Z;
            var front = p.Y < 0;
            var o = _spec.Outfit;
            // Bare below the hem (skirt: under the skirt, which is a piece of its own; shorts).
            var hem = KitBuilder.BareBelow(_spec);
            var soft = float.Lerp(1f, Math.Clamp(s, 0.7f, 1.1f), 0.5f);
            if (hem > 0 && z < hem)
                return Mix(_spec.Legs == LegsStyle.Skirt ? _skin * soft * new Vector3(0.96f, 0.93f, 0.92f) : _skin * soft, _trousers * 0.5f, Band(z, hem, 0.2f) * (_spec.Legs == LegsStyle.Skirt ? 0 : 1));
            if (o == Outfit.None) return c;
            var t = _trousers * s;
            switch (o)
            {
                case Outfit.Suit or Outfit.SuitOpenNeck or Outfit.PilotShirt or Outfit.PilotJacket or Outfit.CabinBlazer or Outfit.CabinWaistcoat or Outfit.CabinShirt or Outfit.HiVisVest:
                    // A pressed crease down the front of each leg, and a dark belt.
                    if (front && z < 92f) t = Mix(t, _trousers * s * 0.72f, Band(u, 14.5f, 0.16f));
                    t = Mix(t, Black * 2f, S((z - 97f) / 0.3f));
                    break;
                case Outfit.FlightSuit:
                    if (front) t = Mix(t, Black * 2.5f, Band(u, 0, 0.5f) * S((z - 82f) / 0.3f));
                    // Thigh and shin pockets with zips.
                    t = Mix(t, _trousers * s * 0.65f, Outline(u, z, 17f, 24f, 63f, 80f));
                    t = Mix(t, Black * 2.5f, Line(u, z, 17f, 79f, 24f, 79f, 0.45f));
                    t = Mix(t, Black * 2.5f, Band(u, 20f, 0.3f) * S((46f - z) / 0.3f) * S((z - 26f) / 0.3f) * (front ? 0 : 1));
                    break;
                case Outfit.RescueSuit:
                    t = Mix(t, Reflective, Band(z, 44f, 1.6f));
                    t = Mix(t, Black * 2.2f, Band(z, 54f, 5f) * (front ? 1 : 0));
                    t = Mix(t, Black * 2f, S((26f - z) / 0.5f));
                    t = Mix(t, _trousers * s * 0.65f, Outline(u, z, 17f, 24f, 63f, 80f));
                    break;
            }
            if (_spec.Apron && front && z is > 60f and < 102f)
            {
                // The apron's skirt, across both legs, with a darker hem.
                var apron = FacePainter.ToVec(_spec.ApronColor) * Math.Clamp(s, 0.8f, 1.05f);
                t = Mix(t, apron, S((23f - u) / 0.3f));
                t = Mix(t, apron * 0.75f, Band(z, 62f, 0.4f) * S((23f - u) / 0.3f));
            }
            return t;
        }

        private Vector3 Badge(Vector3 t, Vector3 p, int axis)
        {
            float u = MathF.Abs(p.X);
            if (_spec.Insignia == Insignia.RedTriangle && axis == 5 && u is > 31f and < 46f && MathF.Abs(p.Y) < 8f)
            {
                // Point towards the elbow: the Republican Guard's red triangle, on both sleeves.
                var tri = S((u - 33f) / 0.3f) * S((5.2f - (u - 33f) * 0.5f - MathF.Abs(p.Y)) / 0.25f);
                t = Mix(t, new Vector3(0.62f, 0.07f, 0.06f), tri);
            }
            return t;
        }

        private Vector3 Damage(Vector3 c, Vector3 p, int group)
        {
            var grime = _spec.Grime;
            float n = GarmentPainter.Blotches(p, _seed);
            if (_spec.Injury.HasFlag(Injury.Torn))
                foreach (var (at, r) in _tears)
                {
                    var d = Vector3.Distance(p with { Y = p.Y * 0.4f }, at with { Y = at.Y * 0.4f }) / r + (GarmentPainter.Noise(p * 0.75f + new Vector3(_seed)) - 0.5f) * 0.9f;
                    if (d < 1) c = Mix(_skin * 0.82f, new Vector3(0.09f, 0.05f, 0.045f), S((d - 0.72f) / 0.28f));
                    else if (d < 1.22f) c *= 0.55f;
                }
            if (_spec.Injury.HasFlag(Injury.ArmBandage))
            {
                var side = _bandageLeft ? p.X : -p.X;
                var wrap = S((side - 59f) / 0.3f) * S((73f - side) / 0.3f);
                if (wrap > 0)
                {
                    var fold = 0.9f + 0.1f * MathF.Sin((side + p.Z * 0.5f) * 1.3f);
                    c = Mix(c, White * fold, wrap);
                    c = Mix(c, new Vector3(0.48f, 0.05f, 0.04f), S((1 - new Vector2((side - 65f) / 3.4f, (p.Z - 133f) / 3f).Length()) / 0.3f) * wrap);
                }
            }
            if (group == 2 && _spec.Injury.HasFlag(Injury.LegBandage))
            {
                var mine = _bandageLeft ? p.X < 0 : p.X > 0;
                var wrap = mine ? S((p.Z - 62f) / 0.3f) * S((75f - p.Z) / 0.3f) : 0;
                if (wrap > 0)
                {
                    c = Mix(c, White * (0.9f + 0.1f * MathF.Sin(p.Z * 1.6f + p.X * 0.3f)), wrap);
                    c = Mix(c, new Vector3(0.48f, 0.05f, 0.04f), S((1 - new Vector2((MathF.Abs(p.X) - 15f) / 3.6f, (p.Z - 68f) / 3.2f).Length()) / 0.3f) * wrap * (p.Y < 0 ? 1 : 0));
                }
            }
            if (_spec.Injury.HasFlag(Injury.Blood))
                foreach (var (at, r) in _stains)
                {
                    var d = Vector3.Distance(p with { Y = p.Y * 0.3f }, at with { Y = at.Y * 0.3f }) / r + (GarmentPainter.Noise(p * 0.5f + new Vector3(_seed + 5)) - 0.5f) * 1.0f;
                    c = Mix(c, new Vector3(0.3f, 0.025f, 0.025f) * (0.8f + 0.4f * n), S((1 - d) / 0.2f) * 0.92f);
                }
            if (grime > 0)
            {
                // A dusting all over, soft smudges of soot (hard-edged patches read as camouflage) and mud up the legs.
                c *= 1 - 0.14f * grime;
                var fine = GarmentPainter.Noise(p * 0.62f + new Vector3(_seed + 3));
                var soot = S((0.55f * n + 0.45f * fine - (0.66f - 0.16f * grime)) / 0.3f);
                c = Mix(c, c * new Vector3(0.5f, 0.47f, 0.43f) + new Vector3(0.02f, 0.015f, 0.01f), soot * (0.3f + 0.4f * grime));
                if (p.Z < 50f) c = Mix(c, new Vector3(0.24f, 0.19f, 0.13f), S((50f - p.Z) / 44f) * (0.35f + 0.65f * fine) * grime * 0.75f);
            }
            return c;
        }
    }
}

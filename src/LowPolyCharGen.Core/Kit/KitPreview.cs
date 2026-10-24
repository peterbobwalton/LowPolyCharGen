using System.Numerics;
using LowPolyCharGen.Texturing;

namespace LowPolyCharGen.Kit;

/// <summary>
/// What a pack character looks like with the kit material, for previews that have no shaders: the
/// faces grouped by material slot and per-vertex tint, each group with its texture recoloured the
/// way the material does it (skin tone on skin-coloured texels, clothes recolouring).
/// </summary>
public static class KitPreview
{
    public sealed record Group(TextureImage Texture, int[] Faces);

    public static bool Applies(CharacterModel model) => model.FixedTexture is not null && model.Mesh.Colors is not null;

    /// <param name="size">Side length of the preview textures (the pack textures are scaled to it).</param>
    public static IReadOnlyList<Group> Build(CharacterModel model, int size = 1024)
    {
        var mesh = model.Mesh;
        var colors = mesh.Colors!;
        var colors2 = mesh.Colors2;
        var groups = new Dictionary<(int Material, Vector4 A, Vector4 B), List<int>>();
        for (var f = 0; f < mesh.Faces.Length; f++)
        {
            var v = mesh.Faces[f].Vertices[0];
            var key = (mesh.Faces[f].Material, mesh.Faces[f].Material == 1 ? Vector4.Zero : colors[v],
                mesh.Faces[f].Material == 1 || colors2 is null ? Vector4.Zero : colors2[v]);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(f);
        }
        var scaled = new Dictionary<int, TextureImage>();
        TextureImage Source(int material)
        {
            if (scaled.TryGetValue(material, out var t)) return t;
            var src = material switch { 1 => model.FaceTexture, 2 => model.SecondTexture, 3 => model.ThirdTexture, 4 => model.EquipmentTexture, _ => null } ?? model.FixedTexture!;
            return scaled[material] = Resize(src, material == 1 ? src.Size : size);
        }
        return groups.Select(g => new Group(
            g.Key.Material == 1 ? Source(1) : Recolour(Source(g.Key.Material), g.Key.A, g.Key.B),
            g.Value.ToArray())).ToList();
    }

    /// <summary>The kit material's base colour, on the CPU (see tools/import_unreal.py SKIN_TINT_HLSL).</summary>
    private static TextureImage Recolour(TextureImage t, Vector4 a, Vector4 b)
    {
        var tint = new Vector3(a.X, a.Y, a.Z) * 2;
        var cloth = new Vector3(a.W, b.X, b.Y);
        var hasTint = MathF.Abs(tint.X - 1) + MathF.Abs(tint.Y - 1) + MathF.Abs(tint.Z - 1) > 0.002f;
        var hasCloth = cloth.X + cloth.Y + cloth.Z > 0;
        if (!hasTint && !hasCloth) return t;
        var tintLinear = new Vector3(MathF.Pow(tint.X, 2.2f), MathF.Pow(tint.Y, 2.2f), MathF.Pow(tint.Z, 2.2f));
        var lut = new float[256];
        for (var i = 0; i < 256; i++) lut[i] = MathF.Pow(i / 255f, 2.2f);
        var rgb = new byte[t.Size * t.Size * 3];
        for (int i = 0, o = 0; i < rgb.Length; i += 3, o += t.Channels)
        {
            var c = new Vector3(lut[t.Rgb[o]], lut[t.Rgb[o + 1]], lut[t.Rgb[o + 2]]);
            var chroma = (c.X - c.Y) / (c.X + 0.02f);
            var skin = Math.Clamp((chroma - 0.35f) / 0.15f, 0, 1) * Math.Clamp((c.X - 0.1f) / 0.1f, 0, 1);
            var col = Vector3.Lerp(c, c * tintLinear, skin);
            if (hasCloth) col = Vector3.Lerp(cloth * Vector3.Dot(c, new Vector3(0.3f, 0.59f, 0.11f)), col, skin);
            rgb[i] = ToByte(col.X); rgb[i + 1] = ToByte(col.Y); rgb[i + 2] = ToByte(col.Z);
        }
        return new TextureImage(t.Size, rgb);
    }

    private static byte ToByte(float linear) => (byte)Math.Clamp((int)MathF.Round(MathF.Pow(Math.Clamp(linear, 0, 1), 1 / 2.2f) * 255), 0, 255);

    private static TextureImage Resize(TextureImage t, int size)
    {
        if (t.Size == size && t.Channels == 3) return t;
        var rgb = new byte[size * size * 3];
        var step = (float)t.Size / size;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                int sx = Math.Min(t.Size - 1, (int)((x + 0.5f) * step)), sy = Math.Min(t.Size - 1, (int)((y + 0.5f) * step));
                var o = (sy * t.Size + sx) * t.Channels;
                var d = (y * size + x) * 3;
                rgb[d] = t.Rgb[o]; rgb[d + 1] = t.Rgb[o + 1]; rgb[d + 2] = t.Rgb[o + 2];
            }
        return new TextureImage(size, rgb);
    }
}

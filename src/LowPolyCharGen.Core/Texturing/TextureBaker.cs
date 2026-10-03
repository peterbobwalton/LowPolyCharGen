using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;

namespace LowPolyCharGen.Texturing;

/// <summary>A square RGB image, 8 bits per channel, rows top to bottom.</summary>
public sealed class TextureImage(int size, byte[] rgb)
{
    public int Size { get; } = size;
    public byte[] Rgb { get; } = rgb;

    public byte[] EncodePng()
    {
        var stride = Size * 3;
        var raw = new byte[Size * (stride + 1)];
        for (var y = 0; y < Size; y++)   // each scanline starts with filter type 0
            Buffer.BlockCopy(Rgb, y * stride, raw, y * (stride + 1) + 1, stride);

        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Size);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], Size);
        header[8] = 8;   // bit depth
        header[9] = 2;   // colour type: RGB
        WriteChunk(ms, "IHDR", header);
        using (var data = new MemoryStream())
        {
            using (var z = new ZLibStream(data, CompressionLevel.Fastest, leaveOpen: true))
                z.Write(raw);
            WriteChunk(ms, "IDAT", data.GetBuffer().AsSpan(0, (int)data.Length));
        }
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        stream.Write(buffer);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = Crc32(data, Crc32(typeBytes, 0xFFFFFFFF)) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        stream.Write(buffer);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> data, uint crc)
    {
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}

/// <summary>
/// Bakes the character's texture: every face is rasterised into its place in the atlas and each
/// texel is coloured by the <see cref="Painter"/> from the texel's position on the model.
/// </summary>
public static class TextureBaker
{
    /// <param name="size">Side length in pixels (at most 2048).</param>
    public static TextureImage Bake(CharacterModel model, int size)
    {
        size = Math.Clamp(size, 64, MeshData.AtlasSize);
        var mesh = model.DesignMesh;
        var painter = new Painter(model.Paint);
        var occlusion = AmbientOcclusion.PerCorner(mesh);

        var pixels = new Vector3[size * size];
        var filled = new bool[size * size];
        var scale = (float)size / MeshData.AtlasSize;

        Parallel.For(0, mesh.Faces.Length, f =>
        {
            var face = mesh.Faces[f];
            var chart = mesh.Charts[face.Chart];
            var chartSize = new Vector2(chart.Width, chart.Height);
            var v = face.Vertices;
            for (var i = 1; i + 1 < v.Length; i++)
                Rasterise(face, chart, chartSize, f, 0, i, i + 1);
        });

        void Rasterise(Face face, UvChart chart, Vector2 chartSize, int f, int i0, int i1, int i2)
        {
            // Triangle corners in pixels.
            Vector2 Pixel(int i) => (new Vector2(chart.X, chart.Y) + face.ChartUv[i] * chart.Scale) * scale;
            var a = Pixel(i0);
            var b = Pixel(i1);
            var c = Pixel(i2);
            var area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (MathF.Abs(area) < 1e-6f) return;

            var v = face.Vertices;
            var n = mesh.Normals[f];
            Vector3 pa = mesh.Positions[v[i0]], pb = mesh.Positions[v[i1]], pc = mesh.Positions[v[i2]];
            float oa = occlusion[f][i0], ob = occlusion[f][i1], oc = occlusion[f][i2];

            // Cover a margin around the triangle so the texels on chart borders are painted too.
            const float margin = 1.5f;
            var x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)) - margin));
            var x1 = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X)) + margin));
            var y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)) - margin));
            var y1 = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) + margin));
            // Barycentric slack that corresponds to the margin, per edge.
            var slackA = margin * (c - b).Length() / MathF.Abs(area);
            var slackB = margin * (a - c).Length() / MathF.Abs(area);
            var slackC = margin * (b - a).Length() / MathF.Abs(area);

            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    var px = x + 0.5f;
                    var py = y + 0.5f;
                    var wa = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) / area;
                    var wb = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) / area;
                    var wc = 1f - wa - wb;
                    if (wa < -slackA || wb < -slackB || wc < -slackC) continue;
                    var inside = wa >= 0 && wb >= 0 && wc >= 0;
                    var index = y * size + x;
                    // Texels in the margin only fill gaps; they never overwrite a texel inside a triangle.
                    if (!inside && filled[index]) continue;

                    var position = pa * wa + pb * wb + pc * wc;
                    var normal = n[i0] * wa + n[i1] * wb + n[i2] * wc;
                    normal = normal.LengthSquared() > 1e-10f ? Vector3.Normalize(normal) : n[i0];
                    var ao = Math.Clamp(oa * wa + ob * wb + oc * wc, 0f, 1f);
                    var uv = face.ChartUv[i0] * wa + face.ChartUv[i1] * wb + face.ChartUv[i2] * wc;
                    pixels[index] = painter.Shade(new Texel(position, normal, ao, face.Slot, face.Detail, uv, chartSize));
                    if (inside) filled[index] = true;
                }
        }

        // Texels painted only from a margin count as filled from here on.
        for (var i = 0; i < pixels.Length; i++)
            if (!filled[i] && pixels[i] != Vector3.Zero) filled[i] = true;
        Dilate(pixels, filled, size, 3);

        var rgb = new byte[size * size * 3];
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = filled[i] ? pixels[i] : new Vector3(0.35f, 0.33f, 0.30f);
            rgb[i * 3] = (byte)(Math.Clamp(p.X, 0f, 1f) * 255f + 0.5f);
            rgb[i * 3 + 1] = (byte)(Math.Clamp(p.Y, 0f, 1f) * 255f + 0.5f);
            rgb[i * 3 + 2] = (byte)(Math.Clamp(p.Z, 0f, 1f) * 255f + 0.5f);
        }
        return new TextureImage(size, rgb);
    }

    /// <summary>Grows the painted area outwards so filtering and mip-mapping never pick up the background.</summary>
    private static void Dilate(Vector3[] pixels, bool[] filled, int size, int passes)
    {
        for (var pass = 0; pass < passes; pass++)
        {
            var grow = new List<(int Index, Vector3 Color)>();
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var index = y * size + x;
                    if (filled[index]) continue;
                    var sum = Vector3.Zero;
                    var count = 0;
                    if (x > 0 && filled[index - 1]) { sum += pixels[index - 1]; count++; }
                    if (x < size - 1 && filled[index + 1]) { sum += pixels[index + 1]; count++; }
                    if (y > 0 && filled[index - size]) { sum += pixels[index - size]; count++; }
                    if (y < size - 1 && filled[index + size]) { sum += pixels[index + size]; count++; }
                    if (count > 0) grow.Add((index, sum / count));
                }
            foreach (var (index, color) in grow)
            {
                pixels[index] = color;
                filled[index] = true;
            }
        }
    }
}

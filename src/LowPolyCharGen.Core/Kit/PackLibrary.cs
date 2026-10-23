using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using LowPolyCharGen.Texturing;

namespace LowPolyCharGen.Kit;

/// <summary>One skeletal mesh of the Toon Soldiers pack: positions in the generator's space (cm, +X = left, -Y = front, +Z = up).</summary>
public sealed class PackPart
{
    public required string Name { get; init; }
    public required List<Vector3> Positions { get; init; }
    public required List<Vector3> Normals { get; init; }
    public required List<Vector2> Uvs { get; init; }
    public required List<int[]> Triangles { get; init; }
    public required List<(string Bone, float Weight)[]> Weights { get; init; }

    public PackPart Clone() => new()
    {
        Name = Name,
        Positions = [.. Positions],
        Normals = [.. Normals],
        Uvs = [.. Uvs],
        Triangles = Triangles.Select(t => (int[])t.Clone()).ToList(),
        Weights = [.. Weights],
    };

    /// <summary>True if any vertex is bound to a bone whose name contains <paramref name="fragment"/>.</summary>
    public bool Uses(string fragment) => Weights.Any(w => w.Any(i => i.Bone.Contains(fragment, StringComparison.Ordinal)));
}

/// <summary>
/// The Toon Soldiers pack parts, extracted on the user's machine by tools/pack2json.py from FBX exports
/// of the pack's skeletal meshes (the pack is licensed content: the data is never committed or shipped).
/// The folder is LPCG_PACKDATA, or the first "packdata" folder found above the program.
/// </summary>
public static class PackLibrary
{
    private static readonly Dictionary<string, PackPart> Parts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, TextureImage> Textures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock Gate = new();

    public static string Folder => _folder ??= FindFolder();
    private static string? _folder;

    public static bool Available => Directory.Exists(Folder);

    private static string FindFolder()
    {
        if (Environment.GetEnvironmentVariable("LPCG_PACKDATA") is { Length: > 0 } env) return env;
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "packdata"))) return Path.Combine(dir.FullName, "packdata");
        return Path.Combine(AppContext.BaseDirectory, "packdata");
    }

    public static IEnumerable<string> Names(string prefix) =>
        Available ? Directory.EnumerateFiles(Folder, prefix + "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order() : [];

    public static PackPart Part(string name)
    {
        lock (Gate)
        {
            if (Parts.TryGetValue(name, out var cached)) return cached;
            var path = Path.Combine(Folder, name + ".json");
            if (!File.Exists(path)) throw new FileNotFoundException($"Pack part {name} not found in {Folder} (run tools/pack2json.py)", path);
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            var m = doc.RootElement[0];
            static Vector3 V3(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
            var part = new PackPart
            {
                Name = name,
                Positions = m.GetProperty("verts").EnumerateArray().Select(V3).ToList(),
                Normals = m.GetProperty("normals").EnumerateArray().Select(V3).ToList(),
                Uvs = m.GetProperty("uvs").EnumerateArray().Select(e => new Vector2(e[0].GetSingle(), e[1].GetSingle())).ToList(),
                Triangles = m.GetProperty("tris").EnumerateArray().Select(e => new[] { e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32() }).ToList(),
                Weights = m.GetProperty("weights").EnumerateArray()
                    .Select(w => w.EnumerateArray().Select(i => (i[0].GetString()!, i[1].GetSingle())).ToArray()).ToList(),
            };
            Parts[name] = part;
            return part;
        }
    }

    public static TextureImage Texture(string name)
    {
        lock (Gate)
        {
            if (Textures.TryGetValue(name, out var cached)) return cached;
            var path = Path.Combine(Folder, name + ".png");
            if (!File.Exists(path)) throw new FileNotFoundException($"Pack texture {name} not found in {Folder}", path);
            return Textures[name] = Png.Decode(File.ReadAllBytes(path));
        }
    }
}

/// <summary>Minimal PNG reader: 8-bit RGB or RGBA, square, not interlaced (what Unreal's exporter writes).</summary>
internal static class Png
{
    public static TextureImage Decode(byte[] file)
    {
        int width = 0, height = 0, colorType = 0, pos = 8;
        using var idat = new MemoryStream();
        while (pos < file.Length)
        {
            var length = (file[pos] << 24) | (file[pos + 1] << 16) | (file[pos + 2] << 8) | file[pos + 3];
            var type = System.Text.Encoding.ASCII.GetString(file, pos + 4, 4);
            var data = pos + 8;
            if (type == "IHDR")
            {
                width = (file[data] << 24) | (file[data + 1] << 16) | (file[data + 2] << 8) | file[data + 3];
                height = (file[data + 4] << 24) | (file[data + 5] << 16) | (file[data + 6] << 8) | file[data + 7];
                if (file[data + 8] != 8 || file[data + 12] != 0) throw new NotSupportedException("PNG: 8-bit, not interlaced only");
                colorType = file[data + 9];
            }
            else if (type == "IDAT") idat.Write(file, data, length);
            else if (type == "IEND") break;
            pos = data + length + 4;
        }
        var channels = colorType switch { 2 => 3, 6 => 4, _ => throw new NotSupportedException($"PNG colour type {colorType}") };
        if (width != height) throw new NotSupportedException("PNG: square textures only");
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var stride = width * channels;
        var raw = new byte[height * (stride + 1)];
        z.ReadExactly(raw);
        var pixels = new byte[height * stride];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            var src = y * (stride + 1) + 1;
            var row = y * stride;
            for (var x = 0; x < stride; x++)
            {
                int a = x >= channels ? pixels[row + x - channels] : 0;
                int b = y > 0 ? pixels[row - stride + x] : 0;
                int c = x >= channels && y > 0 ? pixels[row - stride + x - channels] : 0;
                int p = raw[src + x];
                p += filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new NotSupportedException("PNG filter"),
                };
                pixels[row + x] = (byte)p;
            }
        }
        if (channels == 3) return new TextureImage(width, pixels);
        var rgb = new byte[width * height * 3];
        for (int i = 0, j = 0; i < pixels.Length; i += 4, j += 3) { rgb[j] = pixels[i]; rgb[j + 1] = pixels[i + 1]; rgb[j + 2] = pixels[i + 2]; }
        return new TextureImage(width, rgb);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}

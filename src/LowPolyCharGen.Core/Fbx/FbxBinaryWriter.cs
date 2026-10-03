using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace LowPolyCharGen.Fbx;

/// <summary>Serialises an <see cref="FbxNode"/> tree as binary FBX 7.4.</summary>
public static class FbxBinaryWriter
{
    public const int Version = 7400;

    private const int SentinelLength = 13;        // null record of a 7.4 file (three uint32 + one byte)
    private const int CompressThresholdBytes = 128;

    private static readonly byte[] Magic = [.. "Kaydara FBX Binary  "u8, 0x00, 0x1A, 0x00];
    private static readonly byte[] FooterId = [0xFA, 0xBC, 0xAB, 0x09, 0xD0, 0xC8, 0xD4, 0x66, 0xB1, 0x76, 0xFB, 0x83, 0x1C, 0xF7, 0x26, 0x7E];
    private static readonly byte[] FooterMagic = [0xF8, 0x5A, 0x8C, 0x6A, 0xDE, 0xF5, 0xD9, 0x7E, 0xEC, 0xE9, 0x0C, 0xE3, 0x75, 0x8F, 0x29, 0x0B];

    public static void Write(string path, IReadOnlyList<FbxNode> topLevel)
    {
        using var stream = new MemoryStream();
        Write(stream, topLevel);
        File.WriteAllBytes(path, stream.ToArray());
    }

    /// <summary>Writes to a seekable stream positioned at 0 (record end offsets are absolute).</summary>
    public static void Write(Stream stream, IReadOnlyList<FbxNode> topLevel)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write((uint)Version);

        for (var i = 0; i < topLevel.Count; i++)
            WriteNode(w, topLevel[i], i == topLevel.Count - 1);
        w.Write(new byte[SentinelLength]);

        w.Write(FooterId);
        w.Write(new byte[4]);
        var position = w.BaseStream.Position;
        var padding = (int)(((position + 15) & ~15L) - position);
        if (padding == 0) padding = 16;
        w.Write(new byte[padding]);
        w.Write((uint)Version);
        w.Write(new byte[120]);
        w.Write(FooterMagic);
    }

    private static void WriteNode(BinaryWriter w, FbxNode node, bool isLast)
    {
        var start = w.BaseStream.Position;
        w.Write(0u);                            // end offset, patched below
        w.Write((uint)node.Properties.Count);
        w.Write(0u);                            // property list length, patched below
        var name = Encoding.ASCII.GetBytes(node.Name);
        w.Write((byte)name.Length);
        w.Write(name);

        var propertiesStart = w.BaseStream.Position;
        foreach (var property in node.Properties)
            WriteProperty(w, property);
        var propertiesEnd = w.BaseStream.Position;

        if (node.Children.Count > 0)
        {
            for (var i = 0; i < node.Children.Count; i++)
                WriteNode(w, node.Children[i], i == node.Children.Count - 1);
            w.Write(new byte[SentinelLength]);
        }
        else if (node.Properties.Count == 0 && !isLast)
        {
            w.Write(new byte[SentinelLength]);
        }

        var end = w.BaseStream.Position;
        w.BaseStream.Position = start;
        w.Write((uint)end);
        w.BaseStream.Position = start + 8;
        w.Write((uint)(propertiesEnd - propertiesStart));
        w.BaseStream.Position = end;
    }

    private static void WriteProperty(BinaryWriter w, object value)
    {
        switch (value)
        {
            case bool b: w.Write((byte)'C'); w.Write((byte)(b ? 1 : 0)); break;
            case short s: w.Write((byte)'Y'); w.Write(s); break;
            case int i: w.Write((byte)'I'); w.Write(i); break;
            case long l: w.Write((byte)'L'); w.Write(l); break;
            case float f: w.Write((byte)'F'); w.Write(f); break;
            case double d: w.Write((byte)'D'); w.Write(d); break;
            case string s:
            {
                var bytes = Encoding.UTF8.GetBytes(s);
                w.Write((byte)'S');
                w.Write((uint)bytes.Length);
                w.Write(bytes);
                break;
            }
            case byte[] raw:
                w.Write((byte)'R');
                w.Write((uint)raw.Length);
                w.Write(raw);
                break;
            case int[] a: WriteArray(w, 'i', a.Length, MemoryMarshal.AsBytes(a.AsSpan())); break;
            case long[] a: WriteArray(w, 'l', a.Length, MemoryMarshal.AsBytes(a.AsSpan())); break;
            case float[] a: WriteArray(w, 'f', a.Length, MemoryMarshal.AsBytes(a.AsSpan())); break;
            case double[] a: WriteArray(w, 'd', a.Length, MemoryMarshal.AsBytes(a.AsSpan())); break;
            default:
                throw new NotSupportedException($"FBX property type {value.GetType().Name} is not supported.");
        }
    }

    private static void WriteArray(BinaryWriter w, char type, int count, ReadOnlySpan<byte> data)
    {
        w.Write((byte)type);
        w.Write((uint)count);
        if (data.Length <= CompressThresholdBytes)
        {
            w.Write(0u);
            w.Write((uint)data.Length);
            w.Write(data);
            return;
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data);
        w.Write(1u);
        w.Write((uint)compressed.Length);
        w.Write(compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
    }
}

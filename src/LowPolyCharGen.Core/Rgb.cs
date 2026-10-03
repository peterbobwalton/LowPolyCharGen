using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LowPolyCharGen;

/// <summary>8-bit sRGB colour. Serialised to JSON as "#RRGGBB".</summary>
[JsonConverter(typeof(RgbJsonConverter))]
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(string hex)
    {
        if (!TryParse(hex, out var c)) throw new FormatException($"'{hex}' is not a #RRGGBB colour.");
        return c;
    }

    public static bool TryParse(string? text, out Rgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public Rgb Scale(float f) => new(Clamp(R * f), Clamp(G * f), Clamp(B * f));

    public Rgb Lerp(Rgb other, float t) =>
        new(Clamp(R + (other.R - R) * t), Clamp(G + (other.G - G) * t), Clamp(B + (other.B - B) * t));

    private static byte Clamp(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);

    public override string ToString() => ToHex();
}

public sealed class RgbJsonConverter : JsonConverter<Rgb>
{
    public override Rgb Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Rgb.FromHex(reader.GetString() ?? "");

    public override void Write(Utf8JsonWriter writer, Rgb value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToHex());
}

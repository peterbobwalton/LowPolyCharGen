using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Markup;

namespace LowPolyCharGen.App;

/// <summary>XAML: ItemsSource="{local:EnumValues {x:Type core:Gender}}".</summary>
public sealed class EnumValuesExtension(Type type) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Enum.GetValues(type);
}

/// <summary>Shows enum values as words: RolledSleeves -> "Rolled sleeves".</summary>
public sealed partial class EnumNameConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Special = new()
    {
        ["TShirt"] = "T-shirt",
        ["TPose"] = "T-pose",
        ["APose"] = "A-pose",
        ["ToonSoldiers"] = "Toon Soldiers",
        ["Mannequin"] = "UE5 mannequin",
        ["TrousersOnly"] = "Trousers only",
        ["TopOnly"] = "Top only",
    };

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordBoundary();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value?.ToString() ?? "";
        if (Special.TryGetValue(name, out var special)) return special;
        var words = WordBoundary().Split(name);
        return words.Length == 0 ? name : words[0] + string.Concat(words.Skip(1).Select(w => " " + w.ToLowerInvariant()));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

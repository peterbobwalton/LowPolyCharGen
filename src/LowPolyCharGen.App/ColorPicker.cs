using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LowPolyCharGen.App;

/// <summary>A swatch button that drops down a palette of presets and a hex box.</summary>
public sealed class ColorPicker : UserControl
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Rgb), typeof(ColorPicker),
        new FrameworkPropertyMetadata(default(Rgb), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((ColorPicker)d).ShowColor()));

    private static readonly string[] Presets =
    [
        // skin
        "#F7DCC4", "#F3D1B0", "#E0AC82", "#C98E63", "#A56C43", "#7A4B2A", "#553320", "#3B2418",
        // hair
        "#1E1A18", "#4A3222", "#6E4A2C", "#A8743C", "#D2B26E", "#E8DCB8", "#8C8C8C", "#7A2E1C",
        // military and earth tones
        "#6B7A4A", "#4E5A3A", "#3B4A2E", "#8A7A55", "#B9A77C", "#D6C9A4", "#6A5038", "#5C4630",
        // greys and darks
        "#F2F2EE", "#C9CCD1", "#8B8F94", "#5A5F66", "#3F4A5A", "#2C3440", "#2E2620", "#1C1C1E",
        // colours
        "#A23C3C", "#C8642E", "#D8A732", "#4F8A4A", "#2F7F7A", "#3E5F7A", "#2F4FA0", "#6A4A8C",
    ];

    private readonly ToggleButton _button = new() { Height = 24, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(3, 0, 6, 0) };
    private readonly Border _swatch = new() { Width = 30, Height = 14, CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(70, 0, 0, 0)) };
    private readonly TextBlock _label = new() { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _hex = new() { Margin = new Thickness(0, 6, 0, 0), Height = 24, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 7 };
    private readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };

    public Rgb Color
    {
        get => (Rgb)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public ColorPicker()
    {
        var face = new StackPanel { Orientation = Orientation.Horizontal };
        face.Children.Add(_swatch);
        face.Children.Add(_label);
        _button.Content = face;
        Content = _button;

        var grid = new UniformGrid { Columns = 8 };
        foreach (var preset in Presets)
        {
            var rgb = Rgb.FromHex(preset);
            var swatch = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(1.5),
                Background = Brush(rgb),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(70, 0, 0, 0)),
                ToolTip = preset,
                Template = SwatchTemplate(),
                Cursor = Cursors.Hand,
            };
            swatch.Click += (_, _) =>
            {
                Color = rgb;
                _popup.IsOpen = false;
            };
            grid.Children.Add(swatch);
        }

        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(_hex);
        _popup.Child = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC4, 0xC8, 0xCF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7),
            Margin = new Thickness(0, 2, 8, 8),
            Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.25 },
        };
        _popup.PlacementTarget = _button;

        _button.Checked += (_, _) => _popup.IsOpen = true;
        _popup.Closed += (_, _) => _button.IsChecked = false;
        // While the popup is open a click on the button should only close it, not reopen it.
        _popup.Opened += (_, _) => _button.IsHitTestVisible = false;
        _popup.Closed += (_, _) => Dispatcher.BeginInvoke(() => _button.IsHitTestVisible = true);

        _hex.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            CommitHex();
            _popup.IsOpen = false;
        };
        _hex.LostFocus += (_, _) => CommitHex();

        ShowColor();
    }

    private void CommitHex()
    {
        if (Rgb.TryParse(_hex.Text, out var rgb)) Color = rgb;
        else _hex.Text = Color.ToHex();
    }

    private void ShowColor()
    {
        _swatch.Background = Brush(Color);
        _label.Text = Color.ToHex();
        _hex.Text = Color.ToHex();
    }

    private static SolidColorBrush Brush(Rgb c)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    private static ControlTemplate SwatchTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }
}

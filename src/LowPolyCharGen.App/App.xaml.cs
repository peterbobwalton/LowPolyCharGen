using System.Globalization;
using System.IO;
using System.Windows;

namespace LowPolyCharGen.App;

/// <summary>Command line options; mostly for automated screenshots of the UI.</summary>
/// <param name="ScreenshotPath">Render the window to this PNG once loaded, then exit.</param>
public sealed record StartupOptions(CharacterSpec Spec, string? ScreenshotPath, PreviewPose Pose, bool ShowSkeleton, double? Yaw);

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow(ParseArguments(e.Args));
        MainWindow = window;
        window.Show();
    }

    // LowPolyCharGen.exe [preset.json] [--set Property=Value]... [--pose Walk] [--skeleton] [--yaw 140] [--screenshot out.png]
    private static StartupOptions ParseArguments(string[] args)
    {
        var spec = new CharacterSpec();
        string? screenshot = null;
        var pose = PreviewPose.TPose;
        var skeleton = false;
        double? yaw = null;
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--screenshot": screenshot = args[++i]; break;
                    case "--pose": pose = Enum.Parse<PreviewPose>(args[++i], ignoreCase: true); break;
                    case "--skeleton": skeleton = true; break;
                    case "--yaw": yaw = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--set": Assign(spec, args[++i]); break;
                    default:
                        if (File.Exists(args[i])) spec = CharacterSpec.FromJson(File.ReadAllText(args[i]));
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not read the command line: " + ex.Message, "Low Poly Character Generator");
        }
        return new StartupOptions(spec, screenshot, pose, skeleton, yaw);
    }

    private static void Assign(CharacterSpec spec, string assignment)
    {
        var parts = assignment.Split('=', 2);
        var property = typeof(CharacterSpec).GetProperty(parts[0]) ?? throw new ArgumentException($"Unknown option '{parts[0]}'.");
        var type = property.PropertyType;
        object value = type.IsEnum ? Enum.Parse(type, parts[1], ignoreCase: true)
            : type == typeof(Rgb) ? Rgb.FromHex(parts[1])
            : Convert.ChangeType(parts[1], type, CultureInfo.InvariantCulture);
        property.SetValue(spec, value);
    }
}

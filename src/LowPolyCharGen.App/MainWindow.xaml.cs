using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using LowPolyCharGen.Fbx;
using Microsoft.Win32;

namespace LowPolyCharGen.App;

public partial class MainWindow : Window
{
    private const string PresetFilter = "Character preset (*.json)|*.json";

    private readonly CharacterSpec _spec;
    private readonly Model3D _ground = PreviewScene.BuildGround();
    private readonly DispatcherTimer _turntable = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Random _random = new();

    /// <summary>Texture size used in the preview; the export uses the size chosen in the options.</summary>
    private const int PreviewTextureSize = 1024;

    private CharacterModel? _model;
    private IReadOnlyList<PreviewScene.Skin>? _skins;
    private bool _ready;
    private bool _rebuildQueued;
    private bool _building;
    private bool _buildAgain;
    private string? _screenshotPath;

    // Orbit camera: angles in degrees around a target point, in model space (+Z up, character faces -Y).
    private const double DefaultYaw = 24, DefaultPitch = 7, DefaultDistance = 560;
    private static readonly Point3D DefaultTarget = new(0, 0, 92);
    private double _yaw = DefaultYaw, _pitch = DefaultPitch, _distance = DefaultDistance;
    private Point3D _target = DefaultTarget;
    private Point _lastMouse;

    public MainWindow(StartupOptions options)
    {
        _spec = options.Spec;
        InitializeComponent();
        TextureSizeCombo.ItemsSource = new[] { 512, 1024, 2048 };
        FillPackLists();
        DataContext = _spec;
        UpdatePackCard();

        PoseCombo.SelectedItem = options.Pose;
        SkeletonCheck.IsChecked = options.ShowSkeleton;
        if (options.Yaw is { } yaw) _yaw = yaw;

        _spec.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CharacterSpec.Body)) UpdatePackCard();
            QueueRebuild();
        };
        _turntable.Tick += (_, _) =>
        {
            _yaw += 0.6;
            UpdateCamera();
        };

        _ready = true;
        _screenshotPath = options.ScreenshotPath;
        RebuildModel();
        UpdateCamera();
    }

    // ---- pack kit --------------------------------------------------------------------------

    private static readonly string[] HeadPrefixes = ["TSA_Head_", "MIL_Mesh_head_", "TS_Head_"];

    /// <summary>The pack parts and textures found in the extracted pack data (see tools/pack2json.py).</summary>
    private void FillPackLists()
    {
        static List<string> Parts(params string[] prefixes) =>
            prefixes.SelectMany(Kit.PackLibrary.Names).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        PackHeadCombo.ItemsSource = Parts(HeadPrefixes);
        PackBodyCombo.ItemsSource = Parts("TSA_Body_", "MIL_Mesh_Body_", "TS_Body_");
        PackLegsCombo.ItemsSource = Parts("TSA_Legs_", "MIL_Mesh_Legs_");
        PackEquipmentCombo.ItemsSource = new[] { "" }.Concat(Parts("TSA_backpack", "TSA_camelback", "TSA_light_backpack", "TSA_equip_", "MIL_Mesh_extra_", "MIL_Extra_", "TS_Extra_", "TS_Equip_")).ToList();
        var textures = Kit.PackLibrary.Available
            ? Directory.EnumerateFiles(Kit.PackLibrary.Folder, "T_*.png").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToList()
            : [];
        static bool Is(string texture, string prefix) => texture.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        PackTextureCombo.ItemsSource = textures.Where(t => !Is(t, "T_Militias_") && !Is(t, "T_Soldiers_") && !Is(t, "T_Equipment")).ToList();
        MilitiaTextureCombo.ItemsSource = textures.Where(t => Is(t, "T_Militias_")).ToList();
        UE5TextureCombo.ItemsSource = textures.Where(t => Is(t, "T_Soldiers_")).ToList();
        if (!Kit.PackLibrary.Available)
            PackStatus.Text = $"No pack data in {Kit.PackLibrary.Folder}: export the packs' skeletal meshes to FBX in Unreal and run tools/pack2json.py " +
                              "(or set LPCG_PACKDATA to the folder).";
    }

    private void UpdatePackCard() =>
        PackCard.Visibility = _spec.Body == BodyModel.Pack ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>A random pack character: parts, build, colours and the kit's own options.</summary>
    private void RandomisePack(CharacterSpec spec)
    {
        T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];
        var heads = (IReadOnlyList<string>)PackHeadCombo.ItemsSource;
        var bodies = (IReadOnlyList<string>)PackBodyCombo.ItemsSource;
        var legs = (IReadOnlyList<string>)PackLegsCombo.ItemsSource;
        if (heads.Count == 0 || bodies.Count == 0 || legs.Count == 0) return;
        spec.PackHead = Pick(heads);
        spec.PackBody = Pick(bodies);
        spec.PackLegs = Pick(legs);
        var equipment = ((IReadOnlyList<string>)PackEquipmentCombo.ItemsSource).Where(e => e.Length > 0).ToList();
        spec.PackEquipment = equipment.Count > 0 && _random.NextDouble() < 0.3 ? Pick(equipment) : "";
        spec.Nose = Pick(Enum.GetValues<NoseShape>());
        spec.GloveStyle = Pick(Enum.GetValues<GloveStyle>());
        spec.Hat = spec.Hat is HatStyle.Turban or HatStyle.Keffiyeh ? spec.Hat : HatStyle.None;   // the pack heads carry the headgear
        spec.Footwear = Pick(Enum.GetValues<Footwear>());
        spec.RecolourClothes = _random.NextDouble() < 0.3;
    }

    // ---- generation ----------------------------------------------------------------------

    /// <summary>Coalesces bursts of option changes (slider drags, preset loads) into one rebuild.</summary>
    private void QueueRebuild()
    {
        if (!_ready || _rebuildQueued) return;
        _rebuildQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _rebuildQueued = false;
            RebuildModel();
        });
    }

    /// <summary>
    /// Builds the mesh and paints its texture on a worker thread, then swaps both into the preview.
    /// Changes made while a build is running start one more build when it finishes.
    /// </summary>
    private const string BuildErrorPrefix = "Could not build the character: ";

    private async void RebuildModel()
    {
        if (_building)
        {
            _buildAgain = true;
            return;
        }
        _building = true;
        BusyText.Visibility = Visibility.Visible;
        try
        {
            do
            {
                _buildAgain = false;
                var spec = _spec.Clone();
                var ik = IkBonesCheck.IsChecked == true;
                var (model, skins) = await Task.Run(() =>
                {
                    var m = CharacterGenerator.Generate(spec, ik);
                    return (m, PreviewScene.BuildSkins(m, PreviewTextureSize));
                });
                _model = model;
                _skins = skins;
                RefreshScene();
                if (StatusText.Text.StartsWith(BuildErrorPrefix, StringComparison.Ordinal)) StatusText.Text = "";
            }
            while (_buildAgain);
        }
        catch (Exception ex)
        {
            StatusText.Text = BuildErrorPrefix + ex.Message;
        }
        finally
        {
            _building = false;
            BusyText.Visibility = Visibility.Collapsed;
        }
        // A change made while the failed build ran may well be valid: build it.
        if (_buildAgain)
        {
            RebuildModel();
            return;
        }

        if (_screenshotPath is { } path)
        {
            _screenshotPath = null;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            SaveScreenshot(path);
            Application.Current.Shutdown();
        }
    }

    /// <summary>Rebuilds the viewport content from the current model, pose and display options.</summary>
    private void RefreshScene()
    {
        if (!_ready || _model is not { } model || _skins is not { } skins) return;

        var pose = PoseCombo.SelectedItem is PreviewPose p ? p : PreviewPose.TPose;
        var showSkeleton = SkeletonCheck.IsChecked == true;

        IReadOnlyList<Vector3> positions = model.Mesh.Positions;
        IReadOnlyList<Vector3> joints = model.Skeleton.Bones.Select(b => b.Position).ToArray();
        if (pose != PreviewPose.TPose)
        {
            var skin = Posing.SkinMatrices(model.Skeleton, Posing.GetPose(pose));
            positions = Posing.SkinPositions(model.Mesh, skin);
            joints = Posing.BonePositions(model.Skeleton, skin);
        }

        var scene = new Model3DGroup();
        scene.Children.Add(_ground);
        // The skeleton goes in first so the translucent body blends over it.
        if (showSkeleton) scene.Children.Add(PreviewScene.BuildSkeleton(model.Skeleton, joints));
        scene.Children.Add(PreviewScene.BuildBody(model, positions, skins, showSkeleton ? 0.32 : 1.0));
        SceneVisual.Content = scene;

        var height = model.Mesh.Positions.Max(v => v.Z);
        StatsText.Text = $"{model.Mesh.Positions.Length:N0} vertices  ·  {model.Mesh.TriangleCount:N0} triangles  ·  " +
                         $"{model.Skeleton.Bones.Count} bones  ·  {height:F0} cm tall";
    }

    // ---- camera --------------------------------------------------------------------------

    private void UpdateCamera()
    {
        var yaw = _yaw * Math.PI / 180;
        var pitch = _pitch * Math.PI / 180;
        var offset = new Vector3D(Math.Sin(yaw) * Math.Cos(pitch), -Math.Cos(yaw) * Math.Cos(pitch), Math.Sin(pitch)) * _distance;
        Camera.Position = _target + offset;
        Camera.LookDirection = -offset;
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            OnResetView(sender, e);
            return;
        }
        _lastMouse = e.GetPosition(ViewportHost);
        ViewportHost.CaptureMouse();
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e) => ViewportHost.ReleaseMouseCapture();

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (!ViewportHost.IsMouseCaptured) return;
        var position = e.GetPosition(ViewportHost);
        var delta = position - _lastMouse;
        _lastMouse = position;

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _yaw -= delta.X * 0.4;
            _pitch = Math.Clamp(_pitch + delta.Y * 0.4, -85, 85);
        }
        else if (e.RightButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed)
        {
            // Pan in the camera plane, scaled so the model follows the cursor.
            var look = Camera.LookDirection;
            look.Normalize();
            var right = Vector3D.CrossProduct(look, new Vector3D(0, 0, 1));
            right.Normalize();
            var up = Vector3D.CrossProduct(right, look);
            var scale = _distance * 2 * Math.Tan(Camera.FieldOfView * Math.PI / 360) / Math.Max(1, ViewportHost.ActualWidth);
            _target += (-right * delta.X + up * delta.Y) * scale;
        }
        UpdateCamera();
    }

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _distance = Math.Clamp(_distance * Math.Pow(0.999, e.Delta), 80, 1800);
        UpdateCamera();
    }

    private void OnResetView(object sender, RoutedEventArgs e)
    {
        (_yaw, _pitch, _distance, _target) = (DefaultYaw, DefaultPitch, DefaultDistance, DefaultTarget);
        UpdateCamera();
    }

    private void OnViewOptionChanged(object sender, RoutedEventArgs e) => RefreshScene();

    private void OnRigOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_ready) RebuildModel();
    }

    private void OnTurntableChanged(object sender, RoutedEventArgs e)
    {
        if (TurntableCheck.IsChecked == true) _turntable.Start();
        else _turntable.Stop();
    }

    // ---- presets and export --------------------------------------------------------------

    private void OnRandomise(object sender, RoutedEventArgs e)
    {
        var random = CharacterSpec.Random(_random, _spec.Edition);   // dressed for the chosen edition
        random.Name = _spec.Name;
        random.Rig = _spec.Rig;
        random.FlatShaded = _spec.FlatShaded;
        random.TextureSize = _spec.TextureSize;
        random.Body = _spec.Body;
        if (_spec.Body == BodyModel.Pack)
        {
            random.PackTexture = _spec.PackTexture;
            random.MilitiaTexture = _spec.MilitiaTexture;
            random.UE5Texture = _spec.UE5Texture;
            RandomisePack(random);
        }
        _spec.CopyFrom(random);
    }

    private void OnSavePreset(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = PresetFilter, FileName = FbxExporter.SafeName(_spec.Name) + ".json" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, _spec.ToJson());
        StatusText.Text = "Saved preset " + dialog.FileName;
    }

    private void OnLoadPreset(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = PresetFilter };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _spec.CopyFrom(CharacterSpec.FromJson(File.ReadAllText(dialog.FileName)));
            StatusText.Text = "Loaded preset " + dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "That file is not a character preset.\n\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        var dialog = new SaveFileDialog
        {
            Filter = "Autodesk FBX (*.fbx)|*.fbx",
            FileName = "SK_" + FbxExporter.SafeName(_spec.Name) + ".fbx",
            Title = "Export rigged character",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            // Regenerate so the export never depends on what the preview happens to show.
            Mouse.OverrideCursor = Cursors.Wait;
            var model = CharacterGenerator.Generate(_spec, IkBonesCheck.IsChecked == true);
            var result = FbxExporter.Export(model, dialog.FileName);
            Mouse.OverrideCursor = null;
            StatusText.Text = result.TexturePath is null
                ? $"Exported {Path.GetFileName(result.FbxPath)} with its face texture and options to {Path.GetDirectoryName(result.FbxPath)} " +
                  "(pack character: import with tools/import_unreal.py, which uses the packs' textures)"
                : $"Exported {Path.GetFileName(result.FbxPath)}, {Path.GetFileName(result.TexturePath)} and the grime layer {Path.GetFileName(result.GrimePath)} to {Path.GetDirectoryName(result.FbxPath)}";
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{result.FbxPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            MessageBox.Show(this, "The export failed.\n\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveScreenshot(string path)
    {
        var root = (FrameworkElement)Content;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

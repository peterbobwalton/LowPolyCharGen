using System.Numerics;
using LowPolyCharGen;
using LowPolyCharGen.Fbx;

// Headless generator: exports characters without the UI and runs the consistency checks.
//
//   lowpolychargen --out soldier.fbx [--preset soldier.json] [--set Hat=Helmet --set Gender=Female ...]
//   lowpolychargen --random 12 --out-dir out [--seed 7]
//   lowpolychargen --variants out          one FBX per option value
//   lowpolychargen --check                 validate every option combination's mesh and rig
//   lowpolychargen --pose-test poses.json --out-dir dir [--set ...]
//                                          poses a Toon Soldiers character with bone poses dumped from
//                                          the pack's animations in Unreal and writes one OBJ per frame

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0 || args.Contains("--help"))
    {
        Console.WriteLine("usage: lowpolychargen --out <file.fbx> [--preset <file.json>] [--set Property=Value]...");
        Console.WriteLine("       lowpolychargen --random <count> --out-dir <dir> [--seed <n>]");
        Console.WriteLine("       lowpolychargen --variants <dir>");
        Console.WriteLine("       lowpolychargen --check");
        return args.Length == 0 ? 1 : 0;
    }

    string? Option(string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    if (args.Contains("--check")) return Check();

    if (Option("--pose-test") is { } posesPath)
    {
        var spec = Option("--preset") is { } p ? CharacterSpec.FromJson(File.ReadAllText(p)) : new CharacterSpec();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--set")
                Apply(spec, args[i + 1]);
        PoseTest(spec, posesPath, Option("--out-dir") ?? "poses");
        return 0;
    }

    if (Option("--variants") is { } variantsDir)
    {
        foreach (var (label, spec) in Variants())
        {
            spec.Name = label;
            Export(spec, Path.Combine(variantsDir, label + ".fbx"));
        }
        return 0;
    }

    if (Option("--random") is { } countText)
    {
        var dir = Option("--out-dir") ?? "out";
        var rng = new Random(int.Parse(Option("--seed") ?? "1"));
        for (var i = 0; i < int.Parse(countText); i++)
        {
            var spec = CharacterSpec.Random(rng);
            spec.Name = $"Random_{i + 1:00}";
            Export(spec, Path.Combine(dir, spec.Name + ".fbx"));
        }
        return 0;
    }

    if (Option("--out") is { } outPath)
    {
        var spec = Option("--preset") is { } preset ? CharacterSpec.FromJson(File.ReadAllText(preset)) : new CharacterSpec();
        for (var i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--set")
                Apply(spec, args[i + 1]);
        if (Option("--preset") is null && !args.Contains("--set")) spec.Name = Path.GetFileNameWithoutExtension(outPath);
        Export(spec, outPath);
        return 0;
    }

    Console.Error.WriteLine("Nothing to do; see --help.");
    return 1;
}

static void Apply(CharacterSpec spec, string assignment)
{
    var parts = assignment.Split('=', 2);
    var property = typeof(CharacterSpec).GetProperty(parts[0])
        ?? throw new ArgumentException($"Unknown option '{parts[0]}'.");
    var type = property.PropertyType;
    object value = type.IsEnum ? Enum.Parse(type, parts[1], ignoreCase: true)
        : type == typeof(Rgb) ? Rgb.FromHex(parts[1])
        : Convert.ChangeType(parts[1], type, System.Globalization.CultureInfo.InvariantCulture);
    property.SetValue(spec, value);
}

static void Export(CharacterSpec spec, string path)
{
    var model = CharacterGenerator.Generate(spec);
    var result = FbxExporter.Export(model, path);
    Console.WriteLine($"{result.FbxPath}  ({model.Mesh.Positions.Length} verts, {model.Mesh.TriangleCount} tris, {model.Skeleton.Bones.Count} bones)");
}

/// <summary>The default character with each option switched to each of its values in turn.</summary>
static IEnumerable<(string Label, CharacterSpec Spec)> Variants()
{
    yield return ("Default", new CharacterSpec());
    foreach (var property in typeof(CharacterSpec).GetProperties())
    {
        if (property.PropertyType.IsEnum)
        {
            foreach (var value in Enum.GetValues(property.PropertyType))
            {
                var spec = new CharacterSpec();
                if (Equals(property.GetValue(spec), value)) continue;
                property.SetValue(spec, value);
                yield return ($"{property.Name}_{value}", spec);
            }
        }
        else if (property.PropertyType == typeof(bool))
        {
            var spec = new CharacterSpec();
            property.SetValue(spec, true);
            yield return (property.Name, spec);
        }
    }
}

static int Check()
{
    var failures = 0;
    void Fail(string label, string message)
    {
        failures++;
        Console.WriteLine($"FAIL {label}: {message}");
    }

    // Skeleton: the Euler angles written to the FBX must reproduce every bone's bind matrix.
    foreach (var skeleton in new[] { Skeleton.CreateMannequinTPose(), Skeleton.CreateToonSoldier() })
    {
    var rebuilt = new Matrix4x4[skeleton.Bones.Count];
    for (var i = 0; i < skeleton.Bones.Count; i++)
    {
        var bone = skeleton.Bones[i];
        var (t, r, sc) = skeleton.Local(i);
        var e = FbxExporter.ToEulerXyzDegrees(r);
        var rad = Math.PI / 180;
        var local = Matrix4x4.CreateScale(sc) * Matrix4x4.CreateRotationX((float)(e.X * rad)) * Matrix4x4.CreateRotationY((float)(e.Y * rad))
                  * Matrix4x4.CreateRotationZ((float)(e.Z * rad)) * Matrix4x4.CreateTranslation(t);
        rebuilt[i] = bone.Parent < 0 ? local : local * rebuilt[bone.Parent];
        var expected = bone.World;
        var error = 0f;
        for (var row = 0; row < 4; row++)
            for (var col = 0; col < 4; col++)
                error = MathF.Max(error, MathF.Abs(rebuilt[i][row, col] - expected[row, col]));
        if (error > 2e-3f * MathF.Max(1f, bone.Scale)) Fail("skeleton", $"{bone.Name} bind matrix differs by {error}");
    }
    Console.WriteLine($"skeleton {skeleton.Target}: {skeleton.Bones.Count} bones");
    }

    var specs = Variants().ToList();
    var rng = new Random(42);
    for (var i = 0; i < 300; i++)
    {
        var spec = CharacterSpec.Random(rng);
        if (i % 3 == 0) spec.Rig = RigTarget.Mannequin;
        specs.Add(($"random{i}", spec));
    }
    foreach (var edition in Enum.GetValues<Edition>())
        for (var i = 0; i < 25; i++)
            specs.Add(($"{edition}{i}", CharacterSpec.Random(rng, edition)));

    foreach (var (label, spec) in specs)
    {
        CharacterModel model;
        try
        {
            model = CharacterGenerator.Generate(spec);
        }
        catch (Exception ex)
        {
            Fail(label, ex.Message);
            continue;
        }
        var mesh = model.Mesh;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var v = 0; v < mesh.Positions.Length; v++)
        {
            var p = mesh.Positions[v];
            if (!float.IsFinite(p.X + p.Y + p.Z)) { Fail(label, $"vertex {v} is not finite"); break; }
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
            var w = mesh.Weights[v];
            if (w.Length is 0 or > Skinning.MaxInfluences || MathF.Abs(w.Sum(x => x.Weight) - 1f) > 1e-3f)
            {
                Fail(label, $"vertex {v} has bad weights");
                break;
            }
        }
        if (min.Z < -0.05f) Fail(label, $"mesh goes below the ground ({min.Z:F2})");
        if (MathF.Abs(min.X + max.X) > 6f) Fail(label, $"mesh is not centred ({min.X:F1}..{max.X:F1})");
        foreach (var face in mesh.Faces)
            if (face.Vertices.Distinct().Count() < 3) { Fail(label, "degenerate face"); break; }
        foreach (var normals in mesh.Normals)
            if (normals.Any(n => MathF.Abs(n.Length() - 1f) > 1e-3f)) { Fail(label, "bad normal"); break; }

        if (label == "Default")
            Console.WriteLine($"default: {mesh.Positions.Length} verts, {mesh.TriangleCount} tris, bounds {min:F1} .. {max:F1}");
    }

    Console.WriteLine(failures == 0 ? $"OK: {specs.Count} characters checked" : $"{failures} failure(s)");
    return failures == 0 ? 0 : 1;
}

/// <summary>
/// Skins the character with bone poses taken from Unreal (component space, Unreal axes) exactly as the
/// engine does, and writes each frame as an OBJ next to the texture, for checking the skin weights
/// under the pack's real animations.
/// </summary>
static void PoseTest(CharacterSpec spec, string posesPath, string outDir)
{
    Directory.CreateDirectory(outDir);
    var model = CharacterGenerator.Generate(spec);
    var name = FbxExporter.SafeName(spec.Name);
    var texture = name + "_Diffuse.png";
    File.WriteAllBytes(Path.Combine(outDir, texture), model.BakeTexture(1024).EncodePng());
    File.WriteAllText(Path.Combine(outDir, name + ".mtl"), $"newmtl skin\nKd 1 1 1\nmap_Kd {texture}\n");

    var bones = model.Skeleton.Bones;
    var inverseBind = bones.Select(b => { Matrix4x4.Invert(b.World, out var m); return m; }).ToArray();
    var ci = System.Globalization.CultureInfo.InvariantCulture;
    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(posesPath));
    foreach (var anim in doc.RootElement.GetProperty("anims").EnumerateObject())
    {
        var frame = 0;
        foreach (var f in anim.Value.EnumerateArray())
        {
            var pose = f.GetProperty("bones");
            var skin = new Matrix4x4[bones.Count];
            for (var i = 0; i < bones.Count; i++)
            {
                if (!pose.TryGetProperty(bones[i].Name, out var b)) { skin[i] = Matrix4x4.Identity; continue; }
                var v = b.EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
                // Unreal (left-handed, +Y front) to the file's right-handed space (-Y front).
                var world = Matrix4x4.CreateScale(v[7])
                          * Matrix4x4.CreateFromQuaternion(new Quaternion(-v[3], v[4], -v[5], v[6]))
                          * Matrix4x4.CreateTranslation(v[0], -v[1], v[2]);
                skin[i] = inverseBind[i] * world;
            }
            var positions = Posing.SkinPositions(model.Mesh, skin);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"mtllib {name}.mtl").AppendLine("usemtl skin");
            // OBJ is Y-up: (x, z, -y) keeps the character facing -Y once imported.
            foreach (var p in positions) sb.AppendLine(string.Format(ci, "v {0} {1} {2}", p.X / 100, p.Z / 100, -p.Y / 100));
            var corner = 1;
            var faces = new System.Text.StringBuilder();
            for (var fi = 0; fi < model.Mesh.Faces.Length; fi++)
            {
                var face = model.Mesh.Faces[fi];
                faces.Append('f');
                for (var k = 0; k < face.Vertices.Length; k++, corner++)
                {
                    var uv = model.Mesh.Uvs[fi][k];
                    sb.AppendLine(string.Format(ci, "vt {0} {1}", uv.X, uv.Y));
                    faces.Append($" {face.Vertices[k] + 1}/{corner}");
                }
                faces.AppendLine();
            }
            sb.Append(faces);
            File.WriteAllText(Path.Combine(outDir, $"{anim.Name}_{frame++}.obj"), sb.ToString());
        }
    }
    Console.WriteLine($"wrote poses to {outDir}");
}

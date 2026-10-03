using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace LowPolyCharGen.App;

/// <summary>Turns a generated character into WPF 3D models for the preview viewport.</summary>
internal static class PreviewScene
{
    /// <param name="positions">Vertex positions to draw (the bind pose or a skinned pose).</param>
    /// <param name="texture">The baked texture.</param>
    /// <param name="opacity">Below 1 the body is drawn translucent so the skeleton shows through.</param>
    public static Model3D BuildBody(CharacterModel model, IReadOnlyList<Vector3> positions, ImageSource texture, double opacity)
    {
        var mesh = model.Mesh;
        var normals = ReferenceEquals(positions, mesh.Positions)
            ? mesh.Normals
            : MeshData.ComputeNormals(positions, mesh.Faces, model.Spec.FlatShaded ? 0f : CharacterGenerator.SmoothCreaseDegrees);

        var g = new MeshGeometry3D();
        for (var f = 0; f < mesh.Faces.Length; f++)
        {
            var v = mesh.Faces[f].Vertices;
            var uv = mesh.Uvs[f];
            for (var i = 1; i + 1 < v.Length; i++)   // triangle fan
            {
                AddCorner(g, positions[v[0]], normals[f][0], uv[0]);
                AddCorner(g, positions[v[i]], normals[f][i], uv[i]);
                AddCorner(g, positions[v[i + 1]], normals[f][i + 1], uv[i + 1]);
            }
        }
        g.Freeze();

        // Absolute mapping: texture coordinates 0..1 address the whole image.
        var brush = new ImageBrush(texture)
        {
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new System.Windows.Rect(0, 0, 1, 1),
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            Opacity = opacity,
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.Linear);
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        material.Freeze();
        var body = new GeometryModel3D(g, material);
        body.Freeze();
        return body;
    }

    /// <summary>Wraps a baked texture as a WPF image.</summary>
    public static ImageSource ToImage(Texturing.TextureImage texture)
    {
        var image = System.Windows.Media.Imaging.BitmapSource.Create(texture.Size, texture.Size, 96, 96,
            PixelFormats.Rgb24, null, texture.Rgb, texture.Size * 3);
        image.Freeze();
        return image;
    }

    private static void AddCorner(MeshGeometry3D g, Vector3 p, Vector3 n, Vector2? uv = null)
    {
        g.TriangleIndices.Add(g.Positions.Count);
        g.Positions.Add(new Point3D(p.X, p.Y, p.Z));
        g.Normals.Add(new Vector3D(n.X, n.Y, n.Z));
        if (uv is { } t) g.TextureCoordinates.Add(new System.Windows.Point(t.X, 1 - t.Y));
    }

    /// <summary>Octahedral bones from each joint to its children, plus a small marker on every joint.</summary>
    public static Model3D BuildSkeleton(Skeleton skeleton, IReadOnlyList<Vector3> jointPositions)
    {
        var builder = new MeshBuilder(skeleton);
        WeightFn none = _ => [];
        for (var i = 0; i < skeleton.Bones.Count; i++)
        {
            var bone = skeleton.Bones[i];
            if (bone.Name.StartsWith("ik_", StringComparison.Ordinal)) continue;
            var joint = jointPositions[i];
            builder.AddDome(joint, new Vector3(1.3f), 180f, 4, 2, Slot.Skin, none);
            if (bone.Parent <= 0) continue;   // no bone drawn from the root on the ground up to the pelvis

            var from = jointPositions[bone.Parent];
            var axis = joint - from;
            var length = axis.Length();
            if (length < 0.5f) continue;
            var rotation = Geo.LookAlong(axis, Vector3.UnitX);
            var waist = from + axis * 0.18f;
            var width = MathF.Min(2.4f, length * 0.16f);
            var ring = new Vector3[4];
            for (var k = 0; k < 4; k++)
            {
                var (sin, cos) = MathF.SinCos(MathF.PI / 2 * k);
                ring[k] = waist + Vector3.Transform(new Vector3(cos * width, sin * width, 0), rotation);
            }
            builder.AddRingStack([ring], Slot.Skin, none, from, joint);
        }

        var mesh = builder.Build(0f);
        var g = new MeshGeometry3D();
        for (var f = 0; f < mesh.Faces.Length; f++)
        {
            var v = mesh.Faces[f].Vertices;
            for (var i = 1; i + 1 < v.Length; i++)
            {
                AddCorner(g, mesh.Positions[v[0]], mesh.Normals[f][0]);
                AddCorner(g, mesh.Positions[v[i]], mesh.Normals[f][i]);
                AddCorner(g, mesh.Positions[v[i + 1]], mesh.Normals[f][i + 1]);
            }
        }
        g.Freeze();
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20))));
        material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x70, 0x48, 0x00))));
        material.Freeze();
        var result = new GeometryModel3D(g, material);
        result.Freeze();
        return result;
    }

    /// <summary>A ground disc under the character.</summary>
    public static Model3D BuildGround(double radius = 130)
    {
        var g = new MeshGeometry3D();
        const int segments = 48;
        g.Positions.Add(new Point3D(0, 0, -0.05));
        g.Normals.Add(new Vector3D(0, 0, 1));
        for (var i = 0; i < segments; i++)
        {
            var a = Math.PI * 2 * i / segments;
            g.Positions.Add(new Point3D(Math.Cos(a) * radius, Math.Sin(a) * radius, -0.05));
            g.Normals.Add(new Vector3D(0, 0, 1));
        }
        for (var i = 0; i < segments; i++)
        {
            g.TriangleIndices.Add(0);
            g.TriangleIndices.Add(1 + i);
            g.TriangleIndices.Add(1 + (i + 1) % segments);
        }
        g.Freeze();
        var brush = new SolidColorBrush(Color.FromRgb(0x4A, 0x50, 0x5C));
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        var model = new GeometryModel3D(g, material) { BackMaterial = material };
        model.Freeze();
        return model;
    }
}

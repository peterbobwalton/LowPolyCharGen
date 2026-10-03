using System.Numerics;
using System.Text;

namespace LowPolyCharGen.Fbx;

public sealed record FbxExportResult(string FbxPath, string? TexturePath);

/// <summary>
/// Writes a <see cref="CharacterModel"/> as a rigged, skinned FBX 7.4 file plus its painted texture.
/// The file is Z-up, centimetres, right-handed with the character facing -Y, which Unreal imports
/// as a character facing +Y with an untransformed root bone - the same layout as the UE mannequin.
/// </summary>
public static class FbxExporter
{
    private const string Creator = "LowPolyCharGen 1.0";
    private const string Sep = "\0\u0001";   // separates object name and class in binary FBX

    public static FbxExportResult Export(CharacterModel model, string fbxPath, bool writeTexture = true)
    {
        fbxPath = Path.GetFullPath(fbxPath);
        var directory = Path.GetDirectoryName(fbxPath)!;
        Directory.CreateDirectory(directory);
        var texturePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(fbxPath) + "_Diffuse.png");

        FbxBinaryWriter.Write(fbxPath, BuildDocument(model, fbxPath, texturePath));
        if (writeTexture) File.WriteAllBytes(texturePath, model.BakeTexture(model.Spec.TextureSize).EncodePng());
        return new FbxExportResult(fbxPath, writeTexture ? texturePath : null);
    }

    public static string SafeName(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim())
            sb.Append(char.IsAsciiLetterOrDigit(ch) || ch == '_' ? ch : '_');
        return sb.Length == 0 ? "Character" : sb.ToString();
    }

    public static List<FbxNode> BuildDocument(CharacterModel model, string fbxPath, string texturePath)
    {
        var name = SafeName(model.Spec.Name);
        var mesh = model.Mesh;
        var skeleton = model.Skeleton;
        var bones = skeleton.Bones;

        long nextId = 1_000_000;
        long NewId() => nextId++;

        var geometryId = NewId();
        var meshModelId = NewId();
        var materialId = NewId();
        var textureId = NewId();
        var videoId = NewId();
        var skinId = NewId();
        var poseId = NewId();
        var boneModelIds = bones.Select(_ => NewId()).ToArray();
        var boneAttributeIds = bones.Select(_ => NewId()).ToArray();
        var clusterIds = bones.Select(_ => NewId()).ToArray();

        var objects = new FbxNode("Objects");
        var connections = new FbxNode("Connections");
        void Connect(long child, long parent) => connections.Add("C", "OO", child, parent);

        // ---- geometry --------------------------------------------------------------------
        var geometry = objects.Add("Geometry", geometryId, name + Sep + "Geometry", "Mesh");
        geometry.Add("GeometryVersion", 124);
        geometry.Add("Vertices", Flatten(mesh.Positions));

        var cornerCount = mesh.Faces.Sum(f => f.Vertices.Length);
        var polygonIndex = new int[cornerCount];
        var normals = new double[cornerCount * 3];
        var corner = 0;
        for (var f = 0; f < mesh.Faces.Length; f++)
        {
            var v = mesh.Faces[f].Vertices;
            for (var i = 0; i < v.Length; i++, corner++)
            {
                polygonIndex[corner] = i == v.Length - 1 ? ~v[i] : v[i];   // the last index of a polygon is stored negated
                var n = mesh.Normals[f][i];
                normals[corner * 3] = n.X;
                normals[corner * 3 + 1] = n.Y;
                normals[corner * 3 + 2] = n.Z;
            }
        }
        geometry.Add("PolygonVertexIndex", polygonIndex);

        var normalLayer = geometry.Add("LayerElementNormal", 0);
        normalLayer.Add("Version", 101);
        normalLayer.Add("Name", "");
        normalLayer.Add("MappingInformationType", "ByPolygonVertex");
        normalLayer.Add("ReferenceInformationType", "Direct");
        normalLayer.Add("Normals", normals);

        // One texture coordinate per polygon corner.
        var uvs = new double[cornerCount * 2];
        var uvIndex = new int[cornerCount];
        corner = 0;
        for (var f = 0; f < mesh.Faces.Length; f++)
            foreach (var uv in mesh.Uvs[f])
            {
                uvs[corner * 2] = uv.X;
                uvs[corner * 2 + 1] = uv.Y;
                uvIndex[corner] = corner;
                corner++;
            }

        var uvLayer = geometry.Add("LayerElementUV", 0);
        uvLayer.Add("Version", 101);
        uvLayer.Add("Name", "UVMap");
        uvLayer.Add("MappingInformationType", "ByPolygonVertex");
        uvLayer.Add("ReferenceInformationType", "IndexToDirect");
        uvLayer.Add("UV", uvs);
        uvLayer.Add("UVIndex", uvIndex);

        var smoothing = new int[mesh.Faces.Length];
        Array.Fill(smoothing, model.Spec.FlatShaded ? 0 : 1);
        var smoothingLayer = geometry.Add("LayerElementSmoothing", 0);
        smoothingLayer.Add("Version", 102);
        smoothingLayer.Add("Name", "");
        smoothingLayer.Add("MappingInformationType", "ByPolygon");
        smoothingLayer.Add("ReferenceInformationType", "Direct");
        smoothingLayer.Add("Smoothing", smoothing);

        var materialLayer = geometry.Add("LayerElementMaterial", 0);
        materialLayer.Add("Version", 101);
        materialLayer.Add("Name", "");
        materialLayer.Add("MappingInformationType", "AllSame");
        materialLayer.Add("ReferenceInformationType", "IndexToDirect");
        materialLayer.Add("Materials", new[] { 0 });

        var layer = geometry.Add("Layer", 0);
        layer.Add("Version", 100);
        foreach (var type in new[] { "LayerElementNormal", "LayerElementSmoothing", "LayerElementUV", "LayerElementMaterial" })
        {
            var element = layer.Add("LayerElement");
            element.Add("Type", type);
            element.Add("TypedIndex", 0);
        }

        // ---- models ----------------------------------------------------------------------
        AddModel(objects, meshModelId, name, "Mesh", Vector3.Zero, (0, 0, 0));
        Connect(meshModelId, 0);
        Connect(geometryId, meshModelId);

        // Bind matrices are rebuilt in double precision from the local values that go into the file,
        // so the bind pose matches the node transforms exactly (the FBX SDK validates this).
        var bind = new Matrix4d[bones.Count];
        for (var i = 0; i < bones.Count; i++)
        {
            var (translation, rotation, scale) = skeleton.Local(i);
            var euler = ToEulerXyzDegrees(rotation);
            var local = Matrix4d.FromEulerXyzDegrees(euler.X, euler.Y, euler.Z, translation.X, translation.Y, translation.Z, scale);
            bind[i] = bones[i].Parent < 0 ? local : local.Then(bind[bones[i].Parent]);

            AddModel(objects, boneModelIds[i], bones[i].Name, "LimbNode", translation, euler, scale);
            Connect(boneModelIds[i], bones[i].Parent < 0 ? 0 : boneModelIds[bones[i].Parent]);

            var attribute = objects.Add("NodeAttribute", boneAttributeIds[i], bones[i].Name + Sep + "NodeAttribute", "LimbNode");
            attribute.Add("TypeFlags", "Skeleton");
            Connect(boneAttributeIds[i], boneModelIds[i]);
        }

        // ---- material and texture ----------------------------------------------------------
        var material = objects.Add("Material", materialId, "M_" + name + Sep + "Material", "");
        material.Add("Version", 102);
        material.Add("ShadingModel", "lambert");
        material.Add("MultiLayer", 0);
        var materialProperties = material.Add("Properties70");
        materialProperties.P("DiffuseColor", "Color", "", "A", 1.0, 1.0, 1.0);
        materialProperties.P("DiffuseFactor", "Number", "", "A", 1.0);
        materialProperties.P("AmbientColor", "Color", "", "A", 0.0, 0.0, 0.0);
        materialProperties.P("EmissiveColor", "Color", "", "A", 0.0, 0.0, 0.0);
        Connect(materialId, meshModelId);

        var textureName = "T_" + name + "_Diffuse";
        var textureFile = Path.GetFileName(texturePath);
        var video = objects.Add("Video", videoId, textureName + Sep + "Video", "Clip");
        video.Add("Type", "Clip");
        video.Add("Properties70").P("Path", "KString", "XRefUrl", "", texturePath);
        video.Add("UseMipMap", 0);
        video.Add("Filename", texturePath);
        video.Add("RelativeFilename", textureFile);

        var texture = objects.Add("Texture", textureId, textureName + Sep + "Texture", "");
        texture.Add("Type", "TextureVideoClip");
        texture.Add("Version", 202);
        texture.Add("TextureName", textureName + Sep + "Texture");
        texture.Add("Media", textureName + Sep + "Video");
        texture.Add("FileName", texturePath);
        texture.Add("RelativeFilename", textureFile);
        texture.Add("ModelUVTranslation", 0.0, 0.0);
        texture.Add("ModelUVScaling", 1.0, 1.0);
        texture.Add("Texture_Alpha_Source", "None");
        texture.Add("Cropping", 0, 0, 0, 0);
        var textureProperties = texture.Add("Properties70");
        textureProperties.P("UVSet", "KString", "", "", "UVMap");
        textureProperties.P("UseMaterial", "bool", "", "", 1);
        Connect(videoId, textureId);
        connections.Add("C", "OP", textureId, materialId, "DiffuseColor");

        // ---- skin ------------------------------------------------------------------------
        var skin = objects.Add("Deformer", skinId, name + Sep + "Deformer", "Skin");
        skin.Add("Version", 101);
        skin.Add("Link_DeformAcuracy", 50.0);
        Connect(skinId, geometryId);

        var indexes = bones.Select(_ => new List<int>()).ToArray();
        var weights = bones.Select(_ => new List<double>()).ToArray();
        for (var v = 0; v < mesh.Weights.Length; v++)
            foreach (var influence in mesh.Weights[v])
            {
                indexes[influence.Bone].Add(v);
                weights[influence.Bone].Add(influence.Weight);
            }

        for (var i = 0; i < bones.Count; i++)
        {
            var cluster = objects.Add("Deformer", clusterIds[i], bones[i].Name + Sep + "SubDeformer", "Cluster");
            cluster.Add("Version", 100);
            cluster.Add("UserData", "", "");
            if (indexes[i].Count > 0)
            {
                cluster.Add("Indexes", indexes[i].ToArray());
                cluster.Add("Weights", weights[i].ToArray());
            }
            cluster.Add("Transform", bind[i].InvertRigid().ToArray());   // mesh space -> bone space at bind time
            cluster.Add("TransformLink", bind[i].ToArray());             // bone -> world at bind time
            Connect(clusterIds[i], skinId);
            Connect(boneModelIds[i], clusterIds[i]);
        }

        var pose = objects.Add("Pose", poseId, name + Sep + "Pose", "BindPose");
        pose.Add("Type", "BindPose");
        pose.Add("Version", 100);
        pose.Add("NbPoseNodes", bones.Count + 1);
        var meshPose = pose.Add("PoseNode");
        meshPose.Add("Node", meshModelId);
        meshPose.Add("Matrix", Matrix4d.Identity.ToArray());
        for (var i = 0; i < bones.Count; i++)
        {
            var node = pose.Add("PoseNode");
            node.Add("Node", boneModelIds[i]);
            node.Add("Matrix", bind[i].ToArray());
        }

        // ---- document --------------------------------------------------------------------
        var definitions = new FbxNode("Definitions");
        definitions.Add("Version", 100);
        var counts = new (string Type, int Count)[]
        {
            ("GlobalSettings", 1), ("Geometry", 1), ("Model", bones.Count + 1), ("NodeAttribute", bones.Count),
            ("Material", 1), ("Video", 1), ("Texture", 1), ("Deformer", bones.Count + 1), ("Pose", 1),
        };
        definitions.Add("Count", counts.Sum(c => c.Count));
        foreach (var (type, count) in counts)
            definitions.Add("ObjectType", type).Add("Count", count);

        var documents = new FbxNode("Documents");
        documents.Add("Count", 1);
        var document = documents.Add("Document", NewId(), "Scene", "Scene");
        var documentProperties = document.Add("Properties70");
        documentProperties.P("SourceObject", "object", "", "");
        documentProperties.P("ActiveAnimStackName", "KString", "", "", "");
        document.Add("RootNode", 0L);

        var takes = new FbxNode("Takes");
        takes.Add("Current", "");

        return
        [
            BuildHeader(fbxPath),
            new FbxNode("FileId", new byte[] { 0x28, 0xB3, 0x2A, 0xEB, 0xB6, 0x24, 0xCC, 0xC2, 0xBF, 0xC8, 0xB0, 0x2A, 0xA9, 0x2B, 0xFC, 0xF1 }),
            new FbxNode("CreationTime", "1970-01-01 10:00:00:000"),
            new FbxNode("Creator", Creator),
            BuildGlobalSettings(),
            documents,
            new FbxNode("References"),
            definitions,
            objects,
            connections,
            takes,
        ];
    }

    private static FbxNode BuildHeader(string fbxPath)
    {
        var now = DateTime.Now;
        var header = new FbxNode("FBXHeaderExtension");
        header.Add("FBXHeaderVersion", 1003);
        header.Add("FBXVersion", FbxBinaryWriter.Version);
        header.Add("EncryptionType", 0);
        var stamp = header.Add("CreationTimeStamp");
        stamp.Add("Version", 1000);
        stamp.Add("Year", now.Year);
        stamp.Add("Month", now.Month);
        stamp.Add("Day", now.Day);
        stamp.Add("Hour", now.Hour);
        stamp.Add("Minute", now.Minute);
        stamp.Add("Second", now.Second);
        stamp.Add("Millisecond", now.Millisecond);
        header.Add("Creator", Creator);

        var info = header.Add("SceneInfo", "GlobalInfo" + Sep + "SceneInfo", "UserData");
        info.Add("Type", "UserData");
        info.Add("Version", 100);
        var meta = info.Add("MetaData");
        meta.Add("Version", 100);
        foreach (var field in new[] { "Title", "Subject", "Author", "Keywords", "Revision", "Comment" })
            meta.Add(field, "");
        var properties = info.Add("Properties70");
        properties.P("DocumentUrl", "KString", "Url", "", fbxPath);
        properties.P("SrcDocumentUrl", "KString", "Url", "", fbxPath);
        properties.P("Original", "Compound", "", "");
        properties.P("Original|ApplicationName", "KString", "", "", "LowPolyCharGen");
        properties.P("Original|ApplicationVersion", "KString", "", "", "1.0");
        return header;
    }

    private static FbxNode BuildGlobalSettings()
    {
        var settings = new FbxNode("GlobalSettings");
        settings.Add("Version", 1000);
        var p = settings.Add("Properties70");
        // Z up, -Y front, +X right-handed third axis: the 3ds Max axis system, which is what Unreal uses.
        p.P("UpAxis", "int", "Integer", "", 2);
        p.P("UpAxisSign", "int", "Integer", "", 1);
        p.P("FrontAxis", "int", "Integer", "", 1);
        p.P("FrontAxisSign", "int", "Integer", "", -1);
        p.P("CoordAxis", "int", "Integer", "", 0);
        p.P("CoordAxisSign", "int", "Integer", "", 1);
        p.P("OriginalUpAxis", "int", "Integer", "", 2);
        p.P("OriginalUpAxisSign", "int", "Integer", "", 1);
        p.P("UnitScaleFactor", "double", "Number", "", 1.0);           // centimetres
        p.P("OriginalUnitScaleFactor", "double", "Number", "", 1.0);
        p.P("AmbientColor", "ColorRGB", "Color", "", 0.0, 0.0, 0.0);
        p.P("DefaultCamera", "KString", "", "", "Producer Perspective");
        p.P("TimeMode", "enum", "", "", 6);                            // 30 fps
        p.P("TimeSpanStart", "KTime", "Time", "", 0L);
        p.P("TimeSpanStop", "KTime", "Time", "", 46186158000L);
        p.P("CustomFrameRate", "double", "Number", "", 30.0);
        return settings;
    }

    private static void AddModel(FbxNode objects, long id, string name, string type, Vector3 translation, (double X, double Y, double Z) euler, double scale = 1)
    {
        var model = objects.Add("Model", id, name + Sep + "Model", type);
        model.Add("Version", 232);
        var p = model.Add("Properties70");
        p.P("Lcl Translation", "Lcl Translation", "", "A", (double)translation.X, (double)translation.Y, (double)translation.Z);
        p.P("Lcl Rotation", "Lcl Rotation", "", "A", euler.X, euler.Y, euler.Z);
        p.P("Lcl Scaling", "Lcl Scaling", "", "A", scale, scale, scale);
        p.P("DefaultAttributeIndex", "int", "Integer", "", 0);
        p.P("InheritType", "enum", "", "", 1);
        model.Add("MultiLayer", 0);
        model.Add("MultiTake", 0);
        model.Add("Shading", true);
        model.Add("Culling", "CullingOff");
    }

    /// <summary>
    /// FBX's default rotation order (XYZ: rotate about X, then Y, then Z), in degrees.
    /// </summary>
    public static (double X, double Y, double Z) ToEulerXyzDegrees(Quaternion q)
    {
        double x = q.X, y = q.Y, z = q.Z, w = q.W;
        var n = Math.Sqrt(x * x + y * y + z * z + w * w);
        x /= n; y /= n; z /= n; w /= n;

        // Elements of the rotation matrix R (column vectors, R = Rz * Ry * Rx).
        var r00 = 1 - 2 * (y * y + z * z);
        var r10 = 2 * (x * y + z * w);
        var r20 = 2 * (x * z - y * w);
        var r21 = 2 * (y * z + x * w);
        var r22 = 1 - 2 * (x * x + y * y);
        var r11 = 1 - 2 * (x * x + z * z);
        var r12 = 2 * (y * z - x * w);

        const double toDegrees = 180.0 / Math.PI;
        var sinY = Math.Clamp(-r20, -1.0, 1.0);
        if (Math.Abs(sinY) < 0.9999999)
            return (Math.Atan2(r21, r22) * toDegrees, Math.Asin(sinY) * toDegrees, Math.Atan2(r10, r00) * toDegrees);
        // Gimbal lock: Y is +-90 degrees, put the whole remaining rotation on X.
        return (Math.Atan2(-r12, r11) * toDegrees, Math.Asin(sinY) * toDegrees, 0.0);
    }

    private static double[] Flatten(IReadOnlyList<Vector3> points)
    {
        var a = new double[points.Count * 3];
        for (var i = 0; i < points.Count; i++)
        {
            a[i * 3] = points[i].X;
            a[i * 3 + 1] = points[i].Y;
            a[i * 3 + 2] = points[i].Z;
        }
        return a;
    }

}

"""Imports generated characters into an Unreal project.

Every <name>.fbx in a folder (with its <name>_Diffuse.png) becomes, under the destination folder:
  SK_<name>   skeletal mesh on the Toon Soldiers skeleton (or the UE5 mannequin's, see --mannequin)
  T_<name>    its texture
  T_<name>_Grime  its grime layer (RGB dirt, A coverage), when exported
  MI_<name>   a material instance of M_LPCG_Character (texture + grime layer, GrimeAmount from <name>.json)
and the pack's physics asset is assigned, so the character drops into anything built for the pack.

Run inside the editor (Tools > Execute Python Script, or the Output Log's Python console):
  py "C:/source/Claude/LowPolyCharGen/tools/import_unreal.py" "D:/out" [/Game/LowPolyCharGen] [--mannequin] [--lods=N]
or headless:
  UnrealEditor-Cmd.exe Project.uproject -run=pythonscript -script="import_unreal.py D:/out /Game/LowPolyCharGen"
"""
import json
import os
import sys

import unreal

PACK = "/Game/Toon_Soldiers_Armies"
TOON_SKELETON = PACK + "/Meshes/Characters_Skeleton"
TOON_PHYSICS = PACK + "/Meshes/Characters_PhysicsAsset"
TOON_MATERIAL = PACK + "/Materials/M_Armies_Parent"
LAYERED_MATERIAL_DIR = "/Game/LowPolyCharGen/Materials"
LAYERED_MATERIAL = LAYERED_MATERIAL_DIR + "/M_LPCG_Character"
MANNEQUIN_SKELETON = "/Game/Characters/Mannequins/Meshes/SK_Mannequin"
MANNEQUIN_PHYSICS = "/Game/Characters/Mannequins/Rigs/PA_Mannequin"
DEFAULT_LODS = 3   # LOD0 + two reduced levels; --lods=1 imports the full mesh only


def import_task(filename, destination, name, options=None):
    task = unreal.AssetImportTask()
    task.filename = filename
    task.destination_path = destination
    task.destination_name = name
    task.automated = True
    task.replace_existing = True
    task.save = True
    if options is not None:
        task.options = options
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
    return [unreal.load_asset(p) for p in task.imported_object_paths]


def layered_material():
    """
    The character material: the pack's look (one texture, default lit) plus a grime layer,
    BaseColor = lerp(Texture.rgb, GrimeTexture.rgb, GrimeTexture.a * GrimeAmount). Created once.
    """
    if unreal.EditorAssetLibrary.does_asset_exist(LAYERED_MATERIAL):
        mat = unreal.load_asset(LAYERED_MATERIAL)
        if not mat.get_editor_property("used_with_skeletal_mesh") or mat.get_editor_property("two_sided"):
            # older copies lacked the flag: the meshes then render with the default grey material
            mat.set_editor_property("used_with_skeletal_mesh", True)
            mat.set_editor_property("two_sided", False)
            unreal.MaterialEditingLibrary.recompile_material(mat)
            unreal.EditorAssetLibrary.save_loaded_asset(mat)
        return mat
    mel = unreal.MaterialEditingLibrary
    mat = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
        "M_LPCG_Character", LAYERED_MATERIAL_DIR, unreal.Material, unreal.MaterialFactoryNew())
    # set up front: without it some imported meshes get the default material in game and in thumbnails
    mat.set_editor_property("used_with_skeletal_mesh", True)
    mat.set_editor_property("two_sided", False)   # the meshes have no back faces to show
    tex = mel.create_material_expression(mat, unreal.MaterialExpressionTextureSampleParameter2D, -700, -200)
    tex.set_editor_property("parameter_name", "Texture")
    tex.set_editor_property("texture", unreal.load_asset("/Engine/EngineResources/WhiteSquareTexture"))
    grime = mel.create_material_expression(mat, unreal.MaterialExpressionTextureSampleParameter2D, -700, 100)
    grime.set_editor_property("parameter_name", "GrimeTexture")
    grime.set_editor_property("texture", unreal.load_asset("/Engine/EngineResources/Black"))
    amount = mel.create_material_expression(mat, unreal.MaterialExpressionScalarParameter, -700, 400)
    amount.set_editor_property("parameter_name", "GrimeAmount")
    amount.set_editor_property("default_value", 0.0)
    cover = mel.create_material_expression(mat, unreal.MaterialExpressionMultiply, -400, 250)
    mel.connect_material_expressions(grime, "A", cover, "A")
    mel.connect_material_expressions(amount, "", cover, "B")
    blend = mel.create_material_expression(mat, unreal.MaterialExpressionLinearInterpolate, -200, 0)
    mel.connect_material_expressions(tex, "RGB", blend, "A")
    mel.connect_material_expressions(grime, "RGB", blend, "B")
    mel.connect_material_expressions(cover, "", blend, "Alpha")
    mel.connect_material_property(blend, "", unreal.MaterialProperty.MP_BASE_COLOR)
    rough = mel.create_material_expression(mat, unreal.MaterialExpressionConstant, -200, 250)
    rough.set_editor_property("r", 0.85)
    mel.connect_material_property(rough, "", unreal.MaterialProperty.MP_ROUGHNESS)
    mel.recompile_material(mat)
    unreal.EditorAssetLibrary.save_loaded_asset(mat)
    return mat


def single_sided(mi):
    """
    Back faces culled whatever the parent says: the meshes are closed and single sided (hidden
    triangles are left out by the generator), so drawing back faces would only cost.
    """
    overrides = mi.get_editor_property("base_property_overrides")
    overrides.set_editor_property("override_two_sided", True)
    overrides.set_editor_property("two_sided", False)
    mi.set_editor_property("base_property_overrides", overrides)
    unreal.MaterialEditingLibrary.update_material_instance(mi)


def add_lods(mesh, count):
    """
    Lower-detail versions for crowds: Unreal's skeletal mesh reduction, each LOD half the triangles of the
    one before (about 2,400 -> 1,200 -> 600 tris), switched by screen size.
    """
    unreal.get_editor_subsystem(unreal.SkeletalMeshEditorSubsystem).regenerate_lod(mesh, count, True, False)


def import_character(fbx, destination, mannequin, lods=DEFAULT_LODS):
    base = os.path.splitext(os.path.basename(fbx))[0]
    skeleton = unreal.load_asset(MANNEQUIN_SKELETON if mannequin else TOON_SKELETON)
    if skeleton is None:
        raise RuntimeError("Skeleton not found: " + (MANNEQUIN_SKELETON if mannequin else TOON_SKELETON))

    options = unreal.FbxImportUI()
    options.import_mesh = True
    options.import_as_skeletal = True
    options.mesh_type_to_import = unreal.FBXImportType.FBXIT_SKELETAL_MESH
    options.skeleton = skeleton
    options.import_materials = False
    options.import_textures = False
    options.import_animations = False
    options.create_physics_asset = False
    options.skeletal_mesh_import_data.set_editor_property("use_t0_as_ref_pose", False)
    options.skeletal_mesh_import_data.set_editor_property("import_morph_targets", False)
    options.skeletal_mesh_import_data.set_editor_property("vertex_color_import_option", unreal.VertexColorImportOption.REPLACE)
    options.skeletal_mesh_import_data.set_editor_property("normal_import_method", unreal.FBXNormalImportMethod.FBXNIM_IMPORT_NORMALS)
    imported = import_task(fbx, destination, "SK_" + base, options)
    mesh = next((a for a in imported if isinstance(a, unreal.SkeletalMesh)), None)
    if mesh is None:
        raise RuntimeError("FBX import produced no skeletal mesh: " + fbx)

    spec_json = os.path.join(os.path.dirname(fbx), base + "_Spec.json")
    spec = json.load(open(spec_json, encoding="utf-8-sig")) if os.path.exists(spec_json) else {}
    if spec.get("Body") == "Pack":
        # Built from the pack's parts: the pack's own material instance for its texture, shared by
        # every character that wears it.
        # Slot 1 (material "M_<name>_Face"): the character's own face texture on the pack's material.
        face_material = None
        face_png = os.path.join(os.path.dirname(fbx), base + "_Face.png")
        if os.path.exists(face_png):
            face_texture = next(iter(import_task(face_png, destination, "T_" + base + "_Face")), None)
            tools = unreal.AssetToolsHelpers.get_asset_tools()
            mi_path = destination + "/MI_" + base + "_Face"
            face_material = unreal.load_asset(mi_path) if unreal.EditorAssetLibrary.does_asset_exist(mi_path) else \
                tools.create_asset("MI_" + base + "_Face", destination, unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
            unreal.MaterialEditingLibrary.set_material_instance_parent(face_material, unreal.load_asset(TOON_MATERIAL))
            unreal.MaterialEditingLibrary.set_material_instance_texture_parameter_value(face_material, "Texture", face_texture)
            unreal.EditorAssetLibrary.save_loaded_asset(face_material)
        body_material = kit_material_instance(spec.get("PackTexture", "T_Armies_Standard"))
        mesh.set_editor_property("materials", [
            unreal.SkeletalMaterial(material_interface=face_material if face_material and str(slot.material_slot_name).endswith("_Face") else body_material,
                                    material_slot_name=slot.material_slot_name)
            for slot in mesh.get_editor_property("materials")])
        if "--no-nanite" not in sys.argv:
            enable_nanite(mesh)
        return finish(mesh, fbx, mannequin, 1)

    texture = None
    png = os.path.join(os.path.dirname(fbx), base + "_Diffuse.png")
    if os.path.exists(png):
        texture = next(iter(import_task(png, destination, "T_" + base)), None)

    # The grime layer and the options the character was made with (for its grime amount).
    grime = None
    grime_png = os.path.join(os.path.dirname(fbx), base + "_Grime.png")
    if os.path.exists(grime_png):
        grime = next(iter(import_task(grime_png, destination, "T_" + base + "_Grime")), None)
    amount = float(spec.get("Grime", 0.0))

    # The same material set-up on either skeleton (the realistic body on the mannequin included).
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    mi_path = destination + "/MI_" + base
    if unreal.EditorAssetLibrary.does_asset_exist(mi_path):
        mi = unreal.load_asset(mi_path)
    else:
        mi = tools.create_asset("MI_" + base, destination, unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
    mel = unreal.MaterialEditingLibrary
    # With a grime layer: the layered material; otherwise the pack's own.
    mel.set_material_instance_parent(mi, layered_material() if grime is not None else unreal.load_asset(TOON_MATERIAL))
    if texture is not None:
        mel.set_material_instance_texture_parameter_value(mi, "Texture", texture)
    if grime is not None:
        mel.set_material_instance_texture_parameter_value(mi, "GrimeTexture", grime)
        mel.set_material_instance_scalar_parameter_value(mi, "GrimeAmount", amount)
    single_sided(mi)
    unreal.EditorAssetLibrary.save_loaded_asset(mi)

    mesh.set_editor_property("materials", [
        unreal.SkeletalMaterial(material_interface=mi, material_slot_name=slot.material_slot_name)
        for slot in mesh.get_editor_property("materials")])
    return finish(mesh, fbx, mannequin, lods)


def enable_nanite(mesh):
    """Nanite skinned mesh (UE 5.5+): rendered as clusters, so crowds cost little more than one character."""
    settings = mesh.get_editor_property("nanite_settings")
    settings.set_editor_property("enabled", True)
    mesh.set_editor_property("nanite_settings", settings)


KIT_MATERIAL = LAYERED_MATERIAL_DIR + "/M_LPCG_Kit"
SKIN_TINT_HLSL = """
float chroma = (C.r - C.g) / (C.r + 0.02);
float m = saturate((chroma - 0.35) / 0.15) * saturate((C.r - 0.1) / 0.1) * T2.y;
return lerp(C.rgb, C.rgb * pow(float3(T1.x, T1.y, T2.x) * 2, 2.2), m);   // the ratio is in sRGB
"""


def kit_material(rebuild=False):
    """
    The body material of pack-kit characters: the pack's look (one texture, default lit, no specular)
    with the skin tone from UV channels 1 and 2 (the vertex colours RG and BA; Nanite skinned meshes
    drop vertex colours): where A = 1, skin-coloured texels are multiplied by RGB * 2. One instance per
    pack texture is shared by every kit character. rebuild=True rebuilds an existing one's graph.
    """
    mel = unreal.MaterialEditingLibrary
    if unreal.EditorAssetLibrary.does_asset_exist(KIT_MATERIAL):
        mat = unreal.load_asset(KIT_MATERIAL)
        if not rebuild:
            return mat
        mel.delete_all_material_expressions(mat)
    else:
        tools = unreal.AssetToolsHelpers.get_asset_tools()
        mat = tools.create_asset("M_LPCG_Kit", LAYERED_MATERIAL_DIR, unreal.Material, unreal.MaterialFactoryNew())
    tex = mel.create_material_expression(mat, unreal.MaterialExpressionTextureSampleParameter2D, -600, 0)
    tex.set_editor_property("parameter_name", "Texture")
    tex.set_editor_property("texture", unreal.load_asset(PACK + "/Textures/T_Armies_Standard"))
    uv1 = mel.create_material_expression(mat, unreal.MaterialExpressionTextureCoordinate, -600, 300)
    uv1.set_editor_property("coordinate_index", 1)
    uv2 = mel.create_material_expression(mat, unreal.MaterialExpressionTextureCoordinate, -600, 400)
    uv2.set_editor_property("coordinate_index", 2)
    custom = mel.create_material_expression(mat, unreal.MaterialExpressionCustom, -250, 100)
    custom.set_editor_property("code", SKIN_TINT_HLSL)
    custom.set_editor_property("output_type", unreal.CustomMaterialOutputType.CMOT_FLOAT3)
    custom.set_editor_property("description", "Skin tint")
    inputs = []
    for name in ("C", "T1", "T2"):
        ci = unreal.CustomInput()
        ci.set_editor_property("input_name", name)
        inputs.append(ci)
    custom.set_editor_property("inputs", inputs)
    mel.connect_material_expressions(tex, "RGBA", custom, "C")
    mel.connect_material_expressions(uv1, "", custom, "T1")
    mel.connect_material_expressions(uv2, "", custom, "T2")
    mel.connect_material_property(custom, "", unreal.MaterialProperty.MP_BASE_COLOR)
    spec = mel.create_material_expression(mat, unreal.MaterialExpressionConstant, -250, 300)
    mel.connect_material_property(spec, "", unreal.MaterialProperty.MP_SPECULAR)
    mat.set_editor_property("used_with_skeletal_mesh", True)
    mat.set_editor_property("used_with_nanite", True)
    mel.recompile_material(mat)
    unreal.EditorAssetLibrary.save_loaded_asset(mat)
    return mat


def kit_material_instance(texture_name):
    """MI_LPCG_Kit_<texture>: the kit material on one of the pack's textures (its camo variants)."""
    path = LAYERED_MATERIAL_DIR + "/MI_LPCG_Kit_" + texture_name
    if unreal.EditorAssetLibrary.does_asset_exist(path):
        return unreal.load_asset(path)
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    mi = tools.create_asset("MI_LPCG_Kit_" + texture_name, LAYERED_MATERIAL_DIR, unreal.MaterialInstanceConstant,
                            unreal.MaterialInstanceConstantFactoryNew())
    mel = unreal.MaterialEditingLibrary
    mel.set_material_instance_parent(mi, kit_material())
    mel.set_material_instance_texture_parameter_value(mi, "Texture", unreal.load_asset(PACK + "/Textures/" + texture_name))
    unreal.EditorAssetLibrary.save_loaded_asset(mi)
    return mi


def pack_material(texture_name):
    """The pack's material instance that uses <texture_name>, or M_Armies_Parent for the standard texture."""
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    for data in registry.get_assets_by_path(PACK + "/Materials", recursive=False):
        mi = data.get_asset()
        if isinstance(mi, unreal.MaterialInstanceConstant):
            for p in mi.get_editor_property("texture_parameter_values"):
                if p.parameter_value is not None and p.parameter_value.get_name().lower() == texture_name.lower():
                    return mi
    return unreal.load_asset(TOON_MATERIAL)


def finish(mesh, fbx, mannequin, lods):
    physics = unreal.load_asset(MANNEQUIN_PHYSICS if mannequin else TOON_PHYSICS)
    if physics is not None:
        mesh.set_editor_property("physics_asset", physics)
    if lods > 1:
        add_lods(mesh, lods)
    unreal.EditorAssetLibrary.save_loaded_asset(mesh)

    unreal.log("LPCG imported %s -> %s (skeleton %s)" % (fbx, mesh.get_path_name(), mesh.skeleton.get_path_name()))
    return mesh


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    if not args:
        raise SystemExit("usage: import_unreal.py <folder with .fbx files> [/Game/Destination] [--mannequin]")
    folder = args[0]
    destination = args[1] if len(args) > 1 else "/Game/LowPolyCharGen"
    mannequin = "--mannequin" in argv
    lods = next((int(a.split("=", 1)[1]) for a in argv if a.startswith("--lods=")), DEFAULT_LODS)
    files = sorted(os.path.join(folder, f) for f in os.listdir(folder) if f.lower().endswith(".fbx"))
    for fbx in files:
        import_character(fbx, destination, mannequin, lods)
    unreal.log("LPCG done: %d character(s)" % len(files))


if __name__ == "__main__":
    main(sys.argv[1:])

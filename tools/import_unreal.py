"""Imports generated characters into an Unreal project.

Every <name>.fbx in a folder (with its <name>_Diffuse.png) becomes, under the destination folder:
  SK_<name>   skeletal mesh on the Toon Soldiers skeleton (or the UE5 mannequin's, see --mannequin)
  T_<name>    its texture
  T_<name>_Grime  its grime layer (RGB dirt, A coverage), when exported
  MI_<name>   a material instance of M_LPCG_Character (texture + grime layer, GrimeAmount from <name>.json)
and the pack's physics asset is assigned, so the character drops into anything built for the pack.

Run inside the editor (Tools > Execute Python Script, or the Output Log's Python console):
  py "C:/source/Claude/LowPolyCharGen/tools/import_unreal.py" "D:/out" [/Game/LowPolyCharGen] [--mannequin]
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
        if not mat.get_editor_property("used_with_skeletal_mesh"):
            # older copies lacked the flag: the meshes then render with the default grey material
            mat.set_editor_property("used_with_skeletal_mesh", True)
            unreal.MaterialEditingLibrary.recompile_material(mat)
            unreal.EditorAssetLibrary.save_loaded_asset(mat)
        return mat
    mel = unreal.MaterialEditingLibrary
    mat = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
        "M_LPCG_Character", LAYERED_MATERIAL_DIR, unreal.Material, unreal.MaterialFactoryNew())
    # set up front: without it some imported meshes get the default material in game and in thumbnails
    mat.set_editor_property("used_with_skeletal_mesh", True)
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


def import_character(fbx, destination, mannequin):
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
    options.skeletal_mesh_import_data.set_editor_property("normal_import_method", unreal.FBXNormalImportMethod.FBXNIM_IMPORT_NORMALS)
    imported = import_task(fbx, destination, "SK_" + base, options)
    mesh = next((a for a in imported if isinstance(a, unreal.SkeletalMesh)), None)
    if mesh is None:
        raise RuntimeError("FBX import produced no skeletal mesh: " + fbx)

    texture = None
    png = os.path.join(os.path.dirname(fbx), base + "_Diffuse.png")
    if os.path.exists(png):
        texture = next(iter(import_task(png, destination, "T_" + base)), None)

    # The grime layer and the options the character was made with (for its grime amount).
    grime = None
    grime_png = os.path.join(os.path.dirname(fbx), base + "_Grime.png")
    if os.path.exists(grime_png):
        grime = next(iter(import_task(grime_png, destination, "T_" + base + "_Grime")), None)
    amount = 0.0
    spec_json = os.path.join(os.path.dirname(fbx), base + "_Spec.json")
    if os.path.exists(spec_json):
        amount = float(json.load(open(spec_json, encoding="utf-8-sig")).get("Grime", 0.0))

    if not mannequin:
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
        unreal.EditorAssetLibrary.save_loaded_asset(mi)

        mesh.set_editor_property("materials", [
            unreal.SkeletalMaterial(material_interface=mi, material_slot_name=slot.material_slot_name)
            for slot in mesh.get_editor_property("materials")])
        physics = unreal.load_asset(TOON_PHYSICS)
        if physics is not None:
            mesh.set_editor_property("physics_asset", physics)
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
    files = sorted(os.path.join(folder, f) for f in os.listdir(folder) if f.lower().endswith(".fbx"))
    for fbx in files:
        import_character(fbx, destination, mannequin)
    unreal.log("LPCG done: %d character(s)" % len(files))


if __name__ == "__main__":
    main(sys.argv[1:])

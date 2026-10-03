"""Imports generated characters into an Unreal project.

Every <name>.fbx in a folder (with its <name>_Diffuse.png) becomes, under the destination folder:
  SK_<name>   skeletal mesh on the Toon Soldiers skeleton (or the UE5 mannequin's, see --mannequin)
  T_<name>    its texture
  MI_<name>   a material instance of the pack's M_Armies_Parent using that texture
and the pack's physics asset is assigned, so the character drops into anything built for the pack.

Run inside the editor (Tools > Execute Python Script, or the Output Log's Python console):
  py "C:/source/Claude/LowPolyCharGen/tools/import_unreal.py" "D:/out" [/Game/LowPolyCharGen] [--mannequin]
or headless:
  UnrealEditor-Cmd.exe Project.uproject -run=pythonscript -script="import_unreal.py D:/out /Game/LowPolyCharGen"
"""
import os
import sys

import unreal

PACK = "/Game/Toon_Soldiers_Armies"
TOON_SKELETON = PACK + "/Meshes/Characters_Skeleton"
TOON_PHYSICS = PACK + "/Meshes/Characters_PhysicsAsset"
TOON_MATERIAL = PACK + "/Materials/M_Armies_Parent"
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

    if not mannequin:
        tools = unreal.AssetToolsHelpers.get_asset_tools()
        mi_path = destination + "/MI_" + base
        mi = unreal.load_asset(mi_path) if unreal.EditorAssetLibrary.does_asset_exist(mi_path) else \
            tools.create_asset("MI_" + base, destination, unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
        unreal.MaterialEditingLibrary.set_material_instance_parent(mi, unreal.load_asset(TOON_MATERIAL))
        if texture is not None:
            unreal.MaterialEditingLibrary.set_material_instance_texture_parameter_value(mi, "Texture", texture)
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

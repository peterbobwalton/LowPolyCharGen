"""Step 2 of building the stand-alone soldier pack. Run inside the scratch project made by pack_export_stage.py:
    py "C:/source/Claude/LowPolyCharGen/tools/pack_export_build.py"

Moves (with references fixed) everything staged into one folder, characters separated by faction:

    /Game/LowPolySoldiers/
        Characters/Armies   SK_Armies_*   (+ Faces/ the per-character face textures and materials)
        Characters/SpecOps  SK_SpecOps_*
        Characters/Militia  SK_Militia_01..24
        Skeleton/           the shared skeleton and physics asset
        Animations/         the pack animations, by weapon type
        Weapons/Armies, Weapons/Militia
        Materials/, Textures/

then checks nothing in it references anything outside it, and copies it to C:/Temp/LowPolySoldiers_Pack, ready
to drop into another project's Content folder.
"""
import os
import re
import shutil

import unreal

ROOT = "/Game/LowPolySoldiers"
OUT = "C:/Temp/LowPolySoldiers_Pack"

registry = unreal.AssetRegistryHelpers.get_asset_registry()
tools = unreal.AssetToolsHelpers.get_asset_tools()
options = unreal.AssetRegistryDependencyOptions(
    include_soft_package_references=True, include_hard_package_references=True, include_searchable_names=False,
    include_soft_management_references=False, include_hard_management_references=False)


def character_name(name):
    """LPCG_Pack_Militia02 -> (Militia, Militia_02); LPCG_Pack_Armies_Soldier_A -> (Armies, Armies_Soldier_A)."""
    name = name.replace("LPCG_Pack_", "")
    m = re.match(r"Militia(\d+)$", name)
    if m:
        return "Militia", "Militia_" + m.group(1)
    return name.split("_", 1)[0], name


def destination(asset):
    package = str(asset.package_name)
    name = str(asset.asset_name)
    cls = str(asset.asset_class_path.asset_name)
    if package.startswith(ROOT + "/"):
        return None
    if "LPCG_Pack_" in name:
        faction, short = character_name(re.sub(r"^(SK|MI|T)_|_Face$", "", name))
        prefix = name.split("_", 1)[0]
        if cls == "SkeletalMesh":
            return "%s/Characters/%s" % (ROOT, faction), "SK_" + short
        return "%s/Characters/%s/Faces" % (ROOT, faction), "%s_%s_Face" % (prefix, short)
    if cls in ("Skeleton", "PhysicsAsset"):
        return ROOT + "/Skeleton", {"Characters_Skeleton": "SKEL_Soldier", "Characters_PhysicsAsset": "PHYS_Soldier"}.get(name, name)
    m = re.match(r"/Game/Toon_Soldiers_\w+/Animations(/.*)?/[^/]+$", package)
    if cls == "AnimSequence" and m:
        return ROOT + "/Animations" + (m.group(1) or ""), name
    m = re.match(r"/Game/Toon_Soldiers_(Armies|Militia)/Meshes/Weapons(/.*)?/[^/]+$", package)
    if cls == "StaticMesh" and m:
        return "%s/Weapons/%s%s" % (ROOT, m.group(1), m.group(2) or ""), name
    if cls.startswith("Material"):
        return ROOT + "/Materials", name
    if cls.startswith("Texture"):
        return ROOT + "/Textures", name
    return ROOT + "/Misc", name


moves = []
for asset in registry.get_assets_by_path("/Game", recursive=True):
    if str(asset.asset_class_path.asset_name) in ("ObjectRedirector", "World", "MapBuildDataRegistry"):
        continue
    target = destination(asset)
    if target is None:
        continue
    folder, new_name = target
    moves.append(unreal.AssetRenameData(asset.get_asset(), folder, new_name))
print("moving %d assets" % len(moves))
tools.rename_assets(moves)

# Point everything at the moved assets, then drop the redirectors and the old folders.
redirectors = [a.get_asset() for a in registry.get_assets_by_path("/Game", recursive=True)
               if str(a.asset_class_path.asset_name) == "ObjectRedirector"]
if redirectors:
    tools.fixup_referencers(redirectors)
unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True)
for folder in unreal.EditorAssetLibrary.list_assets("/Game", recursive=False, include_folder=True):
    if folder.rstrip("/") not in (ROOT, "/Game/Collections", "/Game/Developers") and not registry.get_assets_by_path(folder.rstrip("/"), recursive=True):
        unreal.EditorAssetLibrary.delete_directory(folder.rstrip("/"))

# Self-contained check: nothing under ROOT may reference /Game outside ROOT.
outside = set()
for asset in registry.get_assets_by_path(ROOT, recursive=True):
    for dep in registry.get_dependencies(asset.package_name, options) or []:
        dep = str(dep)
        if dep.startswith("/Game/") and not dep.startswith(ROOT + "/"):
            outside.add((str(asset.package_name), dep))
for pair in sorted(outside):
    print("OUTSIDE REFERENCE: %s -> %s" % pair)

# Copy the folder out.
project = os.path.normpath(unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir()))
source = os.path.join(project, "Content", "LowPolySoldiers")
target = os.path.join(OUT, "LowPolySoldiers")
if os.path.isdir(target):
    shutil.rmtree(target)
shutil.copytree(source, target)
count = {}
for asset in registry.get_assets_by_path(ROOT, recursive=True):
    key = str(asset.asset_class_path.asset_name)
    count[key] = count.get(key, 0) + 1
print("pack: %s  %s  outside references: %d" % (target, count, len(outside)))

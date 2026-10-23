"""Step 1 of building the stand-alone soldier pack: copy the 44 kit-rebuilt characters, the pack animations and
weapons, and everything they reference, into a scratch content-only project (C:/Temp/LpcgPackBuilder).

Run inside the LowPolyCharTest editor:
    py "C:/source/Claude/LowPolyCharGen/tools/pack_export_stage.py"
Then open the scratch project and run tools/pack_export_build.py there; it moves everything under one folder
(/Game/LowPolySoldiers) with the references fixed, so that folder can be copied into any project's Content.
"""
import os
import shutil

import unreal

PROJECT = os.path.normpath(unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir()))
BUILDER = "C:/Temp/LpcgPackBuilder"

ROOTS = [
    ("/Game/LowPolyCharGen/Kit/Pack", "SkeletalMesh"),
    ("/Game/Toon_Soldiers_Armies/Animations", "AnimSequence"),
    ("/Game/Toon_Soldiers_Armies/Meshes/Weapons", "StaticMesh"),
    ("/Game/Toon_Soldiers_Militia/Meshes/Weapons", "StaticMesh"),
]

registry = unreal.AssetRegistryHelpers.get_asset_registry()
options = unreal.AssetRegistryDependencyOptions(
    include_soft_package_references=False, include_hard_package_references=True, include_searchable_names=False,
    include_soft_management_references=False, include_hard_management_references=False)

todo = []
for path, cls in ROOTS:
    todo += [str(a.package_name) for a in registry.get_assets_by_path(path, recursive=True)
             if str(a.asset_class_path.asset_name) == cls]
packages = set()
while todo:
    package = todo.pop()
    if package in packages or not package.startswith("/Game/"):
        continue
    packages.add(package)
    todo += [str(d) for d in (registry.get_dependencies(package, options) or [])]

unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True)
content = os.path.join(PROJECT, "Content")
copied = 0
for package in sorted(packages):
    relative = package[len("/Game/"):]
    folder, name = os.path.split(os.path.join(content, relative))
    target = os.path.join(BUILDER, "Content", os.path.dirname(relative))
    os.makedirs(target, exist_ok=True)
    for f in os.listdir(folder):
        if os.path.splitext(f)[0] == name:
            shutil.copy2(os.path.join(folder, f), os.path.join(target, f))
            copied += 1

# A content-only project on the same engine, with the bridge and Python plugins.
os.makedirs(os.path.join(BUILDER, "Config"), exist_ok=True)
with open(os.path.join(BUILDER, "LpcgPackBuilder.uproject"), "w") as f:
    f.write('{\n\t"FileVersion": 3,\n\t"EngineAssociation": "5.8",\n\t"Description": "Builds the LowPolySoldiers pack",\n'
            '\t"Plugins": [\n\t\t{"Name": "PythonScriptPlugin", "Enabled": true},\n'
            '\t\t{"Name": "UnrealMcpBridge", "Enabled": true}\n\t]\n}\n')
engine_ini = open(os.path.join(PROJECT, "Config", "DefaultEngine.ini")).read().splitlines()
with open(os.path.join(BUILDER, "Config", "DefaultEngine.ini"), "w") as f:
    f.write("\n".join(l for l in engine_ini if "/Script/LowPolyCharTest" not in l and "Lpcg" not in l) + "\n")
plugin = os.path.join(PROJECT, "Plugins", "UnrealMcpBridge")
if os.path.isdir(plugin) and not os.path.isdir(os.path.join(BUILDER, "Plugins", "UnrealMcpBridge")):
    shutil.copytree(plugin, os.path.join(BUILDER, "Plugins", "UnrealMcpBridge"),
                    ignore=shutil.ignore_patterns("Intermediate"))
print("staged %d packages (%d files) into %s" % (len(packages), copied, BUILDER))

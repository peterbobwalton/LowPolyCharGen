"""Builds /Game/LowPolyCharGen/Maps/Lvl_Characters in LowPolyCharTest: a large open floor with daylight, sky and
fog, and the generated characters in rows facing the camera start, each with the pack's own soldier first for
comparison. Rebuilds the level from scratch each run (the characters must be imported to /Game/LowPolyCharGen/Kit).
Run inside the open editor:
    py "C:/source/Claude/LowPolyCharGen/tools/unreal_character_level.py"
"""
import unreal

LEVEL = "/Game/LowPolyCharGen/Maps/Lvl_Characters"
ARMIES = "/Game/Toon_Soldiers_Armies"
MILITIA = "/Game/Toon_Soldiers_Militia"
KIT = "/Game/LowPolyCharGen/Kit/SK_LPCG_"
AW = ARMIES + "/Meshes/Weapons/"
MW = MILITIA + "/Meshes/Weapons/"

# Rows from the front: (mesh, weapon, label line 1, label line 2)
ROWS = [
    [
        (ARMIES + "/Meshes/Characters_Prebuilt/SK_Armies_Soldier_A", AW + "SM_m4", "Armies pack", "reference"),
        (KIT + "Kit_Rifleman", AW + "SM_m4", "Rifleman", "stubble"),
        (KIT + "Kit_Heavy", AW + "SM_m249", "Heavy gunner", "build 1.0, broad nose"),
        (KIT + "Kit_Scout_Slim", AW + "SM_scar", "Scout", "build 0.0, moustache"),
        (KIT + "Kit_Tee_Slim", AW + "SM_m4", "T-shirt", "slim, fair, stubble"),
        (KIT + "Kit_Tee", AW + "SM_m4", "T-shirt", "standard"),
        (KIT + "Kit_Tee_Heavy", AW + "SM_m4", "T-shirt", "heavy, beard"),
        (KIT + "Kit_Tee_Urban", AW + "SM_mp5", "Recoloured", "clothes, bare hands"),
    ],
    [
        (ARMIES + "/Meshes/Characters_Prebuilt/SK_Armies_Soldier_B", AW + "SM_g36c", "Armies pack", "reference"),
        (KIT + "Kit_Female_Tee", AW + "SM_m4", "Female", "long hair"),
        (KIT + "Kit_Female_Rifleman", AW + "SM_g36c", "Female", "helmet, ponytail"),
        (KIT + "Kit_Female_Slim", AW + "SM_mp5", "Female", "beanie, bun"),
        (KIT + "Kit_Female_Bob", AW + "SM_usp", "Female", "bob, fair"),
    ],
    [
        (MILITIA + "/Meshes/Characters_Prebuilt/SK_Militia01", MW + "SM_weapon_ak47", "Militia pack", "reference"),
        (KIT + "Mil_Turban_Thobe", MW + "SM_weapon_ak47", "Militia", "turban, thobe"),
        (KIT + "Mil_Keffiyeh_Thobe", MW + "SM_weapon_rpg", "Militia", "keffiyeh, thobe"),
        (KIT + "Mil_Thobe_Grey", MW + "SM_weapon_pkm", "Militia", "turban, grey thobe"),
        (KIT + "Mil_Turban_Jeans", MW + "SM_weapon_aksu", "Militia", "turban, trainers"),
        (KIT + "Mil_Beanie", MW + "SM_weapon_fal", "Militia", "beanie, beard"),
        (KIT + "Mil_Shemagh", MW + "SM_weapon_ak47", "Militia", "shemagh"),
        (KIT + "Mil_Shemagh_Black", MW + "SM_weapon_svd", "Militia", "black shemagh"),
        (KIT + "Mil_Balaclava", MW + "SM_weapon_mac10", "Militia", "balaclava"),
    ],
]
SPACING = 180.0      # between characters in a row (cm)
ROW_GAP = 450.0      # between rows
FLOOR_SIZE = 200.0   # metres

les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
ues = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)

if unreal.EditorAssetLibrary.does_asset_exist(LEVEL):
    les.load_level(LEVEL)
    for a in eas.get_all_level_actors():
        eas.destroy_actor(a)
else:
    les.new_level(LEVEL)


def spawn(cls, location, rotation=unreal.Rotator(0, 0, 0), label=None):
    actor = eas.spawn_actor_from_class(cls, location, rotation)
    if label:
        actor.set_actor_label(label)
    return actor


# Floor: one large plane with the template's grid material.
floor = spawn(unreal.StaticMeshActor, unreal.Vector(0, 0, 0), label="Floor")
floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Plane"))
floor.static_mesh_component.set_material(0, unreal.load_asset("/Game/LevelPrototyping/Materials/MI_PrototypeGrid_Gray"))
floor.set_actor_scale3d(unreal.Vector(FLOOR_SIZE, FLOOR_SIZE, 1))

# Daylight: sun (atmosphere sun light), sky atmosphere, real-time sky light, height fog.
sun = spawn(unreal.DirectionalLight, unreal.Vector(0, 0, 1000), unreal.Rotator(0, -42, 135), "Sun")
sun.light_component.set_editor_property("atmosphere_sun_light", True)
sun.light_component.set_intensity(8.0)
spawn(unreal.SkyAtmosphere, unreal.Vector(0, 0, 0), label="SkyAtmosphere")
sky = spawn(unreal.SkyLight, unreal.Vector(0, 0, 500), label="SkyLight")
sky.light_component.set_editor_property("real_time_capture", True)
fog = spawn(unreal.ExponentialHeightFog, unreal.Vector(0, 0, 0), label="HeightFog")
fog.component.set_editor_property("fog_density", 0.004)

# Characters: rows across Y, front row nearest the camera start; they face -X (towards it).
for r, row in enumerate(ROWS):
    x = r * ROW_GAP
    for i, (mesh, weapon, line1, line2) in enumerate(row):
        y = (i - (len(row) - 1) / 2.0) * SPACING
        actor = spawn(unreal.LpcgDisplaySoldier, unreal.Vector(x, y, 0), unreal.Rotator(0, 0, 180),
                      "Char_%d_%02d_%s" % (r, i, (line1 + "_" + line2).replace(" ", "_").replace(",", "")))
        actor.set_editor_property("mesh", unreal.load_asset(mesh))
        actor.set_editor_property("animation", None)
        actor.set_editor_property("locomotion_speed", 0.0)
        actor.set_editor_property("crouched", False)
        actor.set_editor_property("weapon_mesh", unreal.load_asset(weapon))
        actor.set_editor_property("grime", -1.0)
        actor.set_editor_property("label", line1 + chr(10) + line2)
        actor.refresh()

start = spawn(unreal.PlayerStart, unreal.Vector(-700, 0, 100), unreal.Rotator(0, 0, 0), "PlayerStart")
world = ues.get_editor_world()
world.get_world_settings().set_editor_property("default_game_mode", unreal.LpcgGameMode)
les.save_current_level()
ues.set_level_viewport_camera_info(unreal.Vector(-650, 0, 260), unreal.Rotator(0, -12, 0))
print("built %s with %d characters" % (LEVEL, sum(len(r) for r in ROWS)))

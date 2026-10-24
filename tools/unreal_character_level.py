"""Builds /Game/LowPolyCharGen/Maps/Lvl_Characters in LowPolyCharTest: a large open floor with daylight, sky and
fog, and the generated characters in rows facing the camera start, each with the pack's own soldier first for
comparison. Rebuilds the level from scratch each run (the characters must be imported to /Game/LowPolyCharGen/Kit).
Run inside the open editor:
    py "C:/source/Claude/LowPolyCharGen/tools/unreal_character_level.py"
"""
import os

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
        (KIT + "Kit_Tee_Urban", AW + "SM_mp5", "Recoloured", "clothes, shoes, bare hands"),
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
        (KIT + "Mil_Keffiyeh_Thobe", MW + "SM_weapon_rpg", "Militia", "keffiyeh, thobe, shoes"),
        (KIT + "Mil_Thobe_Grey", MW + "SM_weapon_pkm", "Militia", "turban, grey thobe"),
        (KIT + "Mil_Turban_Jeans", MW + "SM_weapon_aksu", "Militia", "turban, trainers"),
        (KIT + "Mil_Beanie", MW + "SM_weapon_fal", "Militia", "beanie, beard"),
        (KIT + "Mil_Shemagh", MW + "SM_weapon_ak47", "Militia", "shemagh"),
        (KIT + "Mil_Shemagh_Black", MW + "SM_weapon_svd", "Militia", "black shemagh"),
        (KIT + "Mil_Balaclava", MW + "SM_weapon_mac10", "Militia", "balaclava"),
    ],
]
# The pack's 44 prebuilt characters, each with its kit rebuild (tools/pack_prebuilts.txt, imported to
# /Game/LowPolyCharGen/Kit/Pack) right behind it: rows of 11, original in front.
PACK_KIT = "/Game/LowPolyCharGen/Kit/Pack/SK_LPCG_Pack_"
PREBUILTS = (["Armies_" + n for n in ("Engineer", "OffDuty", "Officer", "Recon_A", "Recon_B", "Sniper_A", "Sniper_B",
                                      "Soldier_A", "Soldier_B", "Soldier_C")]
             + ["SpecOps_" + n for n in ("Officer", "Recon_A", "Recon_B", "Recon_C", "Sniper_A", "Sniper_B",
                                         "Soldier_A", "Soldier_B", "Soldier_C", "Support")]
             + ["Militia%02d" % i for i in range(1, 25)])
for start in range(0, len(PREBUILTS), 11):
    names = PREBUILTS[start:start + 11]
    original, rebuilt = [], []
    for n in names:
        militia = n.startswith("Militia")
        folder = (MILITIA if militia else ARMIES) + "/Meshes/Characters_Prebuilt/SK_"
        weapon = MW + "SM_weapon_ak47" if militia else AW + "SM_m4"
        original.append((folder + n, weapon, n, "pack prebuilt"))
        rebuilt.append((PACK_KIT + n, weapon, n, "kit rebuild"))
    ROWS += [original, rebuilt]
# The UE5 pack's 12 prebuilts (their own skeleton: they play the UE5 pack's idle), then their kit rebuilds on the
# shared skeleton in the pack's blue and in the olive digital camo.
UE5 = "/Game/Toon_Soldiers_UE5"
UE5_IDLE = UE5 + "/Animations/Infantry/infantry_combat_idle"
UE5_PREBUILTS = ["Basic", "Engineer", "Heavy", "Medic", "Officer_A", "Officer_B", "Officer_C", "Pyro", "Scout", "Sniper",
                 "Soldier", "Trench"]
ROWS += [
    [(UE5 + "/Meshes/Characters_Prebuilt/SK_" + ("Enginieer" if n == "Engineer" else n),
      AW + "SM_m4", "UE5 " + n, "pack prebuilt") for n in UE5_PREBUILTS],
    [(PACK_KIT + "UE5_" + n, AW + "SM_m4", "UE5 " + n, "kit rebuild") for n in UE5_PREBUILTS],
    [(PACK_KIT + "UE5Camo_" + n, AW + "SM_m4", "UE5 " + n, "kit, digital camo") for n in UE5_PREBUILTS],
]
# The factions of tools/pack_factions.txt (imported to Kit/Pack with the prebuilts): a row each, with their weapons.
FACTIONS_TXT = os.path.join(os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
                            else "C:/Source/Claude/LowPolyCharGen/tools", "pack_factions.txt")
factions = {}
for line in open(FACTIONS_TXT, encoding="utf-8"):
    if line.startswith("#") or not line.strip():
        continue
    fields = line.strip().split("|")
    name, weapon = fields[0], fields[6] if len(fields) > 6 else "SM_m4"
    faction, role = name.split("_", 1)
    weapon_path = next((f + weapon for f in (AW, MW) if unreal.EditorAssetLibrary.does_asset_exist(f + weapon)), AW + "SM_m4")
    factions.setdefault(faction, []).append((PACK_KIT + name, weapon_path, faction, role.replace("_", " ")))
ROWS += list(factions.values())
ANIMS = ARMIES + "/Animations/"
# Weapons that aren't held like a rifle get the pack's own idle for them (rifles use the C++ locomotion idle).
WEAPON_IDLE = {
    "SM_weapon_rpg": ANIMS + "RocketLauncher/rocketlauncher_combat_idle",
    "SM_smaw": ANIMS + "RocketLauncher/rocketlauncher_combat_idle",
    "SM_javelin": ANIMS + "RocketLauncher/rocketlauncher_combat_idle",
    "SM_weapon_pkm": ANIMS + "Machinegun/machinegun_combat_idle",
    "SM_m249": ANIMS + "Machinegun/machinegun_combat_idle",
    "SM_usp": ANIMS + "Handgun/handgun_combat_idle",
    "SM_beretta92": ANIMS + "Handgun/handgun_combat_idle",
    "SM_weapon_glock": ANIMS + "Handgun/handgun_combat_idle",
    "SM_weapon_deserteagle": ANIMS + "Handgun/handgun_combat_idle",
    "SM_usp_sil": ANIMS + "Handgun/handgun_combat_idle",
}
SPACING = 240.0      # between characters in a row (cm), room for the labels
ROW_GAP = 600.0      # between rows
FLOOR_SIZE = 200.0   # metres

les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
ues = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)

TAG = "LpcgShowcase"   # every actor this script spawns; only these are ever removed

if unreal.EditorAssetLibrary.does_asset_exist(LEVEL):
    les.load_level(LEVEL)
    # Never destroy the level's own actors (WorldSettings, brushes): they are rooted, and destroying them
    # crashes the editor later (Assertion failed: !IsRooted()).
    for a in eas.get_all_level_actors():
        if TAG in [str(t) for t in a.tags] or a.get_actor_label().startswith("Char_") or a.get_actor_label() in (
                "Floor", "Sun", "SkyAtmosphere", "SkyLight", "HeightFog", "PlayerStart"):
            eas.destroy_actor(a)
else:
    les.new_level(LEVEL)


def spawn(cls, location, rotation=unreal.Rotator(0, 0, 0), label=None):
    actor = eas.spawn_actor_from_class(cls, location, rotation)
    actor.tags = [TAG]
    if label:
        actor.set_actor_label(label)
    return actor


# Floor: one large plane with the template's grid material.
floor = spawn(unreal.StaticMeshActor, unreal.Vector((len(ROWS) - 1) * ROW_GAP / 2.0, 0, 0), label="Floor")   # under every row
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
        idle = UE5_IDLE if mesh.startswith(UE5) else WEAPON_IDLE.get(weapon.rsplit("/", 1)[-1])
        actor.set_editor_property("animation", unreal.load_asset(idle) if idle else None)
        actor.set_editor_property("locomotion_speed", 0.0)
        actor.set_editor_property("crouched", False)
        actor.set_editor_property("weapon_mesh", unreal.load_asset(weapon))
        if mesh.startswith(UE5):
            # The UE5 skeleton is 1.18 times the shared one (its own weapons are scaled to match): shown at the
            # same height as the rebuilds, without a weapon.
            actor.set_editor_property("weapon_mesh", None)
            actor.set_editor_property("character_scale", actor.get_editor_property("character_scale") * 122.35 / 144.51)
        actor.set_editor_property("grime", -1.0)
        actor.set_editor_property("label", line1 + chr(10) + line2)
        actor.refresh()
        actor.name_text.set_editor_property("world_size", 16.0)   # readable from the camera start

start = spawn(unreal.PlayerStart, unreal.Vector(-700, 0, 100), unreal.Rotator(0, 0, 0), "PlayerStart")
world = ues.get_editor_world()
world.get_world_settings().set_editor_property("default_game_mode", unreal.LpcgGameMode)
les.save_current_level()
ues.set_level_viewport_camera_info(unreal.Vector(-900, 0, 330), unreal.Rotator(0, -14, 0))
print("built %s with %d characters" % (LEVEL, sum(len(r) for r in ROWS)))

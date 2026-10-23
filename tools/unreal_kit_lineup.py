"""Places the pack-kit comparison row (ALpcgDisplaySoldier actors labelled LPCG_KIT_*) in Lvl_ThirdPerson,
in front of the main lineup: the pack's Soldier A first, then characters built from the pack's parts by
Body = Pack (imported to /Game/LowPolyCharGen/Kit). Run inside the open editor:
    py "C:/source/Claude/LowPolyCharGen/tools/unreal_kit_lineup.py"
"""
import unreal

PACK = "/Game/Toon_Soldiers_Armies"
KIT = "/Game/LowPolyCharGen/Kit/SK_LPCG_Kit_"
W = PACK + "/Meshes/Weapons/"
ROW = [
    (PACK + "/Meshes/Characters_Prebuilt/SK_Armies_Soldier_A", "SM_m4", "Pack soldier A", "reference"),
    (KIT + "Rifleman", "SM_m4", "Kit rifleman", "standard build"),
    (KIT + "Heavy", "SM_m249", "Kit heavy", "build 1.0"),
    (KIT + "Scout_Slim", "SM_scar", "Kit scout", "build 0.0"),
    (KIT + "Tee_Slim", "SM_m4", "Kit T-shirt", "build 0.0"),
    (KIT + "Tee", "SM_m4", "Kit T-shirt", "standard build"),
    (KIT + "Tee_Heavy", "SM_m4", "Kit T-shirt", "build 1.0"),
    (KIT + "Female_Tee", "SM_m4", "Kit female", "long hair"),
    (KIT + "Female_Rifleman", "SM_g36c", "Kit female", "rifleman, backpack"),
    (KIT + "Female_Slim", "SM_mp5", "Kit female", "beanie, bun, button nose"),
    (KIT + "Female_Bob", "SM_usp", "Kit female", "bob, fair"),
]
X, Y0, STEP = 350.0, -1560.0, 105.0

eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()


def ground(x, y):
    """Height of the floor under (x, y): the actor's origin is at its feet."""
    hit = unreal.SystemLibrary.line_trace_single(world, unreal.Vector(x, y, 1000), unreal.Vector(x, y, -500),
                                                 unreal.TraceTypeQuery.ECC_VISIBILITY, False, [],
                                                 unreal.DrawDebugTrace.NONE, True)
    return hit.to_tuple()[4].z if hit else 0.0


for a in eas.get_all_level_actors():
    if a.get_actor_label().startswith("LPCG_KIT_"):
        eas.destroy_actor(a)
for i, (mesh, weapon, line1, line2) in enumerate(ROW):
    actor = eas.spawn_actor_from_class(unreal.LpcgDisplaySoldier, unreal.Vector(X, Y0 + i * STEP, ground(X, Y0 + i * STEP)), unreal.Rotator(0, 0, 180))
    actor.set_actor_label("LPCG_KIT_%02d_%s" % (i, line1.replace(" ", "_")))
    actor.set_editor_property("mesh", unreal.load_asset(mesh))
    actor.set_editor_property("animation", None)
    actor.set_editor_property("locomotion_speed", 0.0)
    actor.set_editor_property("crouched", False)
    actor.set_editor_property("weapon_mesh", unreal.load_asset(W + weapon))
    actor.set_editor_property("grime", -1.0)
    actor.set_editor_property("label", line1 + chr(10) + line2)
    actor.refresh()
les.save_current_level()
print("placed %d" % len(ROW))

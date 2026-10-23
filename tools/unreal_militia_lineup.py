"""Places the pack-kit militia row (LPCG_MIL_*) in Lvl_ThirdPerson, in front of the kit row: the Militia pack's
own SK_Militia01 first, then the militia characters built by Body = Pack (imported to /Game/LowPolyCharGen/Kit).
Run inside the open editor:
    py "C:/source/Claude/LowPolyCharGen/tools/unreal_militia_lineup.py"
"""
import unreal

MIL = "/Game/Toon_Soldiers_Militia"
KIT = "/Game/LowPolyCharGen/Kit/SK_LPCG_Mil_"
W = MIL + "/Meshes/Weapons/"
ROW = [
    (MIL + "/Meshes/Characters_Prebuilt/SK_Militia01", "SM_weapon_ak47", "Militia pack 01", "reference"),
    (KIT + "Turban_Thobe", "SM_weapon_ak47", "Kit militia", "turban, thobe, beard"),
    (KIT + "Keffiyeh_Thobe", "SM_weapon_rpg", "Kit militia", "keffiyeh, thobe"),
    (KIT + "Thobe_Grey", "SM_weapon_pkm", "Kit militia", "turban, grey thobe, heavy"),
    (KIT + "Turban_Jeans", "SM_weapon_aksu", "Kit militia", "turban, jeans"),
    (KIT + "Beanie", "SM_weapon_fal", "Kit militia", "beanie, beard"),
    (KIT + "Shemagh", "SM_weapon_ak47", "Kit militia", "shemagh"),
    (KIT + "Shemagh_Black", "SM_weapon_svd", "Kit militia", "black shemagh"),
    (KIT + "Balaclava", "SM_weapon_mac10", "Kit militia", "balaclava"),
]
X, Y0, STEP = 120.0, -1460.0, 105.0

eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()


def ground(x, y):
    hit = unreal.SystemLibrary.line_trace_single(world, unreal.Vector(x, y, 1000), unreal.Vector(x, y, -500),
                                                 unreal.TraceTypeQuery.ECC_VISIBILITY, False, [],
                                                 unreal.DrawDebugTrace.NONE, True)
    return hit.to_tuple()[4].z if hit else 0.0


for a in eas.get_all_level_actors():
    if a.get_actor_label().startswith("LPCG_MIL_"):
        eas.destroy_actor(a)
for i, (mesh, weapon, line1, line2) in enumerate(ROW):
    y = Y0 + i * STEP
    actor = eas.spawn_actor_from_class(unreal.LpcgDisplaySoldier, unreal.Vector(X, y, ground(X, y)), unreal.Rotator(0, 0, 180))
    actor.set_actor_label("LPCG_MIL_%02d_%s" % (i, line1.replace(" ", "_")))
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

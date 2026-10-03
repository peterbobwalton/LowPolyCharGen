"""Places the LowPolyCharTest showcase lineup (ALpcgDisplaySoldier actors) in Lvl_ThirdPerson, switches the map to
the C++ game mode, then runs Play-in-Editor and takes screenshots (Saved/Screenshots) before quitting.

Needs several editor frames, so run it from a temporary Content/Python/init_unreal.py (which deletes itself) rather
than -ExecutePythonScript, which quits the editor straight after the script returns. Results go to
Saved/LpcgLineup.txt.
"""
import math, os, traceback, unreal

LOG = open(os.path.join(unreal.Paths.project_saved_dir(), "LpcgLineup.txt"), "w")
def log(m):
    LOG.write(str(m) + "\n"); LOG.flush()

PACK = "/Game/Toon_Soldiers_Armies"
DEST = "/Game/LowPolyCharGen/Characters"
A = PACK + "/Animations/"
W = PACK + "/Meshes/Weapons/"

# (mesh, clip or None for the C++ locomotion blend, locomotion speed, crouched, weapon, label line 1, label line 2)
LINEUP = [
    (PACK + "/Meshes/Characters_Prebuilt/SK_Armies_Soldier_A", A + "Infantry/infantry_combat_idle", 0, False, "SM_m4", "Pack soldier A", "reference"),
    (DEST + "/SK_LPCG_Desert_Rifleman", None, 0, False, "SM_m4", "Desert rifleman", "C++ idle"),
    (DEST + "/SK_LPCG_Desert_Scout", None, 111, False, "SM_scar", "Desert scout", "C++ walk"),
    (DEST + "/SK_LPCG_Snow_Rifleman", None, 240, False, "SM_ak12", "Snow rifleman", "C++ walk-run blend"),
    (DEST + "/SK_LPCG_Snow_Scout", None, 369, False, "SM_m249", "Snow scout", "C++ run"),
    (DEST + "/SK_LPCG_Jungle_Rifleman", None, 0, True, "SM_g36c", "Jungle rifleman", "C++ crouch"),
    (DEST + "/SK_LPCG_Jungle_Scout", None, 107, True, "SM_mp5", "Jungle scout", "C++ crouch walk"),
    (DEST + "/SK_LPCG_Trooper", A + "Handgun/Guard/handgun_guard_idle", 0, False, "SM_usp", "Trooper", "handgun idle"),
    (DEST + "/SK_LPCG_Ranger", A + "RocketLauncher/Guard/rocketlauncher_guard_idle", 0, False, "SM_smaw", "Ranger", "rocket idle"),
    (DEST + "/SK_LPCG_Desert_Medic", A + "Handgun/handgun_combat_idle", 0, False, "SM_usp", "Desert medic", "female, handgun idle"),
    (DEST + "/SK_LPCG_Jungle_Sniper", None, 111, False, "SM_m110", "Jungle sniper", "female, C++ walk"),
    (DEST + "/SK_LPCG_Civilian_Hoodie", None, 111, False, None, "Civilian", "hoodie, jeans, C++ walk"),
    (DEST + "/SK_LPCG_Civilian_Tee", None, 0, False, None, "Civilian", "female, tee, jeans"),
    (DEST + "/SK_LPCG_Militia_Balaclava", A + "Infantry/Guard/infantry_guard_idle", 0, False, "SM_ak12", "Militia", "balaclava"),
    (DEST + "/SK_LPCG_Militia_Shemagh", None, 111, False, "SM_galil", "Militia", "shemagh, C++ walk"),
    (DEST + "/SK_LPCG_Militia_Female", None, 0, False, "SM_ak12", "Militia", "female, shemagh"),
]

les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
ues = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
state = {"tick": 0, "phase": "build"}

def build():
    les.load_level("/Game/ThirdPerson/Lvl_ThirdPerson")
    actors = eas.get_all_level_actors()
    for a in actors:
        if a.get_actor_label().startswith("LPCG_"):
            eas.destroy_actor(a)
    start = next((a for a in actors if isinstance(a, unreal.PlayerStart)), None)
    origin = start.get_actor_location() if start else unreal.Vector(0, 0, 100)
    yaw = start.get_actor_rotation().yaw if start else 0.0
    ground = origin.z - 90
    fwd = unreal.Vector(math.cos(math.radians(yaw)), math.sin(math.radians(yaw)), 0)
    right = unreal.Vector(-fwd.y, fwd.x, 0)
    for i, (mesh, clip, speed, crouch, weapon, line1, line2) in enumerate(LINEUP):
        loc = origin + fwd * 700 + right * ((i - (len(LINEUP) - 1) / 2.0) * 160)
        loc.z = ground
        actor = eas.spawn_actor_from_class(unreal.LpcgDisplaySoldier, loc, unreal.Rotator(0, 0, yaw + 180))
        actor.set_actor_label("LPCG_%02d_%s" % (i, line1.replace(" ", "_")))
        actor.set_editor_property("mesh", unreal.load_asset(mesh))
        actor.set_editor_property("animation", unreal.load_asset(clip) if clip else None)
        actor.set_editor_property("locomotion_speed", float(speed))
        actor.set_editor_property("crouched", crouch)
        actor.set_editor_property("weapon_mesh", unreal.load_asset(W + weapon) if weapon else None)
        actor.set_editor_property("label", line1 + chr(10) + line2)
        actor.refresh()
        log("placed " + line1)
    # The template map overrides the game mode with its Blueprint one; use the C++ game mode instead.
    ws = ues.get_editor_world().get_world_settings()
    ws.set_editor_property("default_game_mode", unreal.LpcgGameMode)
    les.save_current_level()
    log("saved level")
    state["view"] = (origin + fwd * 120 + unreal.Vector(0, 0, 60), unreal.Rotator(0, -4, yaw))

def tick(dt):
    try:
        state["tick"] += 1
        t = state["tick"]
        if state["phase"] == "build" and t == 30:
            build()
            ues.set_level_viewport_camera_info(state["view"][0], state["view"][1])
            state["phase"] = "editor_shot"; state["at"] = t
        elif state["phase"] == "editor_shot" and t == state["at"] + 240:
            unreal.SystemLibrary.execute_console_command(ues.get_editor_world(), "HighResShot 1920x1080 filename=LPCG_editor_lineup")
            state["phase"] = "pie"; state["at"] = t
        elif state["phase"] == "pie" and t == state["at"] + 60:
            les.editor_request_begin_play()
            state["phase"] = "pie_shot"; state["at"] = t
        elif state["phase"] == "pie_shot" and t == state["at"] + 300:
            world = ues.get_game_world()
            pawn = unreal.GameplayStatics.get_player_pawn(world, 0) if world else None
            anim = pawn.mesh.get_anim_instance() if pawn else None
            log("PIE pawn %s anim %s" % (pawn.get_class().get_name() if pawn else None, anim.get_class().get_name() if anim else None))
            if world:
                unreal.SystemLibrary.execute_console_command(world, "HighResShot 1920x1080 filename=LPCG_pie")
            state["phase"] = "end"; state["at"] = t
        elif state["phase"] == "end" and t == state["at"] + 120:
            les.editor_request_end_play()
            state["phase"] = "quit"; state["at"] = t
        elif state["phase"] == "quit" and t == state["at"] + 60:
            log("done")
            LOG.close()
            unreal.unregister_slate_post_tick_callback(state["handle"])
            unreal.SystemLibrary.quit_editor()
    except Exception:
        log(traceback.format_exc())
        state["phase"] = "quit"; state["at"] = state["tick"]

if globals().get("LPCG_INTERACTIVE"):
    # Run from an open editor (see lpct_refresh.py): just rebuild the lineup, no Play-in-Editor, no quitting.
    build()
    LOG.close()
else:
    state["handle"] = unreal.register_slate_post_tick_callback(tick)

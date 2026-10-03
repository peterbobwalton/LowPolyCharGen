"""Play-in-Editor foot-slip test for LowPolyCharTest.

Starts PIE, drives the player soldier forward at its normal speed, then sprinting, then crouched, and
measures how fast each planted foot (toe bone, while at its lowest) moves over the ground. A foot that
does not slide moves at ~0 cm/s while planted. Results go to Saved/LpcgFootSlip.txt, then the editor
quits. Run it like unreal_lineup.py, from a temporary Content/Python/init_unreal.py.
"""
import json, math, os, traceback, unreal

LOG = open(os.path.join(unreal.Paths.project_saved_dir(), "LpcgFootSlip.txt"), "w")
def log(m):
    LOG.write(str(m) + "\n"); LOG.flush()

les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
ues = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
TOES = ("Bip001-L-Toe0", "Bip001-R-Toe0")
FLOOR_Z = -10000.0
START = unreal.Vector(0, -15000, FLOOR_Z + 120)
PHASES = [("walk", 220), ("move", 220), ("sprint", 200), ("crouch", 220)]
state = {"tick": 0, "step": "start", "samples": [], "phase": 0, "t0": 0}

RAW = {}

def analyse(name, samples):
    RAW[name] = [[s[0]] + [[p.x, p.y, p.z] for p in s[1]] + [s[2]] for s in samples]
    # samples: (time, [toe positions]); planted = within 1.5 cm of that toe's lowest height in the run
    speeds = []
    for f in range(len(TOES)):
        zmin = min(s[1][f].z for s in samples)
        for a, b in zip(samples, samples[1:]):
            pa, pb = a[1][f], b[1][f]
            if pa.z < zmin + 1.5 and pb.z < zmin + 1.5 and b[0] > a[0]:
                speeds.append(math.hypot(pb.x - pa.x, pb.y - pa.y) / (b[0] - a[0]))
    speeds.sort()
    med = speeds[len(speeds) // 2] if speeds else -1
    p90 = speeds[int(len(speeds) * 0.9)] if speeds else -1
    log("%-7s planted-foot slip: median %.1f cm/s, 90th percentile %.1f cm/s (%d samples)" % (name, med, p90, len(speeds)))

def tick(dt):
    try:
        state["tick"] += 1
        t = state["tick"]
        if state["step"] == "start" and t == 2:
            # Full frame rate even when another window has focus, or the samples are seconds apart.
            try:
                perf = unreal.get_default_object(unreal.load_class(None, "/Script/UnrealEd.EditorPerformanceSettings"))
                state["throttle"] = perf.get_editor_property("bThrottleCPUWhenNotForeground")
                perf.set_editor_property("bThrottleCPUWhenNotForeground", False)
            except Exception as e:
                log("could not unthrottle: %s" % e)
        if state["step"] == "start" and t == 30:
            les.load_level("/Game/ThirdPerson/Lvl_ThirdPerson")
            # Undo an earlier version of this test that saved its floor, light and player start into the map.

            removed = 0
            for a in eas.get_all_level_actors():
                loc, sc = a.get_actor_location(), a.get_actor_scale3d()
                stray = (isinstance(a, unreal.StaticMeshActor) and abs(sc.x - 400) < 0.01 and abs(sc.z - 1) < 0.01)                     or (isinstance(a, unreal.PlayerStart) and abs(loc.x) < 0.01 and abs(loc.y) < 0.01 and abs(loc.z - 120) < 0.01)                     or (isinstance(a, unreal.DirectionalLight) and abs(loc.x) < 0.01 and abs(loc.y) < 0.01 and abs(loc.z - 500) < 0.01)
                if stray:
                    eas.destroy_actor(a); removed += 1
            log("removed %d stray test actors from Lvl_ThirdPerson" % removed)
            if removed:
                les.save_current_level()
            # A flat floor far below the map for the test; removed again before quitting.
            floor = eas.spawn_actor_from_class(unreal.StaticMeshActor, unreal.Vector(0, 0, FLOOR_Z - 50), unreal.Rotator(0, 0, 0))
            floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Cube"))
            floor.set_actor_scale3d(unreal.Vector(400, 400, 1))
            floor.set_actor_label("LPCG_FootTestFloor")
            state["floor"] = floor
            les.editor_request_begin_play()
            state["step"] = "settle"; state["t0"] = t
        elif state["step"] == "settle" and t == state["t0"] + 120:
            world = ues.get_game_world()
            pawn = unreal.GameplayStatics.get_player_pawn(world, 0)
            pawn.set_actor_location(START, False, True)
            state["step"] = "drive"; state["t0"] = t; state["samples"] = []
        elif state["step"] == "drive":
            world = ues.get_game_world()
            pawn = unreal.GameplayStatics.get_player_pawn(world, 0)
            name, frames = PHASES[state["phase"]]
            k = t - state["t0"]
            if k == 1:
                move = pawn.character_movement
                if name == "walk":
                    move.set_editor_property("max_walk_speed", 111.5 * pawn.get_editor_property("character_scale"))
                elif name == "move":
                    move.set_editor_property("max_walk_speed", pawn.get_editor_property("move_speed"))
                elif name == "sprint":
                    move.set_editor_property("max_walk_speed", pawn.get_editor_property("sprint_speed"))
                elif name == "crouch":
                    move.set_editor_property("max_walk_speed", pawn.get_editor_property("move_speed"))
                    pawn.crouch()
                log("%s: max speed %.0f, crouched speed %.0f" % (name, move.max_walk_speed, move.max_walk_speed_crouched))
            pawn.add_movement_input(unreal.Vector(0, 1, 0), 1.0)   # sideways, clear of the lineup
            if k > 60:   # let it reach speed first
                mesh = pawn.mesh
                loc = pawn.get_actor_location()
                state["samples"].append((unreal.GameplayStatics.get_time_seconds(world), [mesh.get_socket_location(b) for b in TOES],
                                         [loc.x, loc.y, loc.z, float(pawn.get_editor_property("bIsCrouched"))]))
            if k == 60:
                log("%s: ground speed %.0f cm/s" % (name, pawn.get_velocity().length()))
            if k >= frames:
                analyse(name, state["samples"])
                # walk back to the start so the next run has room
                pawn.set_actor_location(START, False, True)
                state["phase"] += 1; state["t0"] = t; state["samples"] = []
                if state["phase"] >= len(PHASES):
                    state["step"] = "end"
        elif state["step"] == "end" and t == state["t0"] + 30:
            les.editor_request_end_play()
        elif state["step"] == "end" and t == state["t0"] + 45:
            if "floor" in state:
                eas.destroy_actor(state["floor"])
                les.save_current_level()
            state["step"] = "quit"; state["t0"] = t
        elif state["step"] == "quit" and t == state["t0"] + 30:
            if "throttle" in state:
                unreal.get_default_object(unreal.load_class(None, "/Script/UnrealEd.EditorPerformanceSettings")).set_editor_property("bThrottleCPUWhenNotForeground", state["throttle"])
            json.dump(RAW, open(os.path.join(unreal.Paths.project_saved_dir(), "LpcgFootSlip.json"), "w"))
            log("done")
            LOG.close()
            unreal.unregister_slate_post_tick_callback(state["handle"])
            unreal.SystemLibrary.quit_editor()
    except Exception:
        log(traceback.format_exc())
        if state["step"] in ("end", "quit"):
            # Failing while finishing: stop here rather than retrying forever.
            unreal.unregister_slate_post_tick_callback(state["handle"])
            LOG.close()
            unreal.SystemLibrary.quit_editor()
        else:
            state["step"] = "end"; state["t0"] = state["tick"]

state["handle"] = unreal.register_slate_post_tick_callback(tick)

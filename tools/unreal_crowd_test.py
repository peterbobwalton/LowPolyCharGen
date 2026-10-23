"""Crowd performance test in LowPolyCharTest: a fresh unsaved level with N animated characters, frame time
measured over a few hundred editor frames for each configuration, results in Saved/LpcgCrowd.txt.
    py "C:/source/Claude/LowPolyCharGen/tools/unreal_crowd_test.py"
Configurations: the kit characters with Nanite (as imported), the same meshes without Nanite, and the pack's
own prebuilt soldiers (no Nanite). The kit meshes' Nanite setting is restored afterwards.
"""
import os, time, unreal

COUNT = int(os.environ.get("LPCG_CROWD", "300"))
KIT = ["/Game/LowPolyCharGen/Kit/SK_LPCG_" + n for n in (
    "Kit_Rifleman", "Kit_Heavy", "Kit_Scout_Slim", "Kit_Tee", "Kit_Female_Tee", "Kit_Female_Rifleman",
    "Mil_Turban_Thobe", "Mil_Keffiyeh_Thobe", "Mil_Shemagh", "Mil_Balaclava")]
PACK = ["/Game/Toon_Soldiers_Armies/Meshes/Characters_Prebuilt/SK_Armies_" + n for n in (
    "Soldier_A", "Soldier_B", "Soldier_C", "Recon_A", "Sniper_A", "Officer", "Engineer", "OffDuty", "Recon_B", "Sniper_B")]
ANIM = "/Game/Toon_Soldiers_Armies/Animations/Infantry/infantry_combat_idle"
WARMUP, FRAMES = 150, 300
OUT = os.path.join(unreal.Paths.project_saved_dir(), "LpcgCrowd.txt")

les = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
eas = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
ues = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
log = open(OUT, "w")


def say(m):
    log.write(m + "\n"); log.flush(); unreal.log("LPCG crowd: " + m)


def set_nanite(paths, on):
    for p in paths:
        mesh = unreal.load_asset(p)
        s = mesh.get_editor_property("nanite_settings")
        if s.enabled != on:
            s.enabled = on
            mesh.set_editor_property("nanite_settings", s)


def populate(meshes):
    for a in eas.get_all_level_actors():
        if a.get_actor_label().startswith("Crowd_"):
            eas.destroy_actor(a)
    anim = unreal.load_asset(ANIM)
    side = int(COUNT ** 0.5 + 0.999)
    for i in range(COUNT):
        x, y = (i // side) * 160.0, (i % side - side / 2) * 120.0
        # The display soldier animates in the editor (C++ locomotion, combat idle) and carries no weapon here.
        a = eas.spawn_actor_from_class(unreal.LpcgDisplaySoldier, unreal.Vector(x, y, 0), unreal.Rotator(0, 0, 180))
        a.set_actor_label("Crowd_%03d" % i)
        a.set_editor_property("mesh", unreal.load_asset(meshes[i % len(meshes)]))
        a.set_editor_property("animation", anim if i % 2 else None)
        a.set_editor_property("locomotion_speed", 0.0)
        a.set_editor_property("weapon_mesh", None)
        a.set_editor_property("label", "")
        a.refresh()
    ues.set_level_viewport_camera_info(unreal.Vector(-900, 0, 700), unreal.Rotator(0, -22, 0))


CONFIGS = [("kit, Nanite", KIT, True), ("kit, no Nanite", KIT, False), ("pack prebuilt", PACK, None)]
state = {"cfg": -1, "frame": 0, "times": [], "last": None}


def start(index):
    name, meshes, nanite = CONFIGS[index]
    if nanite is not None:
        set_nanite(KIT, nanite)
    populate(meshes)
    state.update(cfg=index, frame=0, times=[], last=None)
    say("config %s: %d characters" % (name, COUNT))


def finish():
    set_nanite(KIT, True)
    for a in eas.get_all_level_actors():
        if a.get_actor_label().startswith("Crowd_"):
            eas.destroy_actor(a)
    unreal.SystemLibrary.execute_console_command(None, "t.MaxFPS 0")
    say("done")
    log.close()
    unreal.unregister_slate_post_tick_callback(state["handle"])


def tick(dt):
    now = time.perf_counter()
    if state["last"] is not None:
        state["frame"] += 1
        if state["frame"] > WARMUP:
            state["times"].append((now - state["last"]) * 1000.0)
    state["last"] = now
    if state["frame"] >= WARMUP + FRAMES:
        t = sorted(state["times"])
        say("  frame ms: mean %.2f  median %.2f  p95 %.2f" % (sum(t) / len(t), t[len(t) // 2], t[int(len(t) * 0.95)]))
        if state["cfg"] + 1 < len(CONFIGS):
            start(state["cfg"] + 1)
        else:
            finish()


les.new_level("/Temp/Untitled_LpcgCrowd")
for cls, loc, rot in [(unreal.DirectionalLight, (0, 0, 1000), (0, -42, 135)), (unreal.SkyLight, (0, 0, 500), (0, 0, 0))]:
    eas.spawn_actor_from_class(cls, unreal.Vector(*loc), unreal.Rotator(*rot))
floor = eas.spawn_actor_from_class(unreal.StaticMeshActor, unreal.Vector(0, 0, 0))
floor.static_mesh_component.set_static_mesh(unreal.load_asset("/Engine/BasicShapes/Plane"))
floor.set_actor_scale3d(unreal.Vector(100, 100, 1))
for cmd in ("t.MaxFPS 0", "r.VSync 0", "r.VSyncEditor 0"):
    unreal.SystemLibrary.execute_console_command(None, cmd)
say("engine %s, %d frames after %d warm-up per config" % (unreal.SystemLibrary.get_engine_version(), FRAMES, WARMUP))
start(0)
state["handle"] = unreal.register_slate_post_tick_callback(tick)

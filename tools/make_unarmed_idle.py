"""Makes /Game/LowPolyCharGen/Animations/A_Unarmed_Idle in LowPolyCharTest. The packs only have weapon idles, whose
rifleman's stance looks wrong on a civilian, so this one is built from the skeleton's bind pose (toon_soldiers_skeleton.json):
standing straight, arms hanging relaxed at the sides, breathing and a slow sway. It loops.
Run inside the open editor:
    py "C:/source/Claude/LowPolyCharGen/tools/make_unarmed_idle.py"
"""
import json
import math
import os

import unreal

SOURCE = "/Game/Toon_Soldiers_Armies/Animations/Infantry/Guard/infantry_guard_idle"   # its length and sample rate
TARGET_FOLDER = "/Game/LowPolyCharGen/Animations"
TARGET = TARGET_FOLDER + "/A_Unarmed_Idle"
SKELETON_JSON = os.path.join(os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
                             else "C:/Source/Claude/LowPolyCharGen/tools", "toon_soldiers_skeleton.json")
lib = unreal.AnimationLibrary

if unreal.EditorAssetLibrary.does_asset_exist(TARGET):
    unreal.EditorAssetLibrary.delete_asset(TARGET)
tools = unreal.AssetToolsHelpers.get_asset_tools()
unreal.EditorAssetLibrary.make_directory(TARGET_FOLDER)
anim = tools.duplicate_asset("A_Unarmed_Idle", TARGET_FOLDER, unreal.load_asset(SOURCE))
frames = lib.get_num_frames(anim)
bones = json.load(open(SKELETON_JSON))["bones"]
parents = {b["name"]: b["parent"] for b in bones}
BIND = {b["name"]: b["local"] for b in bones}


def qmul(a, b):
    """a * b: b applied first, as FQuat."""
    return unreal.Quat(a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                       a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
                       a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
                       a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z)


def qinv(q):
    return unreal.Quat(-q.x, -q.y, -q.z, q.w)


def rotate(q, v):
    r = qmul(qmul(q, unreal.Quat(v.x, v.y, v.z, 0)), qinv(q))
    return unreal.Vector(r.x, r.y, r.z)


def norm(v):
    length = (v.x * v.x + v.y * v.y + v.z * v.z) ** 0.5
    return unreal.Vector(v.x / length, v.y / length, v.z / length)


def between(a, b):
    """The shortest rotation taking unit a to unit b."""
    c = unreal.Vector(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x)
    w = 1 + a.x * b.x + a.y * b.y + a.z * b.z
    length = (c.x * c.x + c.y * c.y + c.z * c.z + w * w) ** 0.5
    return unreal.Quat(c.x / length, c.y / length, c.z / length, w / length)


def axis_angle(x, y, z, degrees):
    half = math.radians(degrees) / 2
    s = math.sin(half)
    return unreal.Quat(x * s, y * s, z * s, math.cos(half))


def bind_rotation(bone):
    return unreal.Quat(*BIND[bone]["q"])


def bind_position(bone):
    return unreal.Vector(*BIND[bone]["t"])


def component(bone, local_rotations):
    """Rotation and position in component space (the root's uniform scale doesn't change directions)."""
    q = local_rotations[bone]
    p = bind_position(bone)
    parent = parents.get(bone)
    if parent and parent in BIND:
        pq, pp = component(parent, local_rotations)
        return qmul(pq, q), pp + rotate(pq, p)
    return q, p


# Down and a little out; the elbows a touch bent, hands forward (the bind pose faces -Y: left is +X).
UPPER = {"L": norm(unreal.Vector(0.17, 0.0, -1)), "R": norm(unreal.Vector(-0.17, 0.0, -1))}
FOREARM = {"L": norm(unreal.Vector(0.08, -0.2, -1)), "R": norm(unreal.Vector(-0.08, -0.2, -1))}

keys = {b["name"]: ([], [], []) for b in bones}
for frame in range(frames):
    phase = 2 * math.pi * frame / max(frames - 1, 1)
    breath = math.sin(2 * phase)   # two breaths a loop; whole cycles so it loops
    sway = math.sin(phase)
    local = {name: bind_rotation(name) for name in BIND}
    local["Bip001-Spine"] = qmul(local["Bip001-Spine"], axis_angle(0, 0, 1, 0.7 * breath))
    for clavicle in ("Bip001-L-Clavicle", "Bip001-R-Clavicle"):
        local[clavicle] = qmul(local[clavicle], axis_angle(0, 1, 0, 0.5 * breath))
    local["Bip001-Head"] = qmul(local["Bip001-Head"], axis_angle(0, 1, 0, 1.2 * sway))

    for side in "LR":
        upper, fore, hand = "Bip001-%s-UpperArm" % side, "Bip001-%s-Forearm" % side, "Bip001-%s-Hand" % side
        u_q, u_p = component(upper, local)
        f_q, f_p = component(fore, local)
        h_q, h_p = component(hand, local)
        clav_q, _ = component(parents[upper], local)
        arm_sway = 0.012 * sway
        target_upper = norm(UPPER[side] + unreal.Vector(0, arm_sway, 0))
        new_u = qmul(between(norm(f_p - u_p), target_upper), u_q)
        f2 = qmul(new_u, qmul(qinv(u_q), f_q))
        hand_dir = rotate(new_u, rotate(qinv(u_q), norm(h_p - f_p)))
        new_f = qmul(between(norm(hand_dir), FOREARM[side]), f2)
        new_h = qmul(new_f, qmul(qinv(f_q), h_q))
        local[upper] = qmul(qinv(clav_q), new_u)
        local[fore] = qmul(qinv(new_u), new_f)
        local[hand] = qmul(qinv(new_f), new_h)

    for b in bones:
        name = b["name"]
        t = bind_position(name)
        if name == "Bip001":
            t = t + unreal.Vector(0.25 * sway, 0, 0.12 * breath)
        s = BIND[name]["s"]
        keys[name][0].append(t)
        keys[name][1].append(local[name])
        keys[name][2].append(unreal.Vector(*s))

controller = anim.get_editor_property("controller")
controller.open_bracket("Unarmed idle", False)
for name, (positions, rotations, scales) in keys.items():
    controller.set_bone_track_keys(name, positions, rotations, scales, False)
controller.close_bracket(False)
unreal.EditorAssetLibrary.save_asset(TARGET, False)
print("made", TARGET, "frames", frames)

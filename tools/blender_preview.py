"""Imports generated FBX files into Blender and renders a contact sheet for visual checking.

usage: blender -b --factory-startup --python blender_preview.py -- <out.png> <a.fbx> [<b.fbx> ...]
           [--views front,three_quarter,side,back] [--pose] [--head] [--size 560x600] [--report]

Each FBX becomes one row, each view one column. --pose rotates a few bones so the skin weights can
be judged; --report prints what Blender found in the file (objects, bones, bounds).
"""
import math
import sys

import bpy
import numpy as np
from mathutils import Euler, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
out_path = argv[0]
files = [a for a in argv[1:] if a.lower().endswith(".fbx")]
views = "front,three_quarter,side,back"
if "--views" in argv:
    views = argv[argv.index("--views") + 1]
views = views.split(",")
size = (560, 600)
if "--size" in argv:
    size = tuple(int(v) for v in argv[argv.index("--size") + 1].split("x"))
pose = "--pose" in argv
head_only = "--head" in argv
report = "--report" in argv

VIEW_ANGLES = {  # camera azimuth around Z, degrees; 0 = looking at the character's front
    "front": 0, "three_quarter": 35, "side": 90, "back": 180, "back_quarter": 145, "left_quarter": -35,
}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def setup_scene():
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "TEXTURE"
    shading.show_backface_culling = True
    shading.show_shadows = False
    shading.show_cavity = False
    shading.show_object_outline = False
    world = bpy.data.worlds.new("w")
    world.color = (0.32, 0.34, 0.38)
    scene.world = world
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    return scene, cam


def frame(cam, azimuth_deg, lo, hi):
    centre = (lo + hi) / 2
    extent = hi - lo
    a = math.radians(azimuth_deg)
    direction = Vector((math.sin(a), -math.cos(a), 0.12))   # where the camera sits, relative to the centre
    cam.location = centre + direction.normalized() * 10
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    aspect = size[0] / size[1]
    # ortho_scale spans the larger render dimension
    across = abs(extent.x * math.cos(a)) + abs(extent.y * math.sin(a))
    if aspect >= 1:
        cam.data.ortho_scale = max(across * 1.06, extent.z * 1.1 * aspect)
    else:
        cam.data.ortho_scale = max(extent.z * 1.1, across * 1.06 / aspect)


def apply_pose(arm):
    """A-pose arms, bent elbow and knee, lifted leg, turned head: exercises the main joints."""
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")

    def rot(name, axis, deg):
        pb = arm.pose.bones.get(name)
        if pb is None:
            return
        pb.rotation_mode = "QUATERNION"
        # rotate about a world axis through the joint
        mat = arm.matrix_world @ pb.matrix
        from mathutils import Matrix
        r = Matrix.Rotation(math.radians(deg), 4, axis)
        loc = mat.to_translation()
        new = Matrix.Translation(loc) @ r @ Matrix.Translation(-loc) @ mat
        pb.matrix = arm.matrix_world.inverted() @ new
        bpy.context.view_layer.update()

    rot("upperarm_l", "Y", 55)
    rot("upperarm_r", "Y", -70)
    rot("lowerarm_r", "X", -70)
    rot("thigh_l", "X", -55)
    rot("calf_l", "X", 75)
    rot("head", "Z", 30)
    rot("spine_03", "X", 12)
    bpy.ops.object.mode_set(mode="OBJECT")


def describe(path):
    print("=== ", path)
    for ob in bpy.context.scene.objects:
        if ob.type == "ARMATURE":
            print("ARMATURE", ob.name, "bones:", len(ob.data.bones), "scale:", tuple(round(s, 4) for s in ob.scale),
                  "rot:", tuple(round(math.degrees(r), 2) for r in ob.rotation_euler))
            for name in ("root", "pelvis", "spine_05", "head", "upperarm_l", "lowerarm_l", "hand_l", "hand_r", "thigh_l", "foot_l", "ball_l", "ik_hand_gun"):
                b = ob.data.bones.get(name)
                if b:
                    head = ob.matrix_world @ b.head_local
                    print(f"   {name:12s} head(cm) = ({head.x * 100:8.2f}, {head.y * 100:8.2f}, {head.z * 100:8.2f})  parent = {b.parent.name if b.parent else None}")
        elif ob.type == "MESH":
            me = ob.data
            pts = [ob.matrix_world @ v.co for v in me.vertices]
            lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))) * 100
            hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts))) * 100
            tris = sum(len(p.vertices) - 2 for p in me.polygons)
            unweighted = sum(1 for v in me.vertices if not v.groups)
            print("MESH", ob.name, "verts:", len(me.vertices), "polys:", len(me.polygons), "tris:", tris,
                  "groups:", len(ob.vertex_groups), "unweighted:", unweighted,
                  "materials:", [m.name for m in me.materials], "uv:", [u.name for u in me.uv_layers],
                  "colors:", [c.name for c in me.color_attributes])
            print(f"   bounds(cm) = ({lo.x:.1f}, {lo.y:.1f}, {lo.z:.1f}) .. ({hi.x:.1f}, {hi.y:.1f}, {hi.z:.1f})")
            print("   modifiers:", [(m.type, m.object.name if getattr(m, 'object', None) else None) for m in ob.modifiers])
            for m in me.materials:
                if m and m.use_nodes:
                    print("   images:", [n.image.filepath for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image])


tiles = []
for row, path in enumerate(files):
    reset()
    scene, cam = setup_scene()
    bpy.ops.import_scene.fbx(filepath=path)
    if report:
        describe(path)
    arm = next((o for o in scene.objects if o.type == "ARMATURE"), None)
    if pose and arm:
        apply_pose(arm)
    deps = bpy.context.evaluated_depsgraph_get()
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for ob in scene.objects:
        if ob.type != "MESH":
            continue
        ev = ob.evaluated_get(deps)
        me = ev.to_mesh()
        for v in me.vertices:
            p = ev.matrix_world @ v.co
            lo = Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
            hi = Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))
        ev.to_mesh_clear()
    if head_only:
        lo = Vector((-0.24, -0.26, hi.z - 0.50))
        hi = Vector((0.24, 0.22, hi.z + 0.02))
    row_tiles = []
    for col, view in enumerate(views):
        frame(cam, VIEW_ANGLES[view], lo, hi)
        tile = f"{out_path}.{row}.{col}.png"
        scene.render.filepath = tile
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(tile)
        px = np.array(img.pixels[:], dtype=np.float32).reshape(size[1], size[0], 4)
        row_tiles.append(px)
        bpy.data.images.remove(img)
        import os
        os.remove(tile)
    tiles.append(np.hstack(row_tiles))

sheet = np.vstack(list(reversed(tiles)))   # image rows are stored bottom-up
out = bpy.data.images.new("sheet", width=sheet.shape[1], height=sheet.shape[0])
out.pixels = sheet.ravel()
out.filepath_raw = out_path
out.file_format = "PNG"
out.save()
print("wrote", out_path)

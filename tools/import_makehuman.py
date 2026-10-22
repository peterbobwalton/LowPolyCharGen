"""
Builds src/LowPolyCharGen.Core/Assets/human_body.bin from the MakeHuman base mesh and its shape
targets, which the generator shapes, fits to the mannequin skeleton and decimates for the
"Realistic" body.

The MakeHuman base mesh (hm08), its targets and the game_engine rig weights are CC0 (released by
Data Collection AB, Joel Palmius and Jonas Hauquier; see https://github.com/makehumancommunity/mpfb2,
LICENSE.ASSETS.md). Get them with a sparse clone:

    git clone --depth 1 --filter=blob:none --sparse https://github.com/makehumancommunity/mpfb2.git
    cd mpfb2 && git sparse-checkout set src/mpfb/data/3dobjs src/mpfb/data/targets src/mpfb/data/rigs

    python tools/import_makehuman.py path/to/mpfb2 [out.bin]

Units and axes are converted to the generator's: centimetres, +X = character's left, -Y = front,
+Z = up (MakeHuman: decimetres, +Y up, +Z front).

File layout (little endian, gzip-compressed):
    "LPHB" int32 version
    int32 vertexCount, float32[3] positions
    int32 faceCount,  then per face: uint8 corners, int32[corners] vertex indices     (body)
    int32 eyeFaceCount, same layout                                                      (eyeballs)
    int32 jointCount,  per joint: string name, int32 count, int32[count] vertices (centre = mean)
    int32 boneCount,   per bone: string name, string parent, string headJoint, string tailJoint,
                       int32 count, (int32 vertex, float32 weight)[count]
    int32 targetCount, per target: string name, int32 count, int32[count] vertices, float16[3*count] deltas (cm)
Strings are int32 length + UTF-8.
"""
import gzip
import json
import os
import struct
import sys

import numpy as np

AGE = "young"
GENDERS = ["female", "male"]
RACES = ["african", "asian", "caucasian"]
MUSCLES = ["minmuscle", "averagemuscle", "maxmuscle"]
WEIGHTS = ["minweight", "averageweight", "maxweight"]
FACE_SHAPES = ["square", "round", "oval", "rectangular", "triangular", "invertedtriangular", "diamond"]
# (folder, low, high): the two ends of each face feature the generator varies.
FACE_PAIRS = [(folder, f"{name}-{lo}", f"{name}-{hi}") for folder, name, lo, hi in [
    ("head", "head-fat", "decr", "incr"), ("head", "head-scale-horiz", "decr", "incr"), ("head", "head-scale-vert", "decr", "incr"),
    ("chin", "chin-width", "decr", "incr"), ("chin", "chin-prominent", "decr", "incr"), ("chin", "chin-height", "decr", "incr"),
    ("chin", "chin-bones", "decr", "incr"),
    ("nose", "nose-scale-horiz", "decr", "incr"), ("nose", "nose-scale-vert", "decr", "incr"), ("nose", "nose-scale-depth", "decr", "incr"),
    ("nose", "nose-point-width", "decr", "incr"), ("nose", "nose-hump", "decr", "incr"), ("nose", "nose-volume", "decr", "incr"),
    ("nose", "nose-nostrils-width", "decr", "incr"),
    ("mouth", "mouth-scale-horiz", "decr", "incr"), ("mouth", "mouth-lowerlip-volume", "decr", "incr"), ("mouth", "mouth-upperlip-volume", "decr", "incr"),
    ("eyes", "l-eye-scale", "decr", "incr"), ("eyes", "r-eye-scale", "decr", "incr"),
    ("eyes", "l-eye-height2", "decr", "incr"), ("eyes", "r-eye-height2", "decr", "incr"),
    ("cheek", "l-cheek-bones", "decr", "incr"), ("cheek", "r-cheek-bones", "decr", "incr"),
    ("cheek", "l-cheek-volume", "decr", "incr"), ("cheek", "r-cheek-volume", "decr", "incr"),
    ("eyebrows", "eyebrows-trans", "down", "up"),
    # Build: a man's V-shaped, muscular torso; a woman's petite frame.
    ("torso", "torso-vshape", "decr", "incr"), ("torso", "torso-muscle-pectoral", "decr", "incr"),
    ("torso", "torso-muscle-dorsi", "decr", "incr"), ("torso", "measure-shoulder-dist", "decr", "incr"),
    ("torso", "measure-waist-circ", "decr", "incr"), ("torso", "measure-hips-circ", "decr", "incr"),
    ("hip", "hip-scale-horiz", "decr", "incr"),
]]


def convert(p):
    """MakeHuman (dm, +Y up, +Z front) to generator space (cm, +Z up, -Y front)."""
    p = np.asarray(p, dtype=np.float64)
    return np.stack([p[..., 0] * 10.0, -p[..., 2] * 10.0, p[..., 1] * 10.0], axis=-1)


def load_obj(path):
    verts, groups, current = [], {}, None
    for line in open(path):
        s = line.split()
        if not s:
            continue
        if s[0] == "v":
            verts.append([float(x) for x in s[1:4]])
        elif s[0] == "g":
            current = s[1]
            groups.setdefault(current, [])
        elif s[0] == "f":
            groups[current].append([int(w.split("/")[0]) - 1 for w in s[1:]])
    return np.array(verts), groups


def load_target(path):
    idx, deltas = [], []
    if os.path.exists(path):
        for line in gzip.open(path, "rt"):
            s = line.split()
            if not s or s[0].startswith("#"):
                continue
            idx.append(int(s[0]))
            deltas.append([float(x) for x in s[1:4]])
    return np.array(idx, dtype=np.int64), np.array(deltas).reshape(-1, 3)


def main():
    root = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(
        os.path.dirname(__file__), "..", "src", "LowPolyCharGen.Core", "Assets", "human_body.bin")
    data = os.path.join(root, "src", "mpfb", "data")
    verts, groups = load_obj(os.path.join(data, "3dobjs", "base.obj"))
    rig = json.load(open(os.path.join(data, "rigs", "standard", "rig.game_engine.json")))
    weights = json.load(open(os.path.join(data, "rigs", "standard", "weights.game_engine.json")))["weights"]

    body = groups["body"]
    eyes = groups["helper-l-eye"] + groups["helper-r-eye"]
    joint_names = sorted({b[end]["cube_name"] for b in rig.values() for end in ("head", "tail") if "cube_name" in b[end]}
                         | {"joint-l-eye", "joint-r-eye", "joint-head-2", "joint-jaw"})
    joint_groups = {n: sorted({i for f in groups[n] for i in f}) for n in joint_names}

    # Keep only the vertices the generator uses and number them compactly.
    used = sorted({i for f in body + eyes for i in f} | {i for g in joint_groups.values() for i in g})
    remap = {old: new for new, old in enumerate(used)}

    targets = []
    for g in GENDERS:
        for r in RACES:
            targets.append(f"macrodetails/{r}-{g}-{AGE}")
        for m in MUSCLES:
            for w in WEIGHTS:
                if (m, w) != ("averagemuscle", "averageweight"):
                    targets.append(f"macrodetails/universal-{g}-{AGE}-{m}-{w}")
    for cup in ("mincup", "maxcup"):
        targets.append(f"breast/female-{AGE}-averagemuscle-averageweight-{cup}-averagefirmness")
    # Face shape: head outlines and the paired (decr/incr) features the generator varies per face.
    targets += [f"head/head-{shape}" for shape in FACE_SHAPES]
    for folder, lo, hi in FACE_PAIRS:
        targets += [f"{folder}/{lo}", f"{folder}/{hi}"]

    buf = bytearray()

    def i32(v):
        buf.extend(struct.pack("<i", v))

    def string(s):
        b = s.encode("utf-8")
        i32(len(b))
        buf.extend(b)

    def faces(fs):
        i32(len(fs))
        for f in fs:
            buf.extend(struct.pack("<B", len(f)))
            buf.extend(struct.pack(f"<{len(f)}i", *[remap[i] for i in f]))

    buf.extend(b"LPHB")
    i32(1)
    pos = convert(verts[used]).astype("<f4")
    i32(len(used))
    buf.extend(pos.tobytes())
    faces(body)
    faces(eyes)
    i32(len(joint_groups))
    for name, members in joint_groups.items():
        string(name)
        i32(len(members))
        buf.extend(struct.pack(f"<{len(members)}i", *[remap[i] for i in members]))
    bones = [n for n in rig if n != "Root"]
    i32(len(bones))
    for name in bones:
        b = rig[name]
        string(name)
        string(b.get("parent") or "")
        string(b["head"]["cube_name"])
        string(b["tail"].get("cube_name", ""))
        pairs = [(remap[v], w) for v, w in weights.get(name, []) if v in remap]
        i32(len(pairs))
        for v, w in pairs:
            buf.extend(struct.pack("<if", v, w))
    i32(len(targets))
    for name in targets:
        idx, deltas = load_target(os.path.join(data, "targets", name + ".target.gz"))
        keep = [k for k, v in enumerate(idx) if v in remap]
        string(name.split("/")[-1])
        i32(len(keep))
        buf.extend(struct.pack(f"<{len(keep)}i", *[remap[int(idx[k])] for k in keep]))
        d = np.stack([deltas[keep, 0] * 10.0, -deltas[keep, 2] * 10.0, deltas[keep, 1] * 10.0], axis=-1) if keep else np.zeros((0, 3))
        buf.extend(d.astype("<f2").tobytes())
        print(f"{name}: {len(keep)} vertices")

    with gzip.open(out, "wb", compresslevel=9) as f:
        f.write(bytes(buf))
    print(out, len(used), "vertices,", len(body), "body faces,", len(eyes), "eye faces,",
          len(bones), "bones,", len(targets), "targets,", os.path.getsize(out), "bytes")


if __name__ == "__main__":
    main()

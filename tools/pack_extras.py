"""Extract the bits of the pack's prebuilt characters that aren't in its parts library.

A few Militia prebuilts carry geometry that only exists on the prebuilt mesh: the gas mask
(SK_Militia10/14), sunglasses (SK_Militia17) and a back pouch/belt (SK_Militia13/15). This
script finds the triangles of a prebuilt that no matched part covers and writes them to the
pack data folder as new parts (MIL_Extra_*), in the same JSON format as pack2json.py, so the
kit can rebuild every prebuilt.

    python tools/pack_extras.py [packdata folder]

The pack data comes from your own copy of the packs, so it stays out of git like the rest.
"""
import json
import os
import sys

import numpy as np

# out name: (prebuilt, the parts it is built from)
EXTRAS = {
    "MIL_Extra_GasMask": ("MIL_SK_Militia10", ["MIL_Mesh_Body_B", "MIL_Mesh_Legs_C", "MIL_Mesh_extra_equip_D", "MIL_Mesh_extra_backpack"]),
    "MIL_Extra_Pouch": ("MIL_SK_Militia13", ["MIL_Mesh_head_N", "MIL_Mesh_Body_D", "MIL_Mesh_Legs_A", "MIL_Mesh_extra_backpack"]),
    "MIL_Extra_Pouch_B": ("MIL_SK_Militia15", ["MIL_Mesh_head_O", "MIL_Mesh_Body_D", "MIL_Mesh_Legs_A", "MIL_Mesh_extra_backpack"]),
    "MIL_Extra_Sunglasses": ("MIL_SK_Militia17", ["MIL_Mesh_head_B", "MIL_Mesh_Body_E", "MIL_Mesh_Legs_A"]),
    # The UE5 pack (TS_*, from pack2json.py ... TS_ over its prebuilts): SK_Basic's belt pouches, and SK_Pyro's
    # flamethrower tank (on its second material and texture, T_Equipment: TS_Equip_ parts use that texture).
    "TS_Extra_Basic": ("TS_SK_Basic", ["TS_Head_10_glasses", "TS_Body_07_unequipped"]),
    "TS_Equip_FlamerTank": ("TS_SK_Pyro", ["TS_Head_11_gasmask", "TS_Body_08_trench"]),
}


def load(folder, name):
    with open(os.path.join(folder, name + ".json")) as f:
        return json.load(f)


def nearest(a, b):
    """Distance from each point of a to the nearest point of b (brute force: the meshes are small)."""
    out = np.empty(len(a))
    for s in range(0, len(a), 256):
        d = ((a[s:s + 256, None, :] - b[None, :, :]) ** 2).sum(-1)
        out[s:s + 256] = np.sqrt(d.min(1))
    return out


def extract(folder, prebuilt, part_names):
    mesh = load(folder, prebuilt)[0]
    x = np.array(mesh["verts"], float)
    covered = np.vstack([np.array(load(folder, p)[0]["verts"], float) for p in part_names])
    free = nearest(x, covered) > 0.1
    tris = [(t, m) for t, m in zip(mesh["tris"], mesh["triMat"]) if all(free[i] for i in t)]
    used = sorted({i for t, _ in tris for i in t})
    remap = {old: new for new, old in enumerate(used)}
    part = {"name": None, "materials": mesh["materials"]}
    for key in ("verts", "normals", "uvs", "colors", "weights"):
        part[key] = [mesh[key][i] for i in used]
    part["tris"] = [[remap[i] for i in t] for t, _ in tris]
    part["triMat"] = [m for _, m in tris]
    return part


def main():
    folder = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "packdata")
    for out, (prebuilt, parts) in EXTRAS.items():
        part = extract(folder, prebuilt, parts)
        part["name"] = out
        with open(os.path.join(folder, out + ".json"), "w") as f:
            json.dump([part], f)
        print(f"{out}: {len(part['verts'])} verts, {len(part['tris'])} tris from {prebuilt}")


if __name__ == "__main__":
    main()

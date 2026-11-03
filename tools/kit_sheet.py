"""Contact sheet of kit characters from the CLI, without the editor: front and back in the bind pose, each with the
kit material's colours (software z-buffer). Characters are rows of a list, by name wildcard:
    py tools/kit_sheet.py out.png "Civilians_*" [--close] [--list pack_factions.txt] [--cols 8]
--close frames the heads and chests instead of the whole body. Needs Pillow and numpy.
"""
import fnmatch
import json
import os
import subprocess
import sys
import tempfile

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLI = os.path.join(ROOT, "src", "LowPolyCharGen.Cli", "bin", "Release", "net10.0", "lowpolychargen.exe")


def rows(list_name, pattern):
    for line in open(os.path.join(ROOT, "tools", list_name), encoding="utf-8"):
        if line.startswith("#") or not line.strip():
            continue
        fields = line.rstrip("\n").split("|")
        if fnmatch.fnmatch(fields[0], pattern):
            yield fields


def export(fields, folder):
    name = fields[0]
    args = [CLI, "--out", os.path.join(folder, name + ".fbx"), "--preview", "--set", "Name=" + name, "--set", "Body=Pack", "--set", "PackHead=" + fields[1],
            "--set", "PackBody=" + fields[2], "--set", "PackLegs=" + fields[3], "--set", "PackEquipment=" + (fields[4] if len(fields) > 4 else "")]
    for pair in (fields[5] if len(fields) > 5 else "").split():
        args += ["--set", pair]
    subprocess.run(args, check=True, stdout=subprocess.DEVNULL)
    return os.path.join(folder, name)


def render(stem, w, h, zmin, zmax, back):
    mesh = json.load(open(stem + "_PreviewMesh.json"))
    groups = json.load(open(stem + "_Preview.json"))
    V = np.array(mesh["positions"], float)
    img = np.full((h, w, 3), 225, np.uint8)
    zb = np.full((h, w), 1e9)
    scale = h / (zmax - zmin)
    px = w / 2 + (V[:, 0] if back else -V[:, 0]) * scale
    py = (zmax - V[:, 2]) * scale
    depth = -V[:, 1] if back else V[:, 1]
    light = np.array([0.3, 0.7 if back else -0.7, 0.65])
    light /= np.linalg.norm(light)
    for g, faces in enumerate(groups):
        tex = np.asarray(Image.open("%s_Preview%d.png" % (stem, g)).convert("RGB"))
        size = tex.shape[0]
        for f in faces:
            a, b, c = mesh["faces"][f]
            uv = np.array(mesh["uvs"][f])
            n = np.array(mesh["normals"][f])
            x = px[[a, b, c]]
            y = py[[a, b, c]]
            x0, x1 = int(max(0, x.min())), int(min(w - 1, x.max() + 1))
            y0, y1 = int(max(0, y.min())), int(min(h - 1, y.max() + 1))
            if x1 < x0 or y1 < y0:
                continue
            den = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
            if abs(den) < 1e-9:
                continue
            gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + .5, np.arange(y0, y1 + 1) + .5)
            w0 = ((y[1] - y[2]) * (gx - x[2]) + (x[2] - x[1]) * (gy - y[2])) / den
            w1 = ((y[2] - y[0]) * (gx - x[2]) + (x[0] - x[2]) * (gy - y[2])) / den
            w2 = 1 - w0 - w1
            mask = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
            dz = w0 * depth[a] + w1 * depth[b] + w2 * depth[c]
            sub = zb[y0:y1 + 1, x0:x1 + 1]
            mask &= dz < sub
            if not mask.any():
                continue
            u = w0 * uv[0, 0] + w1 * uv[1, 0] + w2 * uv[2, 0]
            v = w0 * uv[0, 1] + w1 * uv[1, 1] + w2 * uv[2, 1]
            tx = ((u % 1) * size).astype(int).clip(0, size - 1)
            ty = ((1 - (v % 1)) * size).astype(int).clip(0, size - 1)
            nn = w0[..., None] * n[0] + w1[..., None] * n[1] + w2[..., None] * n[2]
            lit = np.clip(0.6 + 0.55 * (nn @ light) / (np.linalg.norm(nn, axis=-1) + 1e-6), 0.35, 1.15)
            col = (tex[ty, tx] * lit[..., None]).clip(0, 255).astype(np.uint8)
            sub[mask] = dz[mask]
            img[y0:y1 + 1, x0:x1 + 1][mask] = col[mask]
    return img


def main(argv):
    flags = {}
    plain = []
    i = 0
    while i < len(argv):
        if argv[i] == "--close":
            flags["close"] = True
        elif argv[i].startswith("--"):
            flags[argv[i][2:]] = argv[i + 1]
            i += 1
        else:
            plain.append(argv[i])
        i += 1
    output, pattern = plain
    chosen = list(rows(flags.get("list", "pack_factions.txt"), pattern))
    cols = int(flags.get("cols", 8))
    close = "close" in flags
    w, h = (190, 230) if close else (260, 400)
    zmin, zmax = (118, 190) if close else (-2, 190)
    gap = 20
    cw = w * 2
    canvas = Image.new("RGB", (cw * cols, (h + gap) * ((len(chosen) + cols - 1) // cols)), "white")
    draw = ImageDraw.Draw(canvas)
    with tempfile.TemporaryDirectory() as folder:
        for n, fields in enumerate(chosen):
            stem = export(fields, folder)
            cx, cy = (n % cols) * cw, (n // cols) * (h + gap)
            draw.text((cx + 3, cy + 3), fields[0], fill=(0, 0, 0))
            canvas.paste(Image.fromarray(render(stem, w, h, zmin, zmax, False)), (cx, cy + gap))
            canvas.paste(Image.fromarray(render(stem, w, h, zmin, zmax, True)), (cx + w, cy + gap))
    canvas.save(output)
    print(output, canvas.size, len(chosen), "characters")


if __name__ == "__main__":
    main(sys.argv[1:])

"""
Builds the dense (high-poly) left hand that the generator decimates to its triangle budget.

The hand is modelled as a signed distance field - tapered capsules for the metacarpals and each
finger bone, ellipsoids for the thumb and little-finger pads, blended with smooth unions so the
palm, webbing and knuckles flow into each other - and meshed with marching cubes. Proportions follow
an average adult male hand (19 cm wrist crease to middle fingertip, 8.8 cm across the knuckles) in a
relaxed pose: fingers gently curled, thumb resting forward. Fingertips are full, rounded pads.

Hand space (cm): +X from the wrist to the fingers (the T-posed left arm runs along +X), -Y towards
the thumb (the character's front), -Z out of the palm (palm down). The wrist is cut open at x = 0.

    python tools/make_hand.py [out.obj]      (needs numpy and scikit-image)

Then `lowpolychargen --make-hand out.obj` turns it into src/LowPolyCharGen.Core/Assets/hand_l.json.
Open the OBJ in Blender to look at it or touch it up.
"""
import math
import sys

import numpy as np
from skimage import measure

VOXEL = 0.08  # cm


def round_cone(p, a, b, r1, r2, squash=1.0):
    """Distance to a tapered capsule from a (radius r1) to b (radius r2); squash < 1 flattens it in Z."""
    q = p.copy()
    if squash != 1.0:
        q[..., 2] = a[2] + (q[..., 2] - a[2]) / squash
    ba = b - a
    pa = q - a
    t = np.clip((pa @ ba) / (ba @ ba), 0.0, 1.0)
    r = r1 + (r2 - r1) * t
    d = np.linalg.norm(pa - t[..., None] * ba, axis=-1) - r
    return d * min(1.0, squash)


def ellipsoid(p, c, r):
    """A bound for the distance to an ellipsoid that stays linear far from it (blends evenly)."""
    return (np.linalg.norm((p - c) / r, axis=-1) - 1.0) * float(np.min(r))


def smin(a, b, k):
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b + (a - b) * h - k * h * (1.0 - h)


def rot_y(v, deg):
    """Bend down (towards the palm, -Z) by deg about the Y axis."""
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return np.array([v[0] * c + v[2] * s, v[1], -v[0] * s + v[2] * c])


def rot_z(v, deg):
    """Spread about the Z axis (positive = towards +Y, the little finger side)."""
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return np.array([v[0] * c - v[1] * s, v[0] * s + v[1] * c, v[2]])


# Fingers: knuckle (MCP) position, phalanx lengths, radii at MCP/PIP/DIP/tip, spread, curl per joint.
FINGERS = {
    #          mcp                   proximal, middle, distal   radii                     spread  curl (mcp, pip, dip)
    "index":  ((9.4, -2.75, 0.15),   (4.0, 2.3, 1.75),          (1.02, 0.92, 0.84, 0.80), -5.0,   (8, 16, 10)),
    "middle": ((9.75, -0.85, 0.25),  (4.4, 2.75, 1.85),         (1.05, 0.95, 0.86, 0.82), 0.0,    (10, 20, 12)),
    "ring":   ((9.4, 1.05, 0.15),    (4.1, 2.6, 1.8),           (1.0, 0.9, 0.82, 0.78),   4.0,    (12, 24, 14)),
    "little": ((8.6, 2.85, -0.05),   (3.2, 1.95, 1.6),          (0.88, 0.78, 0.72, 0.68), 10.0,   (15, 28, 16)),
}
SQUASH = 0.86  # fingers are a little flatter than round


def finger_points(mcp, lengths, spread, curl):
    """Joint centres from the knuckle to the tip, and the direction of the back of the last bone."""
    pts = [np.array(mcp)]
    direction = rot_z(np.array([1.0, 0.0, 0.0]), spread)
    up = np.array([0.0, 0.0, 1.0])
    total = 0.0
    for length, bend in zip(lengths, curl):
        total += bend
        d = rot_y(direction, total)
        pts.append(pts[-1] + d * length)
    return pts, d, rot_y(up, total)


def finger_field(p, mcp, lengths, radii, spread, curl):
    pts, _, _ = finger_points(mcp, lengths, spread, curl)
    d = None
    for i in range(3):
        seg = round_cone(p, pts[i], pts[i + 1], radii[i], radii[i + 1], SQUASH)
        d = seg if d is None else smin(d, seg, 0.12)
    # Knuckle bumps on the back of the PIP and DIP joints.
    for i, r in ((1, radii[1]), (2, radii[2])):
        bump = ellipsoid(p, pts[i] + np.array([0, 0, 0.18 * r]), np.array([0.55, 0.85, 0.55]) * r)
        d = smin(d, bump, 0.15)
    return d, pts


def thumb_points():
    """Thumb joints (CMC, MCP, IP, tip centre), the last bone's direction and the way its nail faces."""
    cmc = np.array([1.7, -2.2, -0.5])
    direction = rot_y(rot_z(np.array([1.0, 0.0, 0.0]), -33.0), 24.0)
    mcp = cmc + direction * 4.3
    direction = rot_y(rot_z(direction, 14.0), 8.0)
    ip = mcp + direction * 3.1
    direction = rot_y(rot_z(direction, 6.0), 14.0)
    tip = ip + direction * 2.35
    # The thumb is turned about its own axis so its nail faces out to the front and up.
    out = np.array([0.0, -0.75, 0.66])
    up = out - direction * (out @ direction)
    return [cmc, mcp, ip, tip], direction, up / np.linalg.norm(up)


def hand_field(p):
    # Wrist stub (cut at x = 0 later): an oval matching the forearm end, 6.2 x 3.9 cm.
    d = round_cone(p, np.array([-3.0, 0.0, 0.0]), np.array([1.2, 0.0, 0.0]), 3.1, 3.0, 0.63)
    # Palm: tapered capsules along the metacarpals, flattened, fused into one slab.
    bases = {"index": (0.6, -1.9, 0.0), "middle": (0.6, -0.6, 0.1), "ring": (0.6, 0.7, 0.05), "little": (0.6, 1.9, -0.1)}
    for name, (mcp, *_rest) in FINGERS.items():
        mc = round_cone(p, np.array(bases[name]), np.array(mcp), 1.5, 1.15, 0.8)
        d = smin(d, mc, 1.2)
    # Thenar (thumb pad) and hypothenar (little finger edge) pads on the palm side.
    d = smin(d, ellipsoid(p, np.array([3.4, -2.3, -0.75]), np.array([2.7, 1.35, 1.25])), 0.9)
    d = smin(d, ellipsoid(p, np.array([4.4, 2.75, -0.55]), np.array([3.2, 0.95, 1.0])), 0.8)
    # Pad under the knuckles.
    d = smin(d, ellipsoid(p, np.array([8.6, -0.1, -0.55]), np.array([1.2, 3.9, 0.9])), 0.7)
    # Knuckles: the heads of the metacarpals stand up on the back of the hand.
    for name, (mcp, *_rest) in FINGERS.items():
        d = smin(d, ellipsoid(p, np.array(mcp) + np.array([-0.15, 0.0, 0.3]), np.array([0.95, 0.85, 0.75])), 0.35)
    palm = d
    fingers = None
    for name, (mcp, lengths, radii, spread, curl) in FINGERS.items():
        f, _ = finger_field(p, mcp, lengths, radii, spread, curl)
        fingers = f if fingers is None else np.minimum(fingers, f)
    d = smin(palm, fingers, 0.45)
    # Thumb: metacarpal from the base of the palm, swinging forward (-Y) and down.
    (cmc, mcp, ip, tip), _, _ = thumb_points()
    thumb = round_cone(p, cmc, mcp, 1.45, 1.08, 0.82)
    thumb = smin(thumb, round_cone(p, mcp, ip, 1.08, 0.98, 0.85), 0.15)
    thumb = smin(thumb, round_cone(p, ip, tip, 0.98, 0.88, 0.82), 0.15)
    d = smin(d, thumb, 0.75)
    return d


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "hand_l.obj"
    lo = np.array([-0.4, -9.0, -9.0])
    hi = np.array([20.0, 6.5, 3.5])
    shape = np.ceil((hi - lo) / VOXEL).astype(int) + 1
    xs = [lo[i] + np.arange(shape[i]) * VOXEL for i in range(3)]
    field = np.empty(shape, dtype=np.float32)
    for i, x in enumerate(xs[0]):
        grid = np.stack(np.meshgrid([x], xs[1], xs[2], indexing="ij"), axis=-1)[0]
        field[i] = hand_field(grid)
    # Open the wrist: everything at x < 0 is outside.
    field[xs[0] < 0.0] = np.maximum(field[xs[0] < 0.0], 0.05)
    verts, faces, normals, _ = measure.marching_cubes(field, 0.0, spacing=(VOXEL,) * 3)
    verts += lo
    # The cut leaves a cap at x ~ 0: drop faces lying on the wrist plane so the hand is open there.
    keep = ~np.all(verts[faces][:, :, 0] < 0.05, axis=1)
    faces = faces[keep]
    with open(out, "w") as f:
        f.write("# dense left hand, cm, +X to the fingers, -Y thumb side, -Z palm\n")
        # Nails for the painter: tip centre, the bone's direction, the way the nail faces, tip radius.
        for name, (mcp, lengths, radii, spread, curl) in FINGERS.items():
            pts, d, up = finger_points(mcp, lengths, spread, curl)
            f.write("#nail %s %.4f %.4f %.4f %.4f %.4f %.4f %.4f %.4f %.4f %.4f\n" % ((name, *pts[-1], *d, *up, radii[-1])))
        pts, d, up = thumb_points()
        f.write("#nail thumb %.4f %.4f %.4f %.4f %.4f %.4f %.4f %.4f %.4f %.4f\n" % ((*pts[-1], *d, *up, 0.88)))
        for v in verts:
            f.write("v %.4f %.4f %.4f\n" % tuple(v))
        for t in faces:
            # counter-clockwise seen from outside (the field is negative inside)
            f.write("f %d %d %d\n" % (t[0] + 1, t[1] + 1, t[2] + 1))
    print(out, len(verts), "vertices", len(faces), "triangles")


if __name__ == "__main__":
    main()

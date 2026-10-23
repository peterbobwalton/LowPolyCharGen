"""Repaint the Toon Soldiers UE5 pack's uniform texture with a digital (pixel) camouflage.

The pack ships one 2048 texture per uniform colour (T_Soldiers_Blue, _Olive, _Green ...) with the same layout:
only the cloth is coloured differently. So the cloth is found where two colourways differ, and repainted with
a blocky camo pattern, keeping the hand-painted shading (folds, seams, edges) of the original.

    python tools/digital_camo.py T_Soldiers_Blue.png T_Soldiers_Olive.png out.png [--palette olive] [--block 6]

The pattern is laid out in texture space, so it breaks at UV seams, which reads fine for a pixel camo.
"""
import argparse

import numpy as np
from PIL import Image

PALETTES = {
    # light to dark; the share of the surface each colour covers
    "olive": ([(0x8A, 0x86, 0x5C), (0x6B, 0x70, 0x42), (0x4C, 0x52, 0x30), (0x2E, 0x30, 0x22)], [0.22, 0.36, 0.28, 0.14]),
    "woodland": ([(0x8E, 0x7E, 0x5C), (0x5E, 0x66, 0x3E), (0x4A, 0x3C, 0x2A), (0x24, 0x26, 0x1E)], [0.25, 0.35, 0.25, 0.15]),
    "desert": ([(0xC8, 0xB4, 0x8C), (0xA8, 0x92, 0x6A), (0x80, 0x6C, 0x4E), (0x5A, 0x4C, 0x38)], [0.3, 0.32, 0.24, 0.14]),
    "urban": ([(0xA8, 0xA8, 0xA4), (0x80, 0x80, 0x7C), (0x5A, 0x5A, 0x58), (0x34, 0x34, 0x34)], [0.25, 0.35, 0.25, 0.15]),
}


def smooth_noise(shape, scale, rng):
    """Value noise: a coarse random grid, bilinearly upsampled."""
    h, w = shape
    gh, gw = int(np.ceil(h / scale)) + 2, int(np.ceil(w / scale)) + 2
    g = rng.random((gh, gw))
    y = np.arange(h) / scale
    x = np.arange(w) / scale
    y0, x0 = y.astype(int), x.astype(int)
    fy, fx = (y - y0)[:, None], (x - x0)[None, :]
    fy, fx = fy * fy * (3 - 2 * fy), fx * fx * (3 - 2 * fx)
    a = g[y0][:, x0]
    b = g[y0][:, x0 + 1]
    c = g[y0 + 1][:, x0]
    d = g[y0 + 1][:, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def camo_pattern(size, block, palette, seed):
    """Blocky multi-scale noise quantised to the palette's colours by their coverage."""
    colours, share = PALETTES[palette]
    rng = np.random.default_rng(seed)
    cells = -(-size // block)
    n = (smooth_noise((cells, cells), 9, rng) * 0.55 + smooth_noise((cells, cells), 4, rng) * 0.3
         + smooth_noise((cells, cells), 1.6, rng) * 0.15)
    cuts = np.quantile(n, np.cumsum(share)[:-1])
    index = np.digitize(n, cuts)            # 0 = lowest noise
    rgb = np.array(colours, float)[::-1][index] / 255.0   # dark colours where the noise is low
    return np.repeat(np.repeat(rgb, block, 0), block, 1)[:size, :size]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("source", help="the colourway to repaint (e.g. T_Soldiers_Blue.png)")
    ap.add_argument("other", help="another colourway of the same pack, to find the cloth")
    ap.add_argument("out")
    ap.add_argument("--palette", default="olive", choices=sorted(PALETTES))
    ap.add_argument("--block", type=int, default=6, help="camo pixel size in texels (2048 texture)")
    ap.add_argument("--seed", type=int, default=7)
    a = ap.parse_args()

    src = np.asarray(Image.open(a.source).convert("RGB"), float) / 255.0
    other = np.asarray(Image.open(a.other).convert("RGB").resize(src.shape[1::-1]), float) / 255.0
    size = src.shape[0]

    # Cloth: where the colourways differ. Soft edge so painted outlines blend in.
    diff = np.abs(src - other).max(-1)
    mask = np.clip((diff - 0.04) / 0.08, 0, 1)

    # Shading: the source's brightness relative to the typical cloth brightness, so the folds stay.
    lum = src @ np.array([0.299, 0.587, 0.114])
    ref = np.median(lum[mask > 0.9]) if (mask > 0.9).any() else 0.5
    shade = np.clip(lum / max(ref, 1e-3), 0.25, 1.35)[..., None]

    camo = camo_pattern(size, a.block, a.palette, a.seed) * shade
    out = src * (1 - mask[..., None]) + np.clip(camo, 0, 1) * mask[..., None]
    Image.fromarray((out * 255 + 0.5).astype(np.uint8)).save(a.out)
    print("wrote %s: %.0f%% of the texture repainted" % (a.out, 100 * mask.mean()))


if __name__ == "__main__":
    main()

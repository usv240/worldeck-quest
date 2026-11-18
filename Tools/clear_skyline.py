"""Takes the trees out of a photographed sky. The meadow's morning photo has a tree line, and one tree reaching 58
degrees up; behind the world's own low-poly trees and hills it read as two worlds at once. For each column this
finds where the sky stops (the first leaves, from the top), and from a little above there down to the horizon it
continues the sky: that column's own colour, easing to the photo's own horizon haze, blurred sideways so no column
shows. The half below the horizon is left as it is (the ground covers it).

Usage: python clear_skyline.py Assets/Worldeck/Skies/meadow_morning.jpg
"""
import sys

import numpy as np
from PIL import Image


def blur_rows(x, radius):
    """A soft blur along each row only (three box blurs), wrapping round the picture's edge."""
    x = x.astype(np.float32)
    n = 2 * radius + 1
    for _ in range(3):
        padded = np.concatenate([x[:, -radius - 1:], x, x[:, :radius]], axis=1)
        c = np.cumsum(padded, axis=1)
        x = (c[:, n:] - c[:, :-n]) / n
    return x


def main(path):
    im = Image.open(path).convert("RGB")
    a = np.asarray(im).astype(np.float32)
    h, w, _ = a.shape
    hor = h // 2
    top = a[:hor]
    r, g, b = top[..., 0], top[..., 1], top[..., 2]
    # Sky is blue-led or bright haze; leaves are green-led or dark.
    lumi = 0.3 * r + 0.59 * g + 0.11 * b
    sky = ((b >= g * 0.97) & (b > r * 0.98)) | ((lumi > 205) & (b > g * 0.9))
    not_sky = ~sky
    # A speck in the sky is not a tree: two in a column are.
    run = not_sky[:-1] & not_sky[1:]
    first = np.where(run.any(axis=0), run.argmax(axis=0), hor)        # per column: the first row of leaves
    # Trees have soft edges and neighbours: take the highest over the columns either side, and start well above it.
    pad = 36
    wrap = np.concatenate([first[-pad:], first, first[:pad]])
    first = np.array([wrap[i:i + 2 * pad + 1].min() for i in range(w)])
    start = np.clip(first - 34, 0, hor - 1).astype(np.float32)
    # No step where a tall tree begins: beside it the sky is continued from higher up too, easing down.
    start = np.minimum(start, blur_rows(start[None, :, None], 90)[0, :, 0])
    start = np.minimum(start, blur_rows(start[None, :, None], 90)[0, :, 0]).astype(int)
    # The haze at the horizon: the real sky just above where it is continued, averaged round the whole picture
    # (never sampled next to the trees themselves: at dusk that gave a dark band).
    haze = np.array([top[max(0, int(start[c]) - 2), c] for c in range(w)]).mean(axis=0)
    out = a.copy()
    filled = np.zeros((hor, w), np.float32)
    rows = np.arange(hor, dtype=np.float32)
    for c in range(w):
        s = int(start[c])
        if s >= hor - 1:
            continue
        colour = top[max(0, s - 3):s + 1, c].mean(axis=0)
        t = np.clip((rows[s:] - s) / max(1.0, hor - s), 0.0, 1.0)[:, None] ** 0.8
        out[s:hor, c] = colour[None, :] * (1 - t) + haze[None, :] * t
        filled[s:, c] = 1.0
    # Blur the continued sky sideways only (wrapping round), and feather upwards where it meets the real one.
    soft = blur_rows(out[:hor], 40)
    m = filled.copy()
    for c in range(w):
        s = int(start[c])
        if s < hor - 1:
            k = min(s, 24)
            m[s - k:s, c] = np.linspace(0.0, 1.0, k, endpoint=False) if k > 0 else 1.0
    m = blur_rows(m[..., None], 12)
    out[:hor] = soft * m + a[:hor] * (1 - m)
    Image.fromarray(np.clip(out, 0, 255).astype(np.uint8)).save(path, quality=92)
    print(f"{path}: sky continued in {int((start < hor - 1).sum())} of {w} columns; highest tree was {90 * (1 - first.min() / hor):.0f} degrees up")


if __name__ == "__main__":
    main(sys.argv[1])

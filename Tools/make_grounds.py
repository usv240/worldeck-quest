"""Photographic ground for Worldeck: Poly Haven texture sets (CC0) resized for the headset, plus grounds.json with
each set's average colour (linear), so the Ground shader can keep the world's own palette and add the photo's
detail on top.

    python -I Tools/make_grounds.py <folder with tex/> [output folder]
"""
import json
import os
import sys

import cv2
import numpy as np

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# set, albedo file, normal file, metres one texture repeat covers
SETS = [
    ("forest_floor", "forest_leaves_02/forest_leaves_02_diffuse_2k.jpg", "forest_leaves_02/forest_leaves_02_nor_gl_2k.jpg", 2.2),
    ("sand", "dense_sand/dense_sand_diff_1k.jpg", "dense_sand/dense_sand_nor_gl_1k.jpg", 1.6),
    ("dunes", "aerial_beach_01/aerial_beach_01_diff_2k.jpg", "aerial_beach_01/aerial_beach_01_nor_gl_2k.jpg", 4.0),
    ("snow", "snow_02/snow_02_diff_2k.jpg", "snow_02/snow_02_nor_gl_2k.jpg", 2.4),
    ("grass", "leafy_grass/leafy_grass_diff_1k.jpg", "leafy_grass/leafy_grass_nor_gl_1k.jpg", 1.6),
    ("cobbles", "cobblestone_floor_08/cobblestone_floor_08_diff_2k.jpg", "cobblestone_floor_08/cobblestone_floor_08_nor_gl_2k.jpg", 2.0),
    ("coral_gravel", "coral_gravel/coral_gravel_diff_1k.jpg", "coral_gravel/coral_gravel_nor_gl_1k.jpg", 1.6),
    ("rock", "rock_face_03/rock_face_03_diff_1k.jpg", "rock_face_03/rock_face_03_nor_gl_1k.jpg", 1.5),
    ("moon", "moon_01/moon_01_diff_2k.jpg", "moon_01/moon_01_nor_gl_2k.jpg", 3.0),
]


def srgb_to_linear(x):
    return np.where(x <= 0.04045, x / 12.92, np.power((x + 0.055) / 1.055, 2.4))


def main():
    src = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(PROJECT, "Assets", "Worldeck", "Grounds")
    os.makedirs(out, exist_ok=True)
    meta = {}
    for name, alb, nor, metres in SETS:
        a = cv2.imread(os.path.join(src, "tex", alb), cv2.IMREAD_COLOR)
        n = cv2.imread(os.path.join(src, "tex", nor), cv2.IMREAD_COLOR)
        if a is None or n is None:
            raise SystemExit("missing " + name)
        a = cv2.resize(a, (1024, 1024), interpolation=cv2.INTER_AREA)
        n = cv2.resize(n, (1024, 1024), interpolation=cv2.INTER_AREA)
        cv2.imwrite(os.path.join(out, name + "_albedo.jpg"), a, [cv2.IMWRITE_JPEG_QUALITY, 92])
        cv2.imwrite(os.path.join(out, name + "_normal.png"), n)
        lin = srgb_to_linear(cv2.cvtColor(a, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0)
        mean = lin.reshape(-1, 3).mean(axis=0)
        meta[name] = {"mean": [round(float(c), 4) for c in mean], "metres": metres}
        print(f"{name:<13} mean {np.round(mean, 3)}  repeat {metres} m")
    with open(os.path.join(out, "grounds.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=1)
    print("wrote", len(meta), "ground sets to", out)


if __name__ == "__main__":
    main()

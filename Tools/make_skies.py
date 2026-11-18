"""Photographic skies for Worldeck.

Turns the Poly Haven HDR skies (CC0) and NASA's star map (public domain) into tonemapped equirectangular JPGs the
headset can draw, plus skies.json: for each sky, the sun's direction and colour, the horizon colour (the world's
fog meets it), and the average sky light (the world's ambient), so the procedural land is lit like the photo.

    python -I Tools/make_skies.py <folder with hdri/ and nasa/>

The sources stay outside the repo (they are large); only the small results are kept in Assets/Worldeck/Skies.
"""
import json
import math
import os
import sys

os.environ.setdefault("OPENCV_IO_ENABLE_OPENEXR", "1")
import cv2  # noqa: E402
import numpy as np  # noqa: E402

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(PROJECT, "Assets", "Worldeck", "Skies")

# id, source file, output width, kind ("day" has a sun to find; "night" keeps the stars; "space")
SKIES = [
    ("campfire_night", "hdri/dikhololo_night_4k.hdr", 4096, "night"),
    ("campfire_dawn", "hdri/misty_pines_2k.hdr", 2048, "day"),
    ("campfire_day", "hdri/forest_slope_2k.hdr", 2048, "day"),
    ("campfire_sunset", "hdri/sunset_forest_2k.hdr", 2048, "day"),
    ("beach_sunset", "hdri/belfast_sunset_puresky_2k.hdr", 2048, "day"),
    ("beach_day", "hdri/secluded_beach_2k.hdr", 2048, "day"),
    ("desert_golden", "hdri/quarry_01_2k.hdr", 2048, "day"),
    ("desert_day", "hdri/quarry_03_2k.hdr", 2048, "day"),
    ("desert_dawn", "hdri/aarfontein_dawn_2_2k.hdr", 2048, "day"),
    ("desert_night", "hdri/rogland_clear_night_2k.hdr", 2048, "night"),
    ("snow_sunrise", "hdri/stierberg_sunrise_2k.hdr", 2048, "day"),
    ("snow_day", "hdri/passendorf_snow_2k.hdr", 2048, "day"),
    ("city_night", "hdri/kloppenheim_07_puresky_2k.hdr", 2048, "night"),
    ("city_day", "hdri/kloofendal_overcast_puresky_2k.hdr", 2048, "day"),
    ("meadow_morning", "hdri/meadow_2_2k.hdr", 2048, "day"),
    ("meadow_evening", "hdri/wildflower_field_2k.hdr", 2048, "day"),
    ("meadow_night", "hdri/kloppenheim_02_2k.hdr", 2048, "night"),
    ("open_dawn", "hdri/qwantani_dawn_puresky_2k.hdr", 2048, "day"),
    ("open_noon", "hdri/qwantani_noon_puresky_2k.hdr", 2048, "day"),
    ("open_sunset", "hdri/qwantani_sunset_puresky_2k.hdr", 2048, "day"),
    ("open_dusk", "hdri/qwantani_dusk_2_puresky_2k.hdr", 2048, "day"),
    ("open_moonrise", "hdri/qwantani_moonrise_puresky_2k.hdr", 2048, "night"),
]


def read(path):
    img = cv2.imread(path, cv2.IMREAD_ANYDEPTH | cv2.IMREAD_COLOR)
    if img is None:
        raise SystemExit("could not read " + path)
    img = cv2.cvtColor(img.astype(np.float32), cv2.COLOR_BGR2RGB)
    return np.nan_to_num(np.maximum(img, 0.0), nan=0.0, posinf=0.0)


def lum(x):
    return 0.2126 * x[..., 0] + 0.7152 * x[..., 1] + 0.0722 * x[..., 2]


def aces(x):
    # Narkowicz's fit of the ACES filmic curve: soft highlights, rich mid-tones.
    a, b, c, d, e = 2.51, 0.03, 2.43, 0.59, 0.14
    return np.clip((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0)


def to_srgb(x):
    return np.where(x <= 0.0031308, 12.92 * x, 1.055 * np.power(np.maximum(x, 1e-9), 1.0 / 2.4) - 0.055)


def direction(u, v):
    """Equirect pixel centre (u, v in 0..1) to a world direction, in the layout the Sky shader samples:
    u = 0.5 looks along +Z, u grows towards +X; v = 0 is straight up."""
    phi = (u - 0.5) * 2.0 * math.pi
    theta = v * math.pi
    return (math.sin(theta) * math.sin(phi), math.cos(theta), math.sin(theta) * math.cos(phi))


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(OUT, exist_ok=True)
    meta = {}
    for sid, rel, width, kind in SKIES:
        img = read(os.path.join(src, rel))
        h0, w0 = img.shape[:2]
        rows = np.linspace(0.0, 1.0, h0, endpoint=False) + 0.5 / h0
        weight = np.sin(rows * math.pi)[:, None]          # solid angle per row
        L = lum(img)
        upper = L[: h0 // 2]
        med = float(np.median(upper))

        # The sun: the brightest 0.02 % of the sky, if it stands far above the rest.
        sun = None
        sun_color = [1.0, 0.95, 0.85]
        if kind == "day":
            k = max(1, int(L.size * 0.0002))
            idx = np.argpartition(L.ravel(), -k)[-k:]
            peak = float(L.ravel()[idx].mean())
            if peak > med * 40.0:
                ys, xs = np.unravel_index(idx, L.shape)
                wts = L.ravel()[idx]
                u = float((xs + 0.5).dot(wts) / wts.sum() / w0)
                v = float((ys + 0.5).dot(wts) / wts.sum() / h0)
                sun = direction(u, v)
                col = img.reshape(-1, 3)[idx].mean(axis=0)
                sun_color = [float(c) for c in col / max(1e-6, col.max())]

        # Exposure: the sky's middle brightness to a mid-grey (darker at night, so night stays night).
        target = {"day": 0.30, "night": 0.10, "space": 0.02}[kind]
        exposure = target / max(med, 1e-6)
        if kind == "night":
            exposure = min(exposure, 6.0 / max(float(np.percentile(upper, 99.5)), 1e-6))
        if kind == "space":
            exposure = 0.35 / max(float(np.percentile(L, 99.0)), 1e-6)
        out = aces(img * exposure)

        # Average sky light (the world's ambient), the horizon band (the fog), the ground below.
        def band(a, b):
            r0, r1 = int(a * h0), max(int(a * h0) + 1, int(b * h0))
            seg = out[r0:r1]
            w = weight[r0:r1]
            return [float(c) for c in (seg * w[..., None]).sum(axis=(0, 1)) / max(1e-6, w.sum() * w0)]

        horizon = band(0.475, 0.51)
        sky = band(0.0, 0.5)
        ground = band(0.52, 0.75)

        small = cv2.resize(out, (width, width // 2), interpolation=cv2.INTER_AREA)
        srgb = np.clip(to_srgb(small) * 255.0 + 0.5, 0, 255).astype(np.uint8)
        cv2.imwrite(os.path.join(OUT, sid + ".jpg"), cv2.cvtColor(srgb, cv2.COLOR_RGB2BGR), [cv2.IMWRITE_JPEG_QUALITY, 92])
        meta[sid] = {
            "source": os.path.basename(rel), "kind": kind, "width": width,
            "sun": [round(c, 4) for c in sun] if sun else None, "sun_color": [round(c, 3) for c in sun_color],
            "horizon": [round(c, 4) for c in horizon], "sky": [round(c, 4) for c in sky], "ground": [round(c, 4) for c in ground],
        }
        el = math.degrees(math.asin(sun[1])) if sun else None
        print(f"{sid:<16} {w0}x{h0} -> {width}x{width // 2}  median {med:.4f}  exposure {exposure:.2f}  "
              f"sun {'%.0f deg up' % el if el is not None else '-'}  horizon {[round(c, 2) for c in horizon]}")
    with open(os.path.join(OUT, "skies.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=1)
    print("wrote", len(meta), "skies to", OUT)


if __name__ == "__main__":
    main()

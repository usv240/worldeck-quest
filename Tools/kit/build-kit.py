"""Turns chosen CC0 Kenney models (OBJ) into one compact file the app's MeshBuilder reads: Assets/Worldeck/Resources/Kit/kit.bytes.

Usage: python -I Tools/kit/build-kit.py <folder with the unzipped Kenney packs>

Each model becomes flat-coloured triangles (the colour of its material, or of its colour map at the face's centre),
normalised so its base sits at y=0, centred, and 1 unit tall. The app scales it to the height that kind of prop
should have, tints it toward the world's light, and lets leaves sway in the wind.

Format (little endian): "WKIT", u16 version=1, u16 models; per model: u8 name length, name (utf8), u8 palette size,
palette as RGBA bytes, f32 width/height ratio, u32 triangles, then per triangle 9 x i16 (position * 8192) and u8
palette index.
"""
import os
import struct
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import glb   # noqa: E402
from made import MADE   # noqa: E402

PACKS = sys.argv[1] if len(sys.argv) > 1 else "."
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Assets", "Worldeck", "Resources", "Kit", "kit.bytes")

# kind -> models (pack/name). A kind with several models picks one per prop at random.
KINDS = {
    "pine": [("nature-kit", n) for n in ["tree_pineTallA", "tree_pineTallB", "tree_pineTallC", "tree_pineTallD", "tree_pineDefaultA", "tree_pineDefaultB", "tree_pineRoundA", "tree_pineRoundC"]],
    # Snowy pines and rocks are the plain ones; the app caps their upward faces with snow.
    "snow_pine": [("nature-kit", n) for n in ["tree_pineTallA", "tree_pineTallB", "tree_pineDefaultA", "tree_pineRoundA"]],
    "rock": [("nature-kit", n) for n in ["rock_largeA", "rock_largeB", "rock_largeC", "rock_largeD", "rock_largeE", "rock_largeF"]],
    "snow_rock": [("nature-kit", n) for n in ["rock_largeA", "rock_largeB", "rock_largeC", "rock_largeD"]],
    "fern": [("nature-kit", n) for n in ["plant_bush", "plant_bushDetailed", "plant_flatTall", "grass_leafsLarge"]],
    "palm": [("nature-kit", n) for n in ["tree_palmTall", "tree_palmBend", "tree_palmDetailedTall", "tree_palm"]],
    "cactus": [("nature-kit", n) for n in ["cactus_tall", "cactus_short"]],
    "dune_grass": [("nature-kit", n) for n in ["grass_large", "grass"]],
    "oak": [("nature-kit", n) for n in ["tree_oak", "tree_default", "tree_fat", "tree_tall"]],
    "bush": [("nature-kit", n) for n in ["plant_bushLarge", "plant_bushDetailed", "plant_bush"]],
    "flower_patch": [("nature-kit", n) for n in ["flower_purpleA", "flower_redA", "flower_yellowA", "flower_purpleC", "flower_redC", "flower_yellowC"]],
    "flower": [("nature-kit", n) for n in ["flower_purpleB", "flower_redB", "flower_yellowB"]],
    "stone": [("nature-kit", n) for n in ["stone_smallA", "stone_smallB", "stone_smallC", "stone_smallFlatA"]],
    "log_small": [("nature-kit", n) for n in ["log", "log_large"]],
    "building": [("city-kit-commercial", "low-detail-building-" + n) for n in "abcdefgijklmn"] + [("city-kit-commercial", "low-detail-building-wide-a")],
    "street_lamp": [("city-kit-roads", n) for n in ["light-curved", "light-square"]],
    "antenna": [("space-kit", n) for n in ["satelliteDish", "satelliteDish_detailed"]],
    # Set dressing the worlds did not have before.
    "mushroom": [("nature-kit", n) for n in ["mushroom_red", "mushroom_redGroup", "mushroom_tan", "mushroom_tanGroup"]],
    "stump": [("nature-kit", n) for n in ["stump_old", "stump_round", "stump_roundDetailed"]],
    "tent": [("nature-kit", n) for n in ["tent_detailedOpen"]],
    "log_stack": [("nature-kit", n) for n in ["log_stack", "log_stackLarge"]],
    "lantern_hanging": [("holiday-kit", n) for n in ["lantern-hanging"]],
    "rover": [("space-kit", n) for n in ["rover"]],
    "crate_space": [("space-station-kit", n) for n in ["container", "container-tall", "container-wide"]],
    "console": [("space-station-kit", n) for n in ["computer", "computer-system", "table-display-planet"]],
    "bar_stool": [("furniture-kit", n) for n in ["stoolBar", "stoolBarSquare"]],
    "lounge_chair": [("furniture-kit", n) for n in ["loungeDesignChair", "loungeChair"]],
    "speaker": [("furniture-kit", n) for n in ["speaker"]],
    "potted_plant": [("furniture-kit", n) for n in ["pottedPlant", "plantSmall1"]],
    # For composed places (volcano, Mars, autumn woods, a pumpkin patch...).
    "fall_tree": [("nature-kit", n) for n in ["tree_oak_fall", "tree_default_fall", "tree_fat_fall", "tree_tall_fall", "tree_simple_fall"]],
    "crater": [("space-kit", n) for n in ["crater", "craterLarge"]],
    "crystal_rock": [("space-kit", n) for n in ["rock_crystals", "rock_crystalsLargeA", "rock_crystalsLargeB"]],
    "meteor_rock": [("space-kit", n) for n in ["meteor", "meteor_half"]],
    "pumpkin": [("nature-kit", "crop_pumpkin")],
    "corn": [("nature-kit", n) for n in ["crops_cornStageC", "crops_cornStageD"]],
    "fence": [("nature-kit", "fence_simple")],
    "canoe": [("nature-kit", "canoe")],
    "lily": [("nature-kit", n) for n in ["lily_large", "lily_small"]],
    "parasol": [("city-kit-commercial", n) for n in ["detail-parasol-a", "detail-parasol-b"]],
    "cone": [("city-kit-roads", "construction-cone")],
    "traffic_light": [("city-kit-roads", "traffic-light")],
    "present": [("holiday-kit", n) for n in ["present-a-cube", "present-a-round", "present-b-cube", "present-b-rectangle"]],
    "candy_cane": [("holiday-kit", n) for n in ["candy-cane-red", "candy-cane-green"]],
    "bookcase": [("furniture-kit", n) for n in ["bookcaseOpen", "bookcaseClosedWide"]],
    "floor_lamp": [("furniture-kit", n) for n in ["lampRoundFloor", "lampSquareFloor"]],
    "teddy": [("furniture-kit", "bear")],
    "radio": [("furniture-kit", "radio")],
    "generator": [("space-kit", n) for n in ["machine_generator", "machine_barrel"]],
    "barrels": [("space-kit", n) for n in ["barrels", "barrel"]],
}


def obj_dir(pack):
    base = os.path.join(PACKS, pack, "x", "Models")
    for d in os.listdir(base):
        if d.lower().startswith("obj"):
            return os.path.join(base, d)
    raise FileNotFoundError(base)


images = {}


def read_mtl(path):
    mats, cur = {}, None
    for line in open(path, encoding="utf-8", errors="replace"):
        p = line.split()
        if not p:
            continue
        if p[0] == "newmtl":
            cur = p[1]
            mats[cur] = {"kd": (1.0, 1.0, 1.0), "map": None}
        elif p[0] == "Kd" and cur:
            mats[cur]["kd"] = tuple(float(x) for x in p[1:4])
        elif p[0] == "map_Kd" and cur:
            tex = os.path.join(os.path.dirname(path), " ".join(p[1:]))
            if tex not in images:
                images[tex] = Image.open(tex).convert("RGBA")
            mats[cur]["map"] = tex
    return mats


def read_obj(path):
    v, vt, faces = [], [], []
    mats = {}
    mat = None
    for line in open(path, encoding="utf-8", errors="replace"):
        p = line.split()
        if not p:
            continue
        if p[0] == "v":
            v.append(tuple(float(x) for x in p[1:4]))
        elif p[0] == "vt":
            vt.append(tuple(float(x) for x in p[1:3]))
        elif p[0] == "mtllib":
            mats = read_mtl(os.path.join(os.path.dirname(path), " ".join(p[1:])))
        elif p[0] == "usemtl":
            mat = p[1]
        elif p[0] == "f":
            idx = []
            for c in p[1:]:
                parts = c.split("/")
                vi = int(parts[0])
                ti = int(parts[1]) if len(parts) > 1 and parts[1] else 0
                idx.append((vi - 1 if vi > 0 else len(v) + vi, (ti - 1 if ti > 0 else len(vt) + ti) if ti else None))
            faces.append((mat, idx))
    tris = []
    for mat, idx in faces:
        m = mats.get(mat, {"kd": (0.7, 0.7, 0.7), "map": None})
        for k in range(1, len(idx) - 1):
            a, b, c = idx[0], idx[k], idx[k + 1]
            if m["map"] and a[1] is not None:
                img = images[m["map"]]
                u = (vt[a[1]][0] + vt[b[1]][0] + vt[c[1]][0]) / 3.0
                w = (vt[a[1]][1] + vt[b[1]][1] + vt[c[1]][1]) / 3.0
                x = min(img.width - 1, max(0, int((u % 1.0) * img.width)))
                y = min(img.height - 1, max(0, int((1.0 - (w % 1.0)) * img.height)))
                r, g, bl, _ = img.getpixel((x, y))
                col = (r, g, bl, 255)
            else:
                col = tuple(int(round(max(0.0, min(1.0, x)) * 255)) for x in m["kd"]) + (255,)
            tris.append((v[a[0]], v[b[0]], v[c[0]], col))
    return tris


def normalise(tris):
    xs = [p[0] for t in tris for p in t[:3]]
    ys = [p[1] for t in tris for p in t[:3]]
    zs = [p[2] for t in tris for p in t[:3]]
    cx, cz, y0 = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2, min(ys)
    h = max(ys) - y0
    if h <= 1e-6:
        h = max(max(xs) - min(xs), max(zs) - min(zs), 1e-3)
    width = max(max(xs) - min(xs), max(zs) - min(zs)) / h
    out = []
    for a, b, c, col in tris:
        out.append(tuple(((p[0] - cx) / h, (p[1] - y0) / h, (p[2] - cz) / h) for p in (a, b, c)) + (col,))
    return out, width


def q(x):
    return max(-32767, min(32767, int(round(x * 8192))))


# The things you can ask for join the kit (their own kinds; never scattered by a world).
for _kind, (_size, _words, _srcs) in MADE.items():
    KINDS[_kind] = list(_srcs)

CS_OUT = os.path.join(HERE, "..", "..", "Assets", "Worldeck", "Scripts", "Core", "MadeKinds.cs")
JS_OUT = os.path.join(HERE, "..", "..", "proxy", "made-kinds.js")


def write_lists():
    rows = []
    for kind, (size, words, _) in MADE.items():
        ws = ", ".join('"' + w.replace('"', '') + '"' for w in words)
        rows.append(f'            ("{kind}", {size:.2f}f, new[] {{ {ws} }}),')
    cs = (
        "// Generated by Tools/kit/build-kit.py from Tools/kit/made.py. Do not edit by hand.\n"
        "namespace Worldeck.Core\n{\n"
        "    /// <summary>Things you can ask for that are real models in the kit (Kenney, Quaternius; CC0): kind, size when\n"
        "    /// made (metres, largest side), and the words that name it.</summary>\n"
        "    public static class MadeKinds\n    {\n"
        "        public static readonly (string Kind, float Size, string[] Words)[] All =\n        {\n"
        + "\n".join(rows) + "\n        };\n\n"
        "        public static float SizeOf(string kind)\n        {\n"
        "            foreach (var m in All) if (m.Kind == kind) return m.Size;\n            return 0f;\n        }\n\n"
        "        public static bool Has(string kind) => SizeOf(kind) > 0f;\n    }\n}\n")
    open(CS_OUT, "w", encoding="utf-8", newline="\n").write(cs)
    js = ("// Generated by Tools/kit/build-kit.py from Tools/kit/made.py. Do not edit by hand.\n"
          "// Things the app can make from real models: the relay offers these before building from shapes.\n"
          "export const MADE_KINDS = " + __import__("json").dumps({k: v[1] for k, v in MADE.items()}, indent=1) + ";\n")
    open(JS_OUT, "w", encoding="utf-8", newline="\n").write(js)
    print(f"{len(MADE)} made kinds -> MadeKinds.cs, made-kinds.js")


def main():
    write_lists()
    models = []
    for kind, list_ in KINDS.items():
        for i, (pack, name) in enumerate(list_):
            if pack.startswith("glb:"):
                # Kenney's characters are exported arms out: they are baked standing with their arms down.
                pose = {"arm-left": (-55, 0), "arm-right": (55, 0)} if name.startswith("character-") else None
                tris = glb.read(os.path.join(PACKS, pack[4:], name + ".glb"), pose)
            else:
                tris = read_obj(os.path.join(obj_dir(pack), name + ".obj"))
            tris, width = normalise(tris)
            models.append((kind + "#" + str(i), tris, width, pack + "/" + name))
    body = bytearray(b"WKIT")
    body += struct.pack("<HH", 1, len(models))
    total = 0
    for key, tris, width, src in models:
        palette = []
        for t in tris:
            if t[3] not in palette:
                palette.append(t[3])
        if len(palette) >= 256:
            # A textured model with many shades: round them (8 levels a channel is plenty for flat shading).
            quant = lambda c: tuple(min(255, (v // 32) * 32 + 16) for v in c[:3]) + (255,)
            tris = [(a, b, c, quant(col)) for a, b, c, col in tris]
            palette = []
            for t in tris:
                if t[3] not in palette:
                    palette.append(t[3])
        assert len(palette) < 256, (src, len(palette))
        name = key.encode("utf-8")
        body += struct.pack("<B", len(name)) + name
        body += struct.pack("<B", len(palette))
        for c in palette:
            body += bytes(c)
        body += struct.pack("<fI", width, len(tris))
        for a, b, c, col in tris:
            body += struct.pack("<9hB", q(a[0]), q(a[1]), q(a[2]), q(b[0]), q(b[1]), q(b[2]), q(c[0]), q(c[1]), q(c[2]), palette.index(col))
        total += len(tris)
        print(f"{key:22s} {src:45s} {len(tris):6d} tris {len(palette):3d} colours  w/h {width:.2f}")
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    open(OUT, "wb").write(bytes(body))
    print(f"{len(models)} models, {total} triangles, {len(body) / 1024:.0f} KB -> {os.path.normpath(OUT)}")


if __name__ == "__main__":
    main()

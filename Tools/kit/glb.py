"""Reads a GLB (binary glTF 2.0) into flat-coloured triangles, the same shape build-kit.py makes from OBJ:
[(a, b, c, (r, g, b, 255)), ...] with positions in the model's own space (node transforms applied, glTF's
right-handed Y-up). Colours: a material's base colour factor (linear, converted to sRGB) or its base colour
texture sampled at the face's centre."""
import io
import json
import math
import struct

from PIL import Image

COMP = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2), 5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}
NCOMP = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def _lin_to_srgb(c):
    c = max(0.0, min(1.0, c))
    return 12.92 * c if c <= 0.0031308 else 1.055 * (c ** (1 / 2.4)) - 0.055


def _mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def _node_matrix(n):
    if "matrix" in n:
        m = n["matrix"]
        return [[m[c * 4 + r] for c in range(4)] for r in range(4)]   # column-major
    t = n.get("translation", [0, 0, 0])
    x, y, z, w = n.get("rotation", [0, 0, 0, 1])
    s = n.get("scale", [1, 1, 1])
    r = [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
         [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
         [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]
    return [[r[0][0] * s[0], r[0][1] * s[1], r[0][2] * s[2], t[0]],
            [r[1][0] * s[0], r[1][1] * s[1], r[1][2] * s[2], t[1]],
            [r[2][0] * s[0], r[2][1] * s[1], r[2][2] * s[2], t[2]],
            [0, 0, 0, 1]]


def _apply(m, p):
    return (m[0][0] * p[0] + m[0][1] * p[1] + m[0][2] * p[2] + m[0][3],
            m[1][0] * p[0] + m[1][1] * p[1] + m[1][2] * p[2] + m[1][3],
            m[2][0] * p[0] + m[2][1] * p[1] + m[2][2] * p[2] + m[2][3])


def _rot_z(deg):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return [[c, -s, 0, 0], [s, c, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]]


def _rot_x(deg):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return [[1, 0, 0, 0], [0, c, -s, 0], [0, s, c, 0], [0, 0, 0, 1]]


def read(path, pose=None):
    """pose: {joint name: (degrees about Z, degrees about X)} turns those joints of a skinned model (Kenney's
    characters are exported arms out; {"arm-left": (-72, 0), "arm-right": (72, 0)} puts their arms down). Without
    a pose, or for a model with no skin, the model is read as exported."""
    b = open(path, "rb").read()
    assert b[:4] == b"glTF", path
    jl = struct.unpack("<I", b[12:16])[0]
    j = json.loads(b[20:20 + jl])
    off = 20 + jl
    binl = struct.unpack("<I", b[off:off + 4])[0]
    blob = b[off + 8:off + 8 + binl]

    def accessor(i):
        a = j["accessors"][i]
        bv = j["bufferViews"][a["bufferView"]]
        fmt, size = COMP[a["componentType"]]
        n = NCOMP[a["type"]]
        start = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        stride = bv.get("byteStride", size * n)
        out = []
        for k in range(a["count"]):
            o = start + k * stride
            v = struct.unpack_from("<" + fmt * n, blob, o)
            if a.get("normalized") and fmt in "BH":
                v = tuple(x / (255.0 if fmt == "B" else 65535.0) for x in v)
            out.append(v if n > 1 else v[0])
        return out

    images = {}

    def image(i):
        if i not in images:
            im = j["images"][i]
            if "bufferView" in im:
                bv = j["bufferViews"][im["bufferView"]]
                data = blob[bv.get("byteOffset", 0):bv.get("byteOffset", 0) + bv["byteLength"]]
                images[i] = Image.open(io.BytesIO(data)).convert("RGBA")
            else:
                # An image next to the file (Kenney: Textures/colormap.png).
                import os
                images[i] = Image.open(os.path.join(os.path.dirname(path), im["uri"])).convert("RGBA")
        return images[i]

    # Global transform of every node.
    parent = {}
    for ni, n in enumerate(j["nodes"]):
        for c in n.get("children", []):
            parent[c] = ni
    glob = {}

    def world(ni):
        if ni not in glob:
            m = _node_matrix(j["nodes"][ni])
            glob[ni] = _mat_mul(world(parent[ni]), m) if ni in parent else m
        return glob[ni]

    # A pose: the same hierarchy with some joints turned.
    posed = {}

    def world_posed(ni):
        if ni not in posed:
            n = j["nodes"][ni]
            m = _node_matrix(n)
            turn = pose.get(n.get("name", "")) if pose else None
            if turn:
                m = _mat_mul(m, _mat_mul(_rot_z(turn[0]), _rot_x(turn[1])))
            posed[ni] = _mat_mul(world_posed(parent[ni]), m) if ni in parent else m
        return posed[ni]

    def skin_matrices(si):
        skin = j["skins"][si]
        ibm = accessor(skin["inverseBindMatrices"])
        out = []
        for k, joint in enumerate(skin["joints"]):
            inv = [[ibm[k][c * 4 + r] for c in range(4)] for r in range(4)]   # column-major
            out.append(_mat_mul(world_posed(joint), inv))
        return out

    tris = []
    for ni, n in enumerate(j["nodes"]):
        if "mesh" not in n:
            continue
        # Skinned meshes are exported in their bind pose; the mesh node's own transform still places them.
        m = world(ni)
        joints_m = skin_matrices(n["skin"]) if pose and "skin" in n else None
        for prim in j["meshes"][n["mesh"]]["primitives"]:
            if prim.get("mode", 4) != 4:
                continue
            raw = accessor(prim["attributes"]["POSITION"])
            if joints_m is not None and "JOINTS_0" in prim["attributes"] and "WEIGHTS_0" in prim["attributes"]:
                # Each corner follows its joints (a skinned mesh ignores its own node's transform).
                jn, wt = accessor(prim["attributes"]["JOINTS_0"]), accessor(prim["attributes"]["WEIGHTS_0"])
                pos = []
                for p, js, ws in zip(raw, jn, wt):
                    total = sum(ws) or 1.0
                    x = y = z = 0.0
                    for ji, w in zip(js, ws):
                        if w <= 0:
                            continue
                        q = _apply(joints_m[int(ji)], p)
                        x += q[0] * w / total; y += q[1] * w / total; z += q[2] * w / total
                    pos.append((x, y, z))
            else:
                pos = [_apply(m, p) for p in raw]
            uv = accessor(prim["attributes"]["TEXCOORD_0"]) if "TEXCOORD_0" in prim["attributes"] else None
            idx = accessor(prim["indices"]) if "indices" in prim else list(range(len(pos)))
            mat = j["materials"][prim["material"]] if "material" in prim else {}
            pbr = mat.get("pbrMetallicRoughness", {})
            fac = pbr.get("baseColorFactor", [1, 1, 1, 1])
            tex = pbr.get("baseColorTexture")
            img = image(j["textures"][tex["index"]]["source"]) if tex is not None and uv is not None else None
            flat = tuple(int(round(_lin_to_srgb(c) * 255)) for c in fac[:3]) + (255,)
            for k in range(0, len(idx) - 2, 3):
                a, bb, c = idx[k], idx[k + 1], idx[k + 2]
                if img is not None:
                    u = (uv[a][0] + uv[bb][0] + uv[c][0]) / 3.0
                    v = (uv[a][1] + uv[bb][1] + uv[c][1]) / 3.0
                    x = min(img.width - 1, max(0, int((u % 1.0) * img.width)))
                    y = min(img.height - 1, max(0, int((v % 1.0) * img.height)))
                    r, g, bl, _ = img.getpixel((x, y))
                    # The texture is sRGB already; the factor is linear.
                    col = (int(r * fac[0]), int(g * fac[1]), int(bl * fac[2]), 255)
                else:
                    col = flat
                tris.append((pos[a], pos[bb], pos[c], col))
    return tris

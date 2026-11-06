# 素晴 COFFEE SHOP —— Blender 建模脚本
# 运行: blender -b --python build_shop.py
# 坐标: 平面图像素 (px, py) -> 世界坐标 (米)，X 向右(东)，Y 向上(北)，Z 向上
import bpy, bmesh, math, random, os
import numpy as np
from mathutils import Matrix, Vector, Euler

ROOT = os.path.dirname(os.path.abspath(__file__)) if "__file__" in dir() else r"D:\coffee_shop\仅供参考的文件"
ROOT = os.path.normpath(ROOT)   # 资产源目录（仅供参考的文件/）
PHOTOS = os.path.normpath(os.path.join(ROOT, "..", "photos"))
TEX = os.path.join(ROOT, "textures")
REFS = os.path.join(ROOT, "refs")
os.makedirs(TEX, exist_ok=True)

# ---------------- 可调参数 ----------------
SCALE = 1.25           # 2026-11-06 店面整体放大 25%（用户决策）：原比例下
                       # 圆桌组 1.2m 过道 / 阳光区斜窗吧凳 / 平面图所有座位互相排斥，无解
S = 1.5 / 147 * SCALE  # 平面图 1 大格(147px) ≈ 1.5 m × 整体放大系数
H = 4.5                # 黑色格栅吊顶高度
T = 0.2                # 墙厚
# 06 票：前柜（含收银/吧凳/吊架/吊灯/低吊顶）整体北移，使前后柜通道 = 1.20m
BAR_SHIFT = 1.20 - ((1360 - 1255) * S - 0.75)
random.seed(7)


def P(px, py):
    return ((px - 150) * S, (1360 - py) * S)


# ---------------- 清空场景 ----------------
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'

COLLS = {}
for cname in ["Structure", "Ceiling", "Furniture", "Bar", "Lighting", "Plants", "Decor"]:
    c = bpy.data.collections.new(cname)
    scene.collection.children.link(c)
    COLLS[cname] = c


# ---------------- 贴图生成 ----------------
def save_img(name, arr):
    h, w, _ = arr.shape
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    path = os.path.join(TEX, name + ".png")
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    return img


def rgba(arr3):
    h, w, _ = arr3.shape
    return np.concatenate([arr3, np.ones((h, w, 1))], axis=2)


def load_pixels(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)   # 行0 = 图片底部


def crop_image(src, fx0, fy0, fx1, fy1, name, out_w=1024):
    """fx/fy 为从左上角量的比例坐标"""
    px = load_pixels(src)
    h, w, _ = px.shape
    x0, x1 = int(fx0 * w), int(fx1 * w)
    r0, r1 = int((1 - fy1) * h), int((1 - fy0) * h)
    sub = px[r0:r1, x0:x1].copy()
    img = save_img(name, sub)
    sh, sw = sub.shape[:2]
    if sw > out_w:
        img.scale(out_w, max(1, int(sh * out_w / sw)))
        img.save()
    return img


def gen_textures():
    """加载既有贴图（原始 refs/ 素材目录已不存，贴图以 textures/ 下抢救版为准）。
    painting_4 按原裁剪比例从 photos/吧台的左半部分 重新裁。"""
    t = {}
    for key, fn in (("granite", "granite"), ("grid", "ceiling_grid"), ("blind", "zebra_blind"),
                    ("grille", "grille"), ("perf", "perforated"), ("wood", "wood_rubber"),
                    ("beans", "lightbox_beans"), ("logo", "logo_white"),
                    ("paint1", "painting_1"), ("paint2", "painting_2"), ("paint3", "painting_3")):
        t[key] = bpy.data.images.load(os.path.join(TEX, fn + ".png"))
    t["paint4"] = crop_image(os.path.join(PHOTOS, "吧台的左半部分（该图的左侧是门）.jpg"),
                             0.13, 0.46, 0.22, 0.56, "painting_4", 512)
    t["menu"] = bpy.data.images.load(os.path.join(ROOT, "menu.jpg"))
    return t


TX = gen_textures()

# ---------------- 材质 ----------------
MATS = {}


def mat(name, color=(0.8, 0.8, 0.8), rough=0.5, metal=0.0, img=None, tile=1.0,
        emit=None, emit_strength=0.0, emit_img=None, alpha=1.0, transmission=0.0):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if img is not None:
        tn = nt.nodes.new("ShaderNodeTexImage")
        tn.image = img
        nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
    if emit is not None:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1)
        bsdf.inputs["Emission Strength"].default_value = emit_strength
    if emit_img is not None:
        tn = nt.nodes.new("ShaderNodeTexImage")
        tn.image = emit_img
        nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
        nt.links.new(tn.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = emit_strength
    if alpha < 1.0:
        bsdf.inputs["Alpha"].default_value = alpha
        m.blend_method = 'BLEND' if hasattr(m, "blend_method") else None
        m.surface_render_method = 'BLENDED'
    if transmission > 0:
        bsdf.inputs["Transmission Weight"].default_value = transmission
    m["tile"] = tile
    m.diffuse_color = (*color, 1)
    MATS[name] = m
    return m


M_WOOD = mat("wood", img=TX["wood"], rough=0.45, tile=0.8)
M_WOODP = mat("wood_panel", img=TX["wood"], rough=0.6, tile=1.6)
M_FLOOR = mat("granite_floor", img=TX["granite"], rough=0.25, tile=0.8)
M_PORCH = mat("porch_paving", (0.44, 0.43, 0.41), 0.85, tile=0.6)
M_GRID = mat("ceiling_grid", img=TX["grid"], rough=0.8, tile=0.15)
M_WHITE = mat("white_paint", (0.86, 0.84, 0.79), 0.85)
M_CEIL = mat("white_ceiling", (0.90, 0.89, 0.86), 0.9)
M_FABRIC = mat("fabric_gray", (0.56, 0.53, 0.49), 0.95)
M_SEATBLUE = mat("leather_bluegray", (0.36, 0.45, 0.48), 0.45)
M_BRASS = mat("brass", (0.80, 0.52, 0.32), 0.3, 1.0)
M_ALU = mat("aluminium", (0.78, 0.78, 0.78), 0.35, 1.0)
M_STEEL = mat("stainless", (0.75, 0.75, 0.74), 0.25, 1.0)
M_CHROME = mat("chrome", (0.9, 0.9, 0.9), 0.08, 1.0)
M_GLASS = mat("window_glass", (0.02, 0.07, 0.05), 0.04, 0.0)
M_DOORGLASS = mat("door_glass", (0.6, 0.7, 0.75), 0.02, 0.0, alpha=0.25)
# 主入口是原木门（照片 入口门.jpg）：暖棕木框 + 上半磨砂玻璃 + 下部木裙板
M_DOORWOOD = mat("door_wood", (0.30, 0.15, 0.06), 0.42)
M_FROSTED = mat("frosted_glass", (0.80, 0.85, 0.81), 0.22, alpha=0.72)
M_EXIT = mat("exit_sign", (0.05, 0.45, 0.14), 0.5, emit=(0.10, 0.90, 0.30), emit_strength=3.0)
M_BLIND = mat("zebra_blind", img=TX["blind"], rough=0.9, tile=0.13)
M_GRILLE = mat("radiator_grille", img=TX["grille"], rough=0.5, tile=0.1)
M_PERF = mat("perforated_steel", img=TX["perf"], rough=0.3, metal=0.8, tile=0.025)
M_BLACK = mat("black_plastic", (0.02, 0.02, 0.02), 0.35)
M_WHITEM = mat("machine_white", (0.88, 0.87, 0.83), 0.3)
M_STRAW = mat("straw", (0.70, 0.54, 0.34), 0.95)
M_RIBBON = mat("hat_ribbon", (0.25, 0.17, 0.10), 0.7)
M_POT = mat("pot_white", (0.90, 0.90, 0.88), 0.5)
M_POTGRAY = mat("pot_gray", (0.55, 0.56, 0.55), 0.7)
M_SOIL = mat("soil", (0.10, 0.07, 0.05), 1.0)
M_LEAF = [mat("leaf_a", (0.06, 0.20, 0.07), 0.5), mat("leaf_b", (0.10, 0.26, 0.08), 0.5),
          mat("leaf_c", (0.05, 0.15, 0.08), 0.5)]
M_STEM = mat("stem", (0.22, 0.28, 0.12), 0.7)
M_TRUNK = mat("trunk", (0.30, 0.22, 0.14), 0.9)
M_BULB = mat("bulb", (1, 0.85, 0.6), 0.5, emit=(1.0, 0.72, 0.42), emit_strength=25)
M_DOWN = mat("downlight", (1, 1, 1), 0.5, emit=(1.0, 0.86, 0.68), emit_strength=30)
M_LOGO = mat("lightbox_logo", emit_img=TX["logo"], emit_strength=2.5, rough=0.4)
M_BEANS = mat("lightbox_beans", emit_img=TX["beans"], emit_strength=2.0, rough=0.4)
M_MENU = mat("menu", img=TX["menu"], rough=0.6)
M_PAINT = [mat("painting_%d" % i, img=TX["paint%d" % i], rough=0.7) for i in (1, 2, 3, 4)]
M_SOCKET = mat("socket", (0.42, 0.42, 0.43), 0.4)
M_LCD = mat("lcd_blue", (0.05, 0.2, 1.0), 0.2, emit=(0.15, 0.45, 1.0), emit_strength=6)
M_MACHINE_LIGHT = mat("machine_backlight", (0.9, 0.95, 1.0), 0.3, emit=(0.8, 0.9, 1.0), emit_strength=3)
M_CORK = mat("cork", (0.62, 0.45, 0.28), 0.9)
M_PAPER = mat("paper", (0.92, 0.91, 0.88), 0.9)
M_CUPGLASS = mat("cup_glass", (0.85, 0.9, 0.9), 0.05, alpha=0.35)
M_CERAMIC = mat("ceramic_white", (0.93, 0.92, 0.9), 0.2)
M_CERAMIC_B = mat("ceramic_blue", (0.15, 0.3, 0.55), 0.25)
M_DARKBOX = mat("dark_box", (0.08, 0.08, 0.08), 0.5)
M_ELEC = mat("electric_box", (0.78, 0.77, 0.72), 0.5)
M_CUSHION = mat("cushion_denim", (0.16, 0.25, 0.48), 0.9)
BOOK_COLS = [mat("book_%d" % i, c, 0.7) for i, c in enumerate([
    (0.75, 0.72, 0.65), (0.55, 0.15, 0.12), (0.15, 0.25, 0.4), (0.2, 0.35, 0.3), (0.85, 0.82, 0.75),
    (0.6, 0.5, 0.3), (0.1, 0.1, 0.12), (0.7, 0.4, 0.35), (0.45, 0.55, 0.6), (0.9, 0.88, 0.8)])]


# ---------------- 网格构建器 ----------------
def MX(loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    return Matrix.LocRotScale(Vector(loc), Euler(rot), Vector(scale))


class B:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.mats = []
        self.keep = self.bm.faces.layers.int.new("keepuv")
        self.uv = self.bm.loops.layers.uv.new("UVMap")

    def _mi(self, m):
        if m not in self.mats:
            self.mats.append(m)
        return self.mats.index(m)

    def _tag(self, verts, m, smooth=False):
        i = self._mi(m)
        for f in {f for v in verts for f in v.link_faces}:
            f.material_index = i
            f.smooth = smooth

    def box(self, size, loc, m, rot=(0, 0, 0)):
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=MX(loc, rot, size))
        self._tag(r["verts"], m)

    def boxm(self, size, matrix, m):
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=matrix @ Matrix.Diagonal((*size, 1)))
        self._tag(r["verts"], m)

    def cyl(self, r, h, loc, m, rot=(0, 0, 0), seg=24, r2=None, caps=True, smooth=True, scale=(1, 1, 1)):
        res = bmesh.ops.create_cone(self.bm, cap_ends=caps, cap_tris=False, segments=seg, radius1=r,
                                    radius2=(r if r2 is None else r2), depth=h, matrix=MX(loc, rot, scale))
        self._tag(res["verts"], m, smooth)

    def sphere(self, r, loc, m, scale=(1, 1, 1), seg=16):
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=seg, v_segments=max(4, seg // 2), radius=r,
                                        matrix=MX(loc, (0, 0, 0), scale))
        self._tag(res["verts"], m, True)

    def beam(self, p0, p1, w, m, d=None):
        p0, p1 = Vector(p0), Vector(p1)
        v = p1 - p0
        up = 'X' if abs(v.normalized().y) > 0.95 else 'Y'
        q = v.to_track_quat('Z', up)
        mm = Matrix.Translation((p0 + p1) / 2) @ q.to_matrix().to_4x4() @ Matrix.Diagonal((w, d or w, v.length, 1))
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=mm)
        self._tag(r["verts"], m)

    def prism(self, pts, thick, m, matrix=Matrix(), smooth=False):
        a = sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1]
                for i in range(len(pts)))
        if a < 0:
            pts = pts[::-1]
        bot = [self.bm.verts.new(matrix @ Vector((x, y, -thick / 2))) for x, y in pts]
        top = [self.bm.verts.new(matrix @ Vector((x, y, thick / 2))) for x, y in pts]
        i = self._mi(m)
        fs = [self.bm.faces.new(top), self.bm.faces.new(bot[::-1])]
        n = len(pts)
        for k in range(n):
            j = (k + 1) % n
            fs.append(self.bm.faces.new((bot[k], bot[j], top[j], top[k])))
        for f in fs:
            f.material_index = i
            f.smooth = smooth
        return fs

    def poly(self, pts3, m, flip=False):
        vs = [self.bm.verts.new(Vector(p)) for p in pts3]
        if flip:
            vs = vs[::-1]
        f = self.bm.faces.new(vs)
        f.material_index = self._mi(m)
        return f

    def img_plane(self, w, h, matrix, m):
        """XZ 平面上的贴图面 (法线 -Y)，UV 0..1"""
        co = [(-w / 2, 0, -h / 2), (w / 2, 0, -h / 2), (w / 2, 0, h / 2), (-w / 2, 0, h / 2)]
        uvs = [(0, 0), (1, 0), (1, 1), (0, 1)]
        vs = [self.bm.verts.new(matrix @ Vector(c)) for c in co]
        f = self.bm.faces.new(vs)
        f.material_index = self._mi(m)
        f[self.keep] = 1
        for l, uv in zip(f.loops, uvs):
            l[self.uv].uv = uv
        return f

    def arc(self, radius, a0, a1, z, h, w, m, n=10, center=(0, 0)):
        for k in range(n):
            t0 = a0 + (a1 - a0) * k / n
            t1 = a0 + (a1 - a0) * (k + 1) / n
            tm = (t0 + t1) / 2
            ch = 2 * radius * math.sin(abs(t1 - t0) / 2) * 1.06
            self.box((ch, w, h), (center[0] + radius * math.cos(tm), center[1] + radius * math.sin(tm), z), m,
                     (0, 0, tm + math.pi / 2))

    def band(self, r, a0, a1, w, m, n, ztop, zbot, center=(0, 0)):
        """沿圆弧的曲木板带：ztop/zbot 是角度 t 的函数（顶/底边高度可沿弧变化），板厚 w（径向）"""
        i = self._mi(m)
        vs = []
        for k in range(n + 1):
            t = a0 + (a1 - a0) * k / n
            cx, cy = center[0] + r * math.cos(t), center[1] + r * math.sin(t)
            nx, ny = math.cos(t), math.sin(t)
            zt, zb = ztop(t), zbot(t)
            vs.append([self.bm.verts.new((cx - nx * w / 2, cy - ny * w / 2, zb)),
                       self.bm.verts.new((cx - nx * w / 2, cy - ny * w / 2, zt)),
                       self.bm.verts.new((cx + nx * w / 2, cy + ny * w / 2, zt)),
                       self.bm.verts.new((cx + nx * w / 2, cy + ny * w / 2, zb))])
        faces = []
        for k in range(n):
            a, c = vs[k], vs[k + 1]
            faces += [self.bm.faces.new((a[0], c[0], c[1], a[1])),   # 内圈
                      self.bm.faces.new((a[3], a[2], c[2], c[3])),   # 外圈
                      self.bm.faces.new((a[1], c[1], c[2], a[2])),   # 顶边
                      self.bm.faces.new((a[0], a[3], c[3], c[0]))]   # 底边
        faces.append(self.bm.faces.new(vs[0]))
        faces.append(self.bm.faces.new(tuple(reversed(vs[n]))))
        for f in faces:
            f.material_index = i
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)

    def finish(self, coll, loc=(0, 0, 0), rotz=0.0, info=None, parent=None):
        bm = self.bm
        bm.normal_update()
        for f in bm.faces:
            if f[self.keep]:
                continue
            nrm = f.normal
            ax = max(range(3), key=lambda k: abs(nrm[k]))
            tile = float(self.mats[f.material_index].get("tile", 1.0)) if self.mats else 1.0
            for l in f.loops:
                c = l.vert.co
                if ax == 2:
                    u, v = c.x, c.y
                elif ax == 0:
                    u, v = c.y, c.z
                else:
                    u, v = c.x, c.z
                l[self.uv].uv = (u / tile, v / tile)
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        for mm in self.mats:
            me.materials.append(mm)
        if any(p.use_smooth for p in me.polygons):
            me.set_sharp_from_angle(angle=math.radians(40))
        ob = bpy.data.objects.new(self.name, me)
        ob.location = loc
        ob.rotation_euler.z = rotz
        coll.objects.link(ob)
        if parent:
            ob.parent = parent
        if info:
            ob["info_title"], ob["info_desc"] = info
        return ob


def face_to(src, dst):
    return math.atan2(dst[1] - src[1], dst[0] - src[0]) - math.pi / 2


# ---------------- 灯光 ----------------
WARM = (1.0, 0.78, 0.55)


def add_light(name, kind, loc, power, color=WARM, size=0.05, spot=math.radians(110), rot=(0, 0, 0)):
    ld = bpy.data.lights.new(name, kind)
    ld.energy = power
    ld.color = color
    if kind in ('POINT', 'SPOT'):
        ld.shadow_soft_size = size
    if kind == 'SPOT':
        ld.spot_size = spot
        ld.spot_blend = 0.6
    ob = bpy.data.objects.new(name, ld)
    ob.location = loc
    ob.rotation_euler = rot
    COLLS["Lighting"].objects.link(ob)
    return ob


# =====================================================================
#                              建筑结构
# =====================================================================
FP = [(160, 115), (610, 115), (610, 295), (920, 600), (1065, 600), (1065, 1180), (920, 1180),
      (920, 1350), (270, 1350), (270, 1175), (160, 1078), (160, 668), (160, 470)]
FPW = [P(*p) for p in FP]
area = sum(FPW[i][0] * FPW[(i + 1) % len(FPW)][1] - FPW[(i + 1) % len(FPW)][0] * FPW[i][1]
           for i in range(len(FPW)))
CCW = area > 0


class Seg:
    def __init__(self, a, b):
        self.a = Vector((*a, 0))
        self.b = Vector((*b, 0))
        self.L = (self.b - self.a).length
        self.u = (self.b - self.a) / self.L
        self.n = Vector((-self.u.y, self.u.x, 0)) if CCW else Vector((self.u.y, -self.u.x, 0))
        self.ang = math.atan2(self.u.y, self.u.x)

    def pt(self, t, d, z=0.0):
        p = self.a + self.u * t + self.n * d
        return (p.x, p.y, z)

    def box(self, b, t0, t1, d0, d1, z0, z1, m):
        b.box((t1 - t0, d1 - d0, z1 - z0), self.pt((t0 + t1) / 2, (d0 + d1) / 2, (z0 + z1) / 2), m,
              (0, 0, self.ang))


SEGS = [Seg(FPW[i], FPW[(i + 1) % len(FPW)]) for i in range(len(FPW))]
# 每段墙的类型
# 方案文档 12.2：「主入口位于右下侧门，左上双开门保持关闭」。
# 所以 seg5（右下）是 entrance，seg11（左上）是 door_locked —— 别再搞反了。
WALL_TYPE = ["window_ledge", "white", "window_counter", "white", "window_counter", "entrance",
             "white", "wood_back", "wood", "white", "white", "door_locked", "white"]

# 地面
b = B("Floor")
b.poly([(x, y, 0) for x, y in FPW], M_FLOOR, flip=not CCW)
b.finish(COLLS["Structure"])

# 格栅吊顶
b = B("Ceiling_Grid")
b.poly([(x, y, H) for x, y in FPW], M_GRID, flip=CCW)
b.finish(COLLS["Ceiling"])


def build_window_wall(b, s, counter, rng):
    sill = 1.26 if counter else 0.88
    s.box(b, -T / 2, s.L + T / 2, -T, 0, 0, sill, M_WOODP if not counter else M_WHITE)
    s.box(b, 0, s.L, -0.11, -0.09, sill, H, M_GLASS)
    n = max(1, round(s.L / 1.6))
    w = s.L / n
    for z in (sill, 2.55, H - 0.03):
        s.box(b, 0, s.L, -0.14, -0.06, z - 0.03, z + 0.03, M_ALU)
    for i in range(n + 1):
        s.box(b, i * w - 0.03, i * w + 0.03, -0.14, -0.06, sill, H, M_ALU)
        if i < n:
            tm = (i + 0.5) * w
            s.box(b, tm - 0.025, tm + 0.025, -0.13, -0.07, sill, 2.55, M_ALU)
    # 斑马帘
    for i in range(n):
        t0, t1 = i * w + 0.04, (i + 1) * w - 0.04
        bottom = rng.choice([2.45, 2.5, 2.6, 2.4, 1.7]) if counter else rng.choice([2.3, 2.5, 2.6])
        s.box(b, t0, t1, 0.07, 0.08, bottom, H - 0.12, M_BLIND)
        s.box(b, t0, t1, 0.06, 0.09, bottom - 0.04, bottom, M_WHITE)
        p = s.pt((t0 + t1) / 2, 0.08, H - 0.1)
        b.cyl(0.04, t1 - t0, p, M_WHITE, rot=(0, math.pi / 2, s.ang), seg=12)
    if counter:
        # 阳光区长桌: 桌面 / 挡板 / 前面板 / 暖气格栅 / 插座
        s.box(b, 0.02, s.L - 0.02, 0.0, 0.48, 1.0, 1.04, M_WOOD)
        s.box(b, 0.02, s.L - 0.02, 0.0, 0.025, 1.04, 1.26, M_WOOD)
        s.box(b, 0.02, s.L - 0.02, 0.40, 0.42, 0.0, 1.0, M_WOODP)
        s.box(b, 0.02, s.L - 0.02, 0.40, 0.43, 0.0, 0.06, M_BLACK)
        for i in range(n):
            tm = (i + 0.5) * w
            s.box(b, tm - 0.4, tm + 0.4, 0.42, 0.435, 0.3, 0.8, M_GRILLE)
            for k in (-0.35, 0.35):
                s.box(b, tm + k - 0.045, tm + k + 0.045, 0.025, 0.035, 1.1, 1.19, M_SOCKET)
    else:
        # 沉浸区: 窗台 + 暖气格栅
        s.box(b, 0, s.L, 0.0, 0.25, sill - 0.04, sill, M_WOOD)
        for i in range(n):
            tm = (i + 0.5) * w
            s.box(b, tm - 0.45, tm + 0.45, 0.0, 0.015, 0.15, 0.7, M_GRILLE)


# ---------------- 门 ----------------
DOOR_HEAD = 2.30        # 门洞净高
ENTRY_OPEN = 1.50       # 主入口门洞净宽
ENTRY_PANEL = 0.40      # 门旁那条固定窄亮子（照片 入口门.jpg 里门右手边那条）


def build_locked_door(b, s):
    """左上双开玻璃门 —— 方案里明确「保持关闭」，所以整体做成一件不可开的门。
    对象不拆扇，Unity 里按整体包围盒烘一个碰撞体正好把门洞堵死（本来就不该过人）。"""
    dz = 2.45
    s.box(b, 0, s.L, -0.11, -0.09, dz, H, M_DOORGLASS)                    # 上亮子
    s.box(b, 0, s.L, -0.13, -0.07, dz - 0.04, dz + 0.04, M_ALU)
    s.box(b, -0.04, 0.04, -0.13, -0.07, 0, H, M_ALU)
    s.box(b, s.L - 0.04, s.L + 0.04, -0.13, -0.07, 0, H, M_ALU)
    s.box(b, s.L / 2 - 0.03, s.L / 2 + 0.03, -0.13, -0.07, 0, dz, M_ALU)   # 两扇对缝处的中梃
    lw = s.L / 2
    for k in range(2):
        t0 = k * lw
        s.box(b, t0 + 0.05, t0 + lw - 0.05, -0.11, -0.09, 0.02, dz - 0.06, M_DOORGLASS)
        s.box(b, t0 + 0.05, t0 + lw - 0.05, -0.12, -0.08, 0.0, 0.08, M_ALU)          # 下横档
        s.box(b, t0 + 0.05, t0 + lw - 0.05, -0.12, -0.08, dz - 0.10, dz - 0.06, M_ALU)
        hx = t0 + (lw - 0.14 if k == 0 else 0.14)                                     # 竖木拉手
        for d in (0.0, -0.2):
            s.box(b, hx - 0.025, hx + 0.025, d - 0.05, d + 0.0, 0.5, 1.9, M_WOOD)


def build_entrance_frame(b, s):
    """主入口门框：木框 + 门头以上白墙 + 门洞两侧墙垛 + 安全出口灯箱。
    会开合的那一扇单独成对象（Door_entrance_Leaf），门洞精确范围由门槛条给。"""
    t0 = (s.L - ENTRY_OPEN) / 2
    t1 = t0 + ENTRY_OPEN
    s.box(b, 0, t0, -T, 0, 0, H, M_WHITE)                      # 东侧墙垛（正好收到墙段端点，
                                                               # 别再往外伸 T/2，否则会从东立面上戳出来）
    s.box(b, t1, s.L, -T, 0, 0, H, M_WHITE)                    # 西侧墙垛
    s.box(b, t0, t1, -T, 0, DOOR_HEAD, H, M_WHITE)             # 门头上方白墙
    # 木门框：两侧立框 + 上框，比墙面凸出 2cm（不然从店外看是"凹"的，白墙边缘会露一圈斜茬）
    s.box(b, t0 - 0.05, t0, -0.22, -0.05, 0, DOOR_HEAD, M_DOORWOOD)
    s.box(b, t1, t1 + 0.05, -0.22, -0.05, 0, DOOR_HEAD, M_DOORWOOD)
    s.box(b, t0, t1, -0.22, -0.05, DOOR_HEAD - 0.09, DOOR_HEAD, M_DOORWOOD)
    # 门上方的「安全出口」灯箱（照片里有）
    s.box(b, t0 + ENTRY_OPEN / 2 - 0.19, t0 + ENTRY_OPEN / 2 + 0.19,
          -0.26, -0.19, 2.52, 2.68, M_EXIT)


def build_entrance_threshold(s):
    """门槛条。除了挡门缝，它还是 Unity 侧唯一的「门洞精确范围」锚点：
    包围盒中心 = 门洞中心，较长的一条水平边 = 净宽。"""
    t0 = (s.L - ENTRY_OPEN) / 2
    b = B("Door_entrance_Threshold")
    b.box((ENTRY_OPEN, 0.22, 0.03), s.pt(t0 + ENTRY_OPEN / 2, -0.09, 0.015), M_STEEL,
          (0, 0, s.ang))
    return b.finish(COLLS["Structure"])


def build_wood_door_leaf(name, s, hinge_t, lw):
    """原木门扇（照片 入口门.jpg）：四边木梃 + 下部木裙板 + 大面磨砂玻璃 + 竖长拉手。
    对象原点放在铰链轴上，局部 +X 从铰链指向自由边，局部 Z 向上 —— Unity 里绕本地 Y 转即开合。"""
    b = B(name)
    h = DOOR_HEAD - 0.10        # 门扇高（上面留 1cm 给门头横框）
    th = 0.055                  # 门扇厚
    st = 0.10                   # 边梃 / 横档宽
    low = 0.80                  # 下部裙板顶面高度
    top = h - st - 0.02         # 玻璃顶
    # 左右边梃
    for x in (st / 2, lw - st / 2):
        b.box((st, th, h - 0.02), (x, 0, (0.02 + h) / 2), M_DOORWOOD)
    # 上下横档
    b.box((lw - 2 * st, th, st), (lw / 2, 0, h - 0.02 - st / 2), M_DOORWOOD)
    b.box((lw - 2 * st, th, st), (lw / 2, 0, 0.02 + st / 2), M_DOORWOOD)
    # 下部木裙板 + 中横档
    b.box((lw - 2 * st, th, low - 0.02 - st), (lw / 2, 0, (0.02 + st + low) / 2), M_DOORWOOD)
    b.box((lw - 2 * st, th, 0.09), (lw / 2, 0, low), M_DOORWOOD)
    # 磨砂玻璃
    b.box((lw - 2 * st - 0.02, 0.012, top - low - 0.055),
          (lw / 2, 0, (low + top) / 2), M_FROSTED)
    # 竖长拉手（内外各一根）
    for y in (th / 2 + 0.045, -th / 2 - 0.045):
        b.box((0.035, 0.05, 0.34), (lw - 0.16, y, 1.02), M_STEEL)
    return b.finish(COLLS["Structure"], loc=s.pt(hinge_t, -0.10, 0), rotz=s.ang)


def build_entrance_panel(s, t_center, w):
    """门旁那条固定窄玻璃，木框包边。"""
    b = B("Door_entrance_Panel")
    h = DOOR_HEAD - 0.02
    b.box((w - 0.05, 0.022, h - 0.14), (0, 0, h / 2), M_FROSTED)
    for x in (-(w / 2 - 0.025), (w / 2 - 0.025)):
        b.box((0.05, 0.055, h), (x, 0, h / 2), M_DOORWOOD)
    b.box((w - 0.05, 0.055, 0.06), (0, 0, h - 0.03), M_DOORWOOD)
    b.box((w - 0.05, 0.055, 0.07), (0, 0, 0.035), M_DOORWOOD)
    return b.finish(COLLS["Structure"], loc=s.pt(t_center, -0.10, 0), rotz=s.ang)


for s, kind in zip(SEGS, WALL_TYPE):
    rng = random.Random(int(s.L * 100))
    if kind.startswith("window"):
        b = B("Wall_Window_%d" % SEGS.index(s))
        build_window_wall(b, s, kind == "window_counter", rng)
        ob = b.finish(COLLS["Structure"])
        if kind == "window_counter":
            ob["info_title"] = "阳光区长桌"
            ob["info_desc"] = "沿窗的长条吧桌，每隔一段就有插座，是店里最受欢迎的自习位。白天阳光透过斑马帘洒进来。"
        else:
            ob["info_title"] = "沉浸区窗边"
            ob["info_desc"] = "沙发区背靠大窗，窗台上摆着老板养的小绿植。"
    elif kind == "entrance":
        b = B("Door_entrance")
        build_entrance_frame(b, s)
        ob = b.finish(COLLS["Structure"])
        ob["info_title"] = "入口"
        ob["info_desc"] = "原木门配竖长拉手，门头上挂着「安全出口」的小灯箱。"
        build_entrance_threshold(s)
        t0 = (s.L - ENTRY_OPEN) / 2
        # 铰链在最东侧（平面图上的门弧就画在这一侧），固定亮子在西侧
        build_wood_door_leaf("Door_entrance_Leaf", s, t0, ENTRY_OPEN - ENTRY_PANEL)
        build_entrance_panel(s, t0 + ENTRY_OPEN - ENTRY_PANEL / 2, ENTRY_PANEL)
    elif kind == "door_locked":
        b = B("Door_locked")
        build_locked_door(b, s)
        ob = b.finish(COLLS["Structure"])
        ob["info_title"] = "双开玻璃门"
        ob["info_desc"] = "方案里这扇门一直关着，只用来看风景和采光。"
    else:
        b = B("Wall_%d" % SEGS.index(s))
        m = M_WOODP if kind.startswith("wood") else M_WHITE
        s.box(b, -T / 2, s.L + T / 2, -T, 0, 0, H, m)
        b.finish(COLLS["Structure"])

# ---- 门外入口铺装（03 票：只做一块地台，不做外立面）----
# 入口在右下角的凹口里，宽度正好等于那段墙，往南伸到与店内南墙齐平再多一点，
# 给"站在门外看店"留出站立空间。
PORCH = SEGS[WALL_TYPE.index("entrance")]
PORCH_DEPTH = 3.00
b = B("Porch")
b.box((PORCH.L, PORCH_DEPTH, 0.15),
      PORCH.pt(PORCH.L / 2, -PORCH_DEPTH / 2, -0.075),
      M_PORCH, (0, 0, PORCH.ang))
b.finish(COLLS["Structure"], info=("门外", "店门口的一小块铺装地台。"))

# ---- 白色跌级吊顶 + 筒灯 ----
b = B("Ceiling_Soffits")
SOFFITS = [  # x0,x1,y0,y1（按原比例给出，随 SCALE 一起放大）
    (1.8, 3.0, 1.95, 12.6),
    (5.3, 6.5, 1.95, 9.0),
    (0.1, 7.0, 7.9, 8.9),
]
SOFFITS = [tuple(v * SCALE for v in s) for s in SOFFITS]
DOWN = []
for x0, x1, y0, y1 in SOFFITS:
    b.box((x1 - x0, y1 - y0, 0.55), ((x0 + x1) / 2, (y0 + y1) / 2, H - 0.275), M_CEIL)
for y in np.arange(2.9 * SCALE, 12.6 * SCALE, 1.9 * SCALE):
    DOWN.append((2.4 * SCALE, y))
for y in np.arange(2.9 * SCALE, 9.0 * SCALE, 1.9 * SCALE):
    DOWN.append((5.9 * SCALE, y))
for x in (0.9 * SCALE, 4.2 * SCALE):
    DOWN.append((x, 8.4 * SCALE))
for x, y in DOWN:
    b.cyl(0.065, 0.01, (x, y, H - 0.555), M_DOWN, seg=16)
b.finish(COLLS["Ceiling"])
for i, (x, y) in enumerate(DOWN):
    add_light("Downlight_%02d" % i, 'SPOT', (x, y, H - 0.58), 90, size=0.06)

# ---- 吧台区: 低吊顶 + 木饰面门头 ----
bx0, bx1 = P(270, 0)[0], P(920, 0)[0]
by1 = P(0, 1180)[1] + BAR_SHIFT + 0.16   # 低吊顶随前柜北移，并盖住台面前沿
BARDL_Y = by1 - 0.62                     # 吧台筒灯落在柜台正上方
b = B("Bar_Ceiling")
b.box((bx1 - bx0, by1 - 0.1, 0.05), ((bx0 + bx1) / 2, (by1 + 0.1) / 2, 3.2), M_CEIL)
b.box((bx1 - bx0, 0.08, H - 3.2), ((bx0 + bx1) / 2, by1 - 0.04, (H + 3.2) / 2), M_WOODP)
for x in np.arange(bx0 + 0.9, bx1 - 0.5, 1.6):
    b.cyl(0.06, 0.01, (x, BARDL_Y, 3.17), M_DOWN, seg=16)
b.finish(COLLS["Ceiling"])
for i, x in enumerate(np.arange(bx0 + 0.9, bx1 - 0.5, 1.6)):
    add_light("BarDownlight_%d" % i, 'SPOT', (x, BARDL_Y, 3.15), 70, size=0.06)

# =====================================================================
#                              家具
# =====================================================================


def loveseat(name, loc, rot, seats=2, info=None):
    b = B(name)
    W = 0.62 * seats + 0.1
    hw = W / 2
    b.box((W - 0.12, 0.56, 0.13), (0, 0.04, 0.37), M_FABRIC)
    b.box((W - 0.12, 0.13, 0.46), (0, -0.25, 0.66), M_FABRIC, (math.radians(-10), 0, 0))
    b.box((W - 0.12, 0.6, 0.04), (0, 0.0, 0.28), M_WOOD)
    for sx in (-1, 1):
        x = sx * (hw - 0.03)
        b.beam((x, 0.33, 0), (x, 0.28, 0.64), 0.045, M_WOOD)
        b.beam((x, -0.32, 0), (x, -0.30, 0.64), 0.045, M_WOOD)
        b.box((0.06, 0.74, 0.035), (x, 0.0, 0.645), M_WOOD)
        b.box((0.035, 0.62, 0.05), (x, 0.0, 0.25), M_WOOD)
    b.box((W - 0.06, 0.035, 0.06), (0, 0.31, 0.25), M_WOOD)
    # 蓝色"钱多多"坐垫(照片中的牛仔布坐垫)
    for k in range(seats):
        if random.random() < 0.7:
            x = (k - (seats - 1) / 2) * 0.6
            b.box((0.42, 0.40, 0.012), (x, 0.06, 0.44), M_CUSHION, (0, 0, random.uniform(-0.2, 0.2)))
    return b.finish(COLLS["Furniture"], loc, rot, info)


def oval_table(name, loc, rot, L=1.15, W=0.58, info=None):
    b = B(name)
    r = W / 2
    pts = []
    for k in range(13):
        a = -math.pi / 2 + math.pi * k / 12
        pts.append((L / 2 - r + r * math.cos(a), r * math.sin(a)))
    for k in range(13):
        a = math.pi / 2 + math.pi * k / 12
        pts.append((-L / 2 + r + r * math.cos(a), r * math.sin(a)))
    b.prism(pts, 0.03, M_WOOD, MX((0, 0, 0.735)))
    for sx in (-1, 1):
        x = sx * (L / 2 - 0.3)
        b.beam((x, 0, 0.72), (x + sx * 0.05, 0.2, 0.02), 0.04, M_WOOD)
        b.beam((x, 0, 0.72), (x + sx * 0.05, -0.2, 0.02), 0.04, M_WOOD)
        b.box((0.04, 0.42, 0.03), (x + sx * 0.05, 0, 0.015), M_WOOD)
    b.beam((-L / 2 + 0.3, 0, 0.3), (L / 2 - 0.3, 0, 0.3), 0.03, M_WOOD)
    return b.finish(COLLS["Furniture"], loc, rot, info)


def round_table(name, loc, info=None, r=0.35):
    b = B(name)
    b.cyl(r, 0.03, (0, 0, 0.735), M_WOOD, seg=40)
    feet = []
    for k in range(3):
        a = k * 2 * math.pi / 3 + 0.3
        top = (0.1 * math.cos(a), 0.1 * math.sin(a), 0.72)
        bot = (0.28 * math.cos(a), 0.28 * math.sin(a), 0.02)
        b.beam(top, bot, 0.038, M_WOOD)
        feet.append(bot)
    for k in range(3):
        p, q = feet[k], feet[(k + 1) % 3]
        b.beam((p[0], p[1], 0.015), (q[0], q[1], 0.015), 0.03, M_WOOD)
    return b.finish(COLLS["Furniture"], loc, 0, info)


def rect_table(name, loc, rot, L=1.15, W=0.68, h=0.74, info=None):
    b = B(name)
    b.box((L, W, 0.035), (0, 0, h - 0.0175), M_WOOD)
    b.box((L - 0.12, W - 0.12, 0.06), (0, 0, h - 0.065), M_WOOD)
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.beam((sx * (L / 2 - 0.07), sy * (W / 2 - 0.07), h - 0.04),
                   (sx * (L / 2 - 0.05), sy * (W / 2 - 0.05), 0), 0.045, M_WOOD)
    return b.finish(COLLS["Furniture"], loc, rot, info)


def conference_table(name, loc, rot, info=None):
    L, W, h = 1.8, 0.95, 0.76
    b = B(name)
    b.box((L, W, 0.05), (0, 0, h - 0.025), M_WOOD)
    for sx in (-1, 1):
        b.box((0.06, W - 0.15, h - 0.05), (sx * (L / 2 - 0.2), 0, (h - 0.05) / 2), M_WOOD)
    b.box((L - 0.4, 0.03, 0.35), (0, 0, h - 0.25), M_WOOD)
    # 桌上插线板/书
    b.box((0.3, 0.06, 0.04), (-0.3, 0.1, h + 0.02), M_CERAMIC)
    b.box((0.26, 0.2, 0.08), (0.4, -0.15, h + 0.04), BOOK_COLS[2])
    return b.finish(COLLS["Furniture"], loc, rot, info)


def mushroom_chair(name, loc, rot, bar=False, info=None, coll="Furniture"):
    """蘑菇背实木椅 / 吧凳 (照片6、15)，本地坐标正面朝 +Y"""
    b = B(name)
    sh = 0.66 if bar else 0.45
    hw, hd = 0.2, 0.19
    for sx in (-1, 1):
        b.beam((sx * hw, hd, 0), (sx * (hw - 0.01), hd - 0.01, sh - 0.04), 0.04, M_WOOD)
        b.beam((sx * hw, -hd - 0.03, 0), (sx * (hw - 0.01), -hd, sh + 0.33), 0.04, M_WOOD)
    b.box((0.42, 0.40, 0.05), (0, 0, sh - 0.045), M_WOOD)
    b.box((0.42, 0.40, 0.045), (0, 0.0, sh), M_SEATBLUE if bar else M_FABRIC)
    # 弧形靠背：取圆心 (0,0.7) 的下半圆（256°→284°），靠背落在座面后侧 y≈-0.2、两端略向前包
    # （旧值 76°→104° 取成上半圆，靠背飘到 y≈1.6 —— 错位 bug）
    b.arc(0.9, math.radians(256), math.radians(284), sh + 0.27, 0.15, 0.03, M_WOOD, n=6, center=(0, 0.7))
    # 横档
    zs = (0.25, 0.25) if bar else (0.14, 0.14)
    for sy in (-1, 1):
        b.box((2 * hw, 0.03, 0.03), (0, sy * hd, zs[0]), M_WOOD)
    for sx in (-1, 1):
        b.box((0.03, 2 * hd, 0.03), (sx * hw, 0, zs[1] + 0.06), M_WOOD)
    return b.finish(COLLS[coll], loc, rot, info)


def rrect_pts(w, d, r, n=3):
    """圆角矩形轮廓点（逆时针一圈），给 prism 做坐板 / 坐垫"""
    pts = []
    for sx, sy in ((1, 1), (-1, 1), (-1, -1), (1, -1)):
        ccx, ccy = sx * (w / 2 - r), sy * (d / 2 - r)
        base = math.atan2(sy, sx) - math.pi / 4
        for k in range(n + 1):
            a = base + (math.pi / 2) * k / n
            pts.append((ccx + r * math.cos(a), ccy + r * math.sin(a)))
    return pts


def armchair(name, loc, rot, info=None):
    """弧形靠背软包木椅（店内统一样式，见 photos/矮凳子.jpg），本地坐标正面朝 +Y

    旧版是曲木桶形围板＋平板扶手，结构过度复杂且与实物不符。现按店里实际那把
    简式靠背椅重建，只保证"是正常能坐的椅子"：
      ① 四条方形腿，下端略外撇；后腿越过座面向上延伸成靠背立柱；
      ② 座框：圆角座板 + 四周望板；
      ③ 圆角布艺坐垫；
      ④ 一块微弯的曲木靠背板，两端搭在后腿顶端，与坐面留 6cm 空隙；
      ⑤ 四边横档加固。
    """
    b = B(name)
    SH = 0.45                     # 坐垫面高
    ZB = 0.78                     # 靠背板顶边
    hw, hd = 0.20, 0.19           # 半宽 / 半深
    # ① 四腿（下端外撇 1.2cm），后腿延伸为靠背立柱
    for sx in (-1, 1):
        b.beam((sx * (hw + 0.012), hd + 0.008, 0.0), (sx * (hw - 0.008), hd - 0.008, SH - 0.05), 0.042, M_WOOD)
        b.beam((sx * (hw + 0.012), -hd - 0.022, 0.0), (sx * (hw - 0.008), -hd, ZB), 0.042, M_WOOD)
    # ② 座框（圆角座板 + 望板）
    b.prism(rrect_pts(0.42, 0.40, 0.05), 0.03, M_WOOD, MX((0, 0, SH - 0.075)))
    for sy in (-1, 1):
        b.box((2 * hw, 0.022, 0.05), (0, sy * (hd - 0.004), SH - 0.115), M_WOOD)
    for sx in (-1, 1):
        b.box((0.022, 2 * hd - 0.012, 0.05), (sx * (hw - 0.004), 0, SH - 0.115), M_WOOD)
    # ③ 布艺坐垫
    b.prism(rrect_pts(0.40, 0.38, 0.05), 0.055, M_FABRIC, MX((0, 0, SH - 0.0275)))
    # ④ 微弯靠背板（大半径近直圆弧，两端正好落在后腿外侧，中心在 y≈-0.205）
    b.arc(0.9, math.radians(256), math.radians(284), ZB - 0.085, 0.17, 0.028, M_WOOD, n=12, center=(0, 0.695))
    # ⑤ 横档
    for sy in (-1, 1):
        b.box((2 * hw - 0.02, 0.026, 0.026), (0, sy * hd, 0.16), M_WOOD)
    for sx in (-1, 1):
        b.box((0.026, 2 * hd - 0.02, 0.026), (sx * hw, 0, 0.16), M_WOOD)
    return b.finish(COLLS["Furniture"], loc, rot, info)


def books_in_cubby(b, x0, x1, y_back, depth, z0, z1, rng, facing=1):
    """在 [x0,x1] 宽、z0..z1 高的格子里放书 (本地坐标，书背朝 +Y*facing)"""
    roll = rng.random()
    cw = x1 - x0
    ch = z1 - z0
    if roll < 0.62:
        x = x0 + 0.01
        stop = x0 + cw * rng.uniform(0.45, 0.95)
        while x < stop:
            bw = rng.uniform(0.018, 0.045)
            bh = min(ch - 0.03, rng.uniform(0.17, 0.3))
            bd = rng.uniform(0.15, 0.21)
            yc = y_back + facing * bd / 2
            b.box((bw, bd, bh), (x + bw / 2, yc, z0 + bh / 2), rng.choice(BOOK_COLS))
            x += bw + 0.002
        if rng.random() < 0.3 and x < x1 - 0.12:
            b.box((0.18, 0.22, 0.04), (x1 - 0.1, y_back + facing * 0.11, z0 + 0.02), rng.choice(BOOK_COLS))
    elif roll < 0.85:
        n = rng.randint(1, 3)
        for k in range(n):
            xc = x0 + cw * (k + 1) / (n + 1)
            if rng.random() < 0.5:
                b.cyl(0.035, 0.08, (xc, y_back + facing * 0.12, z0 + 0.04), M_CERAMIC, seg=12)
            else:
                b.cyl(0.03, 0.16, (xc, y_back + facing * 0.12, z0 + 0.08), M_CERAMIC_B, seg=12, r2=0.015)
    # 其余留空


def bookshelf_wall(name, loc, rot, L, Hs=2.35, info=None):
    """书墙 (照片6)，沿本地 X 展开，开口朝 +Y，背板在 y=0"""
    b = B(name)
    D = 0.32
    rng = random.Random(11)
    t = 0.022
    b.box((L, 0.015, Hs), (0, 0.0075, Hs / 2), M_WOODP)
    rows = [0.10, 0.48, 0.86, 1.22, 1.58, 1.96, Hs]
    for z in rows[:-1] + [Hs - t]:
        b.box((L, D, t), (0, D / 2, z + t / 2), M_WOOD)
    b.box((L, 0.02, 0.1), (0, D - 0.01, 0.05), M_WOOD)
    nc = max(2, round(L / 0.36))
    cw = L / nc
    for i in range(nc + 1):
        x = -L / 2 + i * cw
        b.box((t, D, Hs), (x, D / 2, Hs / 2), M_WOOD)
    for r in range(len(rows) - 1):
        z0 = rows[r] + t
        z1 = rows[r + 1]
        for i in range(nc):
            x0 = -L / 2 + i * cw + t / 2
            books_in_cubby(b, x0, x0 + cw - t, 0.02, D, z0, z1, rng, 1)
    return b.finish(COLLS["Furniture"], loc, rot, info)


def divider_shelf(name, loc, rot, L, Hs=2.1, info=None):
    """沉浸区隔断书架 (照片8)，双面开放格子"""
    b = B(name)
    D = 0.34
    t = 0.022
    rng = random.Random(5)
    rows = [0.08, 0.42, 0.78, 1.12, 1.44, 1.78, Hs]
    for z in rows[:-1] + [Hs - t]:
        b.box((L, D, t), (0, 0, z + t / 2), M_WOOD)
    b.box((L, D - 0.04, 0.08), (0, 0, 0.04), M_WOOD)
    for sx in (-1, 1):
        b.box((t * 1.5, D, Hs), (sx * (L / 2 - t * 0.75), 0, Hs / 2), M_WOOD)
    for r in range(len(rows) - 1):
        nc = [3, 4, 3, 4, 3, 4][r]
        cw = L / nc
        z0, z1 = rows[r] + t, rows[r + 1]
        for i in range(1, nc):
            b.box((t, D, z1 - z0), (-L / 2 + i * cw, 0, (z0 + z1) / 2), M_WOOD)
        for i in range(nc):
            x0 = -L / 2 + i * cw + t
            books_in_cubby(b, x0, x0 + cw - 2 * t, -D / 2 + 0.01, D, z0, z1, rng, 1)
    return b.finish(COLLS["Furniture"], loc, rot, info)


def straw_hat(b, loc, s=1.0, tilt=(0, 0)):
    m = MX(loc, (tilt[0], tilt[1], 0), (s, s, s))
    b.cyl(0.18, 0.012, m @ Vector((0, 0, 0.006)), M_STRAW, seg=32, rot=(tilt[0], tilt[1], 0), scale=(s, s * 0.92, s))
    b.cyl(0.1, 0.11, m @ Vector((0, 0, 0.06)), M_STRAW, seg=24, r2=0.085, rot=(tilt[0], tilt[1], 0), scale=(s, s * 0.85, s))
    b.cyl(0.101, 0.025, m @ Vector((0, 0, 0.025)), M_RIBBON, seg=24, rot=(tilt[0], tilt[1], 0), scale=(s, s * 0.85, s))


# =====================================================================
#                         按平面图摆放家具
# =====================================================================
def zone_info(t, d):
    return (t, d)


SOFA_OFF = 0.70
# --- 沉浸区: 1、2 号沙发桌 ---
for no, (px, py) in ((1, (292, 192)), (2, (520, 192))):
    c = P(px, py)
    oval_table("Immersive_Table_%d" % no, (*c, 0), math.pi / 2,
               info=("沉浸区 %d 号桌" % no, "椭圆原木桌 + 两张布艺双人沙发，靠窗安静，适合看书、聊天。"))
    for sx in (-1, 1):
        sp = (c[0] + sx * SOFA_OFF, c[1])
        loveseat("Immersive_Sofa_%d_%s" % (no, "L" if sx < 0 else "R"), (*sp, 0), face_to(sp, c),
                 info=("沉浸区 %d 号桌 · 沙发" % no, "原木框架布艺沙发。"))
# --- 沉浸区: 3、4 号方桌 ---
for no, (px, py) in ((3, (227, 341)), (4, (377, 341))):
    c = P(px, py)
    rect_table("Immersive_Table_%d" % no, (*c, 0), 0,
               info=("沉浸区 %d 号桌" % no, "四人原木方桌，配蘑菇背实木椅。"))
    for k, (dx, dy) in enumerate(((-0.3, 0.5), (0.3, 0.5), (-0.3, -0.5), (0.3, -0.5))):
        sp = (c[0] + dx, c[1] + dy)
        mushroom_chair("Immersive_Chair_%d_%d" % (no, k), (*sp, 0), face_to(sp, (sp[0], c[1])))
# --- 隔断书架 ---
x0, y = P(170, 445)
x1, _ = P(452, 445)
divider_shelf("Divider_Bookshelf", ((x0 + x1) / 2, y, 0), 0, x1 - x0,
              info=("隔断书架", "把沉浸区和入口隔开的开放格书架，摆着花瓶、茶具、杂志和绿萝。"))
# --- 书墙 (左墙) ---
_, y0 = P(0, 1075)
_, y1 = P(0, 672)
bookshelf_wall("Bookshelf_Wall", (P(160, 0)[0], (y0 + y1) / 2, 0), -math.pi / 2, y1 - y0,
               info=("书墙", "整面原木书墙，书可以随意取阅；格子里还藏着老板收藏的相机、手办、杯子和书法小品。"))
# 书墙上方的画
for k, m in enumerate(M_PAINT[:3]):
    b = B("Painting_%d" % k)
    yc = y0 + (y1 - y0) * (k + 0.5) / 3
    b.box((0.03, 0.62, 0.48), (P(160, 0)[0] + 0.02, yc, 2.75), M_WOOD)
    b.img_plane(0.58, 0.44, MX((P(160, 0)[0] + 0.04, yc, 2.75), (0, 0, math.pi / 2)), m)
    b.finish(COLLS["Decor"], info=("墙上的油画", "店里挂着的风景油画。"))
# --- 多人桌 1 ---（较平面图北移 ~0.9m：会议桌北上后保持 0.3m 间隙）
c = P(366, 650)
oval_table("Group_Table_1", (*c, 0), math.pi / 2, info=("多人桌 1", "椭圆桌配两张双人沙发，可坐 4 人。"))
for sx in (-1, 1):
    sp = (c[0] + sx * SOFA_OFF, c[1])
    loveseat("Group_Sofa_1_%d" % sx, (*sp, 0), face_to(sp, c))
# --- 会议桌 ---（较平面图北移 ~1.1m：给圆桌 1 让位；与多人桌 1 留 0.3m）
c = P(366, 811)
conference_table("Meeting_Table", (*c, 0), math.pi / 2,
                 info=("会议桌", "1.8 米原木长桌，可坐 6 人，适合小组讨论或自习。"))
for k, py in enumerate((866, 811, 756)):
    for sx, px in ((-1, 287), (1, 445)):
        sp = P(px, py)
        armchair("Meeting_Chair_%d_%d" % (k, sx), (*sp, 0), face_to(sp, (c[0], sp[1])))
# --- 圆桌 1 ---（前柜北移后，这一组原位置会压进吧台；南凳留 0.3m 到吧凳）
c = P(366, 988)
round_table("Round_Table_1", (*c, 0), info=("圆桌 1", "三脚原木小圆桌，配弧形靠背软包小椅。"))
for k in range(4):
    a = math.pi / 4 + k * math.pi / 2
    sp = (c[0] + 0.63 * math.cos(a), c[1] + 0.63 * math.sin(a))
    armchair("Round1_Chair_%d" % k, (*sp, 0), face_to(sp, c))
# --- 多人桌 2、3 ---（较平面图西移 0.22m：与圆桌组留 1.0m 过道）
for no, (px, py) in ((2, (588, 662)), (3, (588, 899))):
    c = P(px, py)
    oval_table("Group_Table_%d" % no, (*c, 0), 0, info=("多人桌 %d" % no, "椭圆桌配两张双人沙发，可坐 4 人。"))
    for sy in (-1, 1):
        sp = (c[0], c[1] + sy * SOFA_OFF)
        loveseat("Group_Sofa_%d_%d" % (no, sy), (*sp, 0), face_to(sp, c))
# --- 圆桌 2/3/4 ---（较平面图西移 0.4m：让出阳光区斜窗吧凳；台距沿用平面图 1.56m）
for no, py in ((2, 670), (3, 792), (4, 915)):
    c = P(793, py)
    round_table("Round_Table_%d" % no, (*c, 0), info=("圆桌 %d" % no, "三脚原木小圆桌，配弧形靠背软包小椅。"))
    seats = [(-0.63, 0), (0.63, 0)]
    if no == 2:
        seats.append((0, 0.63))
    if no == 4:
        seats.append((0, -0.63))
    for k, (dx, dy) in enumerate(seats):
        sp = (c[0] + dx, c[1] + dy)
        armchair("Round%d_Chair_%d" % (no, k), (*sp, 0), face_to(sp, c))
# --- 阳光区吧凳: 斜窗 1-6 ---
sd = SEGS[2]
for k in range(6):
    t = sd.L * (k + 0.5) / 6
    p = sd.pt(t, 0.78, 0)
    mushroom_chair("Sun_Stool_D%d" % (k + 1), p, sd.ang - math.pi, bar=True,
                   info=("阳光区 %d 号座" % (k + 1), "斜窗长桌座位，高脚蘑菇背吧凳，灰蓝色皮质坐垫。"))
# --- 阳光区吧凳: 东窗 7-12 ---
for k, py in enumerate((683, 741, 800, 858, 917, 975)):
    p = P(978, py)
    mushroom_chair("Sun_Stool_E%d" % (k + 7), (*p, 0), -math.pi / 2, bar=True,
                   info=("阳光区 %d 号座" % (k + 7), "东窗长桌座位，桌下是暖气，冬天很暖和。"))

# =====================================================================
#                              吧台区
# =====================================================================
xa, ya = P(278, 1255)
xb, yb = P(826, 1180)
ya += BAR_SHIFT          # 前柜北移（06 票）：后柜前沿 0.74 → 通道 1.21m
yb += BAR_SHIFT
BAR_L = xb - xa
BAR_C = ((xa + xb) / 2, (ya + yb) / 2)
b = B("Bar_Counter")
D = yb - ya
Hc = 1.05
# 台面 + 侧板(梯形) + 开放层板(面向顾客)
b.box((BAR_L, D + 0.1, 0.04), (0, 0.05, Hc - 0.02), M_WOOD)
for sx in (-1, 1):
    b.prism([(-D / 2, 0), (D / 2 + 0.05, 0), (D / 2 + 0.05 - 0.12, Hc - 0.04), (-D / 2, Hc - 0.04)], 0.04,
            M_WOOD, MX((sx * (BAR_L / 2 - 0.02), 0, 0), (math.pi / 2, 0, math.pi / 2)) @ MX(rot=(0, 0, 0)))
for z in (0.05, 0.5):
    b.box((BAR_L - 0.06, D - 0.05, 0.03), (0, 0.0, z), M_WOOD)
b.box((BAR_L - 0.06, 0.03, Hc - 0.05), (0, -D / 2 + 0.02, (Hc - 0.05) / 2), M_WOODP)
for i in range(1, 5):
    b.box((0.03, D - 0.05, 0.95), (-BAR_L / 2 + i * BAR_L / 5, 0, 0.5), M_WOOD)
# 顾客侧杂物: 收纳筐、纸袋
rng = random.Random(3)
for i in range(5):
    x = -BAR_L / 2 + (i + 0.5) * BAR_L / 5
    b.box((0.38, 0.3, 0.22), (x - 0.15, 0.1, 0.18), M_TRUNK)
    b.box((0.25, 0.08, 0.3), (x + 0.2, 0.15, 0.66), rng.choice(BOOK_COLS))
# 台面上: 展示柜(左)、菜单、收银、甜品柜(右)
b.box((0.9, 0.4, 0.04), (BAR_L / 2 - 0.6, 0.0, Hc + 0.02), M_WOOD)
for sx in (-1, 1):
    b.beam((BAR_L / 2 - 0.6 + sx * 0.43, -0.15, Hc), (BAR_L / 2 - 0.6 + sx * 0.36, -0.15, Hc + 0.45), 0.03, M_WOOD)
b.box((0.9, 0.4, 0.03), (BAR_L / 2 - 0.6, 0.0, Hc + 0.46), M_WOOD)
b.box((0.4, 0.3, 0.25), (BAR_L / 2 - 1.5, 0.0, Hc + 0.125), M_CUPGLASS)
b.box((0.75, 0.5, 0.55), (-BAR_L / 2 + 0.55, -0.05, Hc + 0.275), M_CUPGLASS)
b.box((0.75, 0.5, 0.04), (-BAR_L / 2 + 0.55, -0.05, Hc + 0.02), M_STEEL)
for z in (0.2, 0.38):
    b.box((0.72, 0.46, 0.01), (-BAR_L / 2 + 0.55, -0.05, Hc + z), M_CUPGLASS)
# 菜单立牌
b.box((0.24, 0.02, 0.34), (P(500, 0)[0] - BAR_C[0], D / 2 - 0.05, Hc + 0.17), M_TRUNK, (math.radians(-12), 0, 0))
b.img_plane(0.21, 0.28, MX((P(500, 0)[0] - BAR_C[0], D / 2 - 0.035, Hc + 0.18), (math.radians(-12), 0, math.pi)), M_MENU)
ob_bar = b.finish(COLLS["Bar"], (*BAR_C, 0), 0,
                  info=("吧台 · 点单位", "在这里点单。招牌：哥伦比亚拼配美式 ¥10、经典拿铁 ¥13、生椰拿铁 ¥15、桂花拿铁 ¥16；"
                                        "还有东方茗茶、果花茶和无咖啡饮品。"))

# --- 收银机已移除：店里实际没有收银机，点单就在吧台口头完成 ---

# 吧台凳 1-3（随前柜一起北移）
for k, px in enumerate((518, 615, 711)):
    p = (P(px, 1150)[0], P(px, 1150)[1] + BAR_SHIFT)
    mushroom_chair("Bar_Stool_%d" % (k + 1), (*p, 0), math.pi, bar=True, coll="Bar",
                   info=("吧台 %d 号座" % (k + 1), "坐在这里可以看咖啡师做咖啡。"))

# --- 原「打印机」已移除：店里实际没有这台黑色设备，不再虚构摆放 ---

# --- 后操作台 (靠后墙) ---
BACK_Y = 0.1 + 0.33
b = B("Back_Counter")
bx0b, bx1b = P(280, 0)[0], P(910, 0)[0]
b.box((bx1b - bx0b, 0.62, 0.86), ((bx0b + bx1b) / 2, BACK_Y, 0.43), M_STEEL)
b.box((bx1b - bx0b, 0.64, 0.04), ((bx0b + bx1b) / 2, BACK_Y, 0.88), M_STEEL)
for x in np.arange(bx0b + 0.4, bx1b, 0.8):
    b.box((0.02, 0.01, 0.7), (x, BACK_Y + 0.315, 0.45), M_BLACK)
# 软木留言板 + 配电箱
# （原墙上杯架层板已移除：杯子按实景归置到台面置物架中层，见 Counter_Shelf）
b.box((0.9, 0.03, 0.6), (bx0b + 4.0, 0.12, 1.85), M_CORK)
for k in range(10):
    b.box((0.09, 0.005, 0.07), (bx0b + 3.65 + (k % 5) * 0.16, 0.137, 1.7 + (k // 5) * 0.22), M_PAPER,
          (0, 0, 0))
b.box((0.7, 0.2, 0.5), (bx1b - 0.6, 0.2, 2.1), M_ELEC)
b.finish(COLLS["Bar"])

# 灯箱 (后墙顶部): 素晴 logo + 咖啡豆画面
b = B("Bar_Lightbox")
lb_len = bx1b - bx0b
b.box((lb_len, 0.1, 0.6), ((bx0b + bx1b) / 2, 0.15, 2.85), M_WHITE)
logo_w = 0.6 * 2347 / 1138
b.img_plane(logo_w, 0.56, MX((bx1b - 0.1 - logo_w / 2, 0.205, 2.85), (0, 0, math.pi)), M_LOGO)
b.img_plane(logo_w, 0.56, MX((bx0b + 0.1 + logo_w / 2, 0.205, 2.85), (0, 0, math.pi)), M_LOGO)
b.img_plane(lb_len - 2 * logo_w - 0.4, 0.56, MX(((bx0b + bx1b) / 2, 0.205, 2.85), (0, 0, math.pi)), M_BEANS)
b.finish(COLLS["Bar"], info=("素晴 灯箱", "吧台后的发光灯箱，左边是店名「素晴」书法 logo。"))

# --- 咖啡机 VISACREM Ottima 2.0 (照片14) ---
# 位置东移 (px 470→630)：原位置左侧放不下水槽/材料台/热水机（实景顺序：热水机→材料台→水槽→磨豆→压粉→磨豆→咖啡机）
CM = (P(630, 0)[0], BACK_Y + 0.0, 0.9)
b = B("Coffee_Machine")
b.box((0.78, 0.55, 0.13), (0, 0, 0.065), M_WHITEM)
b.box((0.72, 0.30, 0.025), (0, 0.10, 0.145), M_STEEL)
b.box((0.70, 0.28, 0.005), (0, 0.10, 0.16), M_PERF)
b.box((0.70, 0.22, 0.30), (0, -0.16, 0.30), M_WHITEM)
b.img_plane(0.66, 0.26, MX((0, -0.049, 0.30), (0, 0, math.pi)), M_MACHINE_LIGHT)
b.box((0.80, 0.55, 0.13), (0, 0, 0.515), M_WHITEM)
for side in (-1, 1):
    for k in range(5):
        b.sphere(0.014, (side * (0.09 + k * 0.038) + (-0.0 if side > 0 else 0), 0.276, 0.51), M_CHROME, seg=10)
    gx = side * 0.18
    b.cyl(0.065, 0.1, (gx, 0.02, 0.40), M_CHROME, seg=24)
    b.cyl(0.02, 0.1, (gx, 0.02, 0.30), M_CHROME, seg=12)
    b.cyl(0.045, 0.04, (gx, 0.05, 0.22), M_CHROME, seg=20)
    b.beam((gx, 0.08, 0.22), (gx + 0.05 * side, 0.28, 0.20), 0.032, M_BLACK)
    # 蒸汽棒
    b.beam((side * 0.36, 0.2, 0.45), (side * 0.36, 0.24, 0.22), 0.012, M_CHROME)
for x in (0.33, -0.27, -0.36):
    b.cyl(0.045, 0.05, (x, 0.29, 0.51), M_BLACK, rot=(math.pi / 2, 0, 0), seg=20)
b.cyl(0.04, 0.01, (0.25, -0.046, 0.24), M_BLACK, rot=(math.pi / 2, 0, 0), seg=20)
# 顶部杯架 + 杯子
for x in np.arange(-0.32, 0.33, 0.16):
    b.beam((x, -0.2, 0.58), (x, -0.2, 0.78), 0.008, M_BLACK)
    b.beam((x, 0.2, 0.58), (x, 0.2, 0.78), 0.008, M_BLACK)
b.beam((-0.35, -0.2, 0.78), (0.35, -0.2, 0.78), 0.008, M_BLACK)
for k, x in enumerate(np.arange(-0.27, 0.3, 0.135)):
    b.cyl(0.035, 0.07, (x, 0.05, 0.615), M_CUPGLASS, seg=14)
    if k % 2 == 0:
        b.cyl(0.04, 0.09, (x, -0.12, 0.625), M_CERAMIC, seg=14)
ob_cm = b.finish(COLLS["Bar"], CM, 0,
                 info=("意式咖啡机 · VISACREM Ottima 2.0", "双头半自动意式咖啡机。店里的美式、拿铁、摩卡都从这里萃取。"))
# 品牌字
fc = bpy.data.curves.new("VISACREM_txt", 'FONT')
fc.body = "VISACREM"
fc.size = 0.05
fc.align_x = 'CENTER'
fc.extrude = 0.001
tob = bpy.data.objects.new("VISACREM_txt_obj", fc)
COLLS["Bar"].objects.link(tob)
dg = bpy.context.evaluated_depsgraph_get()
tme = bpy.data.meshes.new_from_object(tob.evaluated_get(dg))
bpy.data.objects.remove(tob)
tme.materials.append(M_BLACK)
txt = bpy.data.objects.new("Coffee_Machine_Logo", tme)
txt.location = (0, 0.277, 0.545)
txt.rotation_euler = (math.pi / 2, 0, math.pi)
COLLS["Bar"].objects.link(txt)
txt.parent = ob_cm


def grinder(name, loc, hat=True, info=None):
    b = B(name)
    b.box((0.24, 0.28, 0.04), (0, 0, 0.02), M_BLACK)
    b.prism([(-0.12, 0), (0.12, 0), (0.09, 0.4), (-0.09, 0.4)], 0.26, M_WHITEM, MX((0, 0, 0.04), (math.pi / 2, 0, 0)))
    b.box((0.15, 0.01, 0.15), (0, 0.135, 0.36), M_BLACK)
    b.img_plane(0.06, 0.03, MX((0, 0.142, 0.4), (0, 0, math.pi)), M_LCD)
    b.cyl(0.07, 0.04, (0, -0.02, 0.46), M_CHROME, seg=24)
    b.cyl(0.05, 0.05, (0, -0.02, 0.5), M_BLACK, seg=24)
    b.cyl(0.055, 0.22, (0, -0.02, 0.63), M_BLACK, seg=24, r2=0.11)
    b.box((0.08, 0.03, 0.12), (0, 0.13, 0.2), M_CHROME)
    if hat:
        straw_hat(b, (0, -0.02, 0.73), 0.95, (0.12, 0.08))
    return b.finish(COLLS["Bar"], loc, 0, info)


grinder("Grinder_1", (CM[0] - 0.58, BACK_Y, 0.9),
        info=("磨豆机 (戴草帽的那台)", "电控定量磨豆机，屏幕上的 7.4 是研磨刻度。头上的草帽是老板给它戴的 🙂"))
b = B("Tamper")
b.cyl(0.11, 0.34, (0, 0, 0.17), M_WHITEM, seg=28)
b.box((0.16, 0.06, 0.12), (0, 0.09, 0.12), M_BLACK)
b.cyl(0.03, 0.06, (0, 0.11, 0.25), M_BLACK, rot=(math.pi / 2, 0, 0), scale=(1, 1.4, 1), seg=16)
straw_hat(b, (0, 0, 0.34), 1.0, (0.05, -0.08))
b.finish(COLLS["Bar"], (CM[0] - 0.9, BACK_Y, 0.9), 0,
         info=("自动压粉机", "和磨豆机一样戴着同款草帽，保证每一份粉饼都压得平整。"))
grinder("Grinder_2", (CM[0] - 1.2, BACK_Y, 0.9), info=("第二台磨豆机", "同样戴着草帽，用来磨另一款豆子。"))

# --- 水槽（磨豆机2号继续往右）---
b = B("Sink")
b.box((0.56, 0.44, 0.03), (0, 0, 0.015), M_STEEL)                       # 盆沿
b.box((0.47, 0.35, 0.025), (0, 0.005, 0.012), M_DARKBOX)                # 盆体（暗色内凹感）
b.cyl(0.015, 0.26, (0, -0.17, 0.13), M_CHROME, seg=12)                  # 龙头立管
b.beam((0, -0.17, 0.25), (0, 0.04, 0.25), 0.014, M_CHROME)              # 龙头横臂
b.finish(COLLS["Bar"], (CM[0] - 1.798, BACK_Y, 0.9), 0,
         info=("水槽", "洗杯子、洗拉花缸。台下柜体用铁板封住。"))

# --- 墙面置物架（挂墙，在水槽正上方；实景照片：顶面茶叶罐、中层杯子、下层玻璃杯）---
b = B("Counter_Shelf")
SW, SD, SH = 0.78, 0.32, 0.85
for sx in (-1, 1):
    b.box((0.025, SD, SH), (sx * (SW / 2 - 0.013), 0, SH / 2), M_WHITEM)
b.box((SW, SD, 0.025), (0, 0, SH - 0.013), M_WHITEM)                    # 顶板
b.box((SW, SD, 0.025), (0, 0, 0.0125), M_WHITEM)                        # 底板
b.box((SW - 0.05, SD - 0.03, 0.02), (0, 0, 0.42), M_WHITEM)             # 中层板
b.box((SW, 0.02, SH - 0.06), (0, -SD / 2 + 0.01, SH / 2), M_WHITEM)     # 背板
for k in range(6):                                                      # 顶面：玻璃茶叶罐
    x = -SW / 2 + 0.09 + k * 0.125
    b.cyl(0.045, 0.11, (x, 0.02, SH + 0.055), M_CUPGLASS, seg=12)
    b.cyl(0.038, 0.065, (x, 0.02, SH + 0.04), M_SOIL, seg=12)
for k in range(5):                                                      # 中层：杯子
    b.cyl(0.035, 0.08, (-SW / 2 + 0.1 + k * 0.15, 0.01, 0.475), M_CERAMIC if k % 2 else M_CERAMIC_B, seg=12)
for k in range(4):                                                      # 下层：玻璃杯
    b.cyl(0.032, 0.12, (-SW / 2 + 0.12 + k * 0.18, 0.01, 0.085), M_CUPGLASS, seg=12)
b.finish(COLLS["Bar"], (CM[0] - 1.798, 0.36, 1.30), 0,
         info=("置物架", "挂在水槽正上方的墙架：顶面茶叶罐、中层杯子、下层玻璃杯；台面上放可可粉、抹茶粉、巧克力酱。"))
# 台面上的常温材料罐（可可粉/抹茶粉/巧克力酱）
b = B("Counter_Canisters")
for k, m in enumerate((M_CORK, M_LEAF[1], M_BLACK)):
    b.cyl(0.045, 0.16, (k * 0.13 - 0.13, 0, 0.08), m, seg=14)
    b.cyl(0.046, 0.02, (k * 0.13 - 0.13, 0, 0.165), M_CHROME, seg=14)
b.finish(COLLS["Bar"], (CM[0] - 2.518, BACK_Y + 0.2, 0.9), 0,
         info=("常温材料", "可可粉、抹茶粉、巧克力酱。"))

# --- 热水机（台面最右端，黑色带蓝色屏显）---
b = B("Water_Boiler")
b.box((0.38, 0.42, 0.52), (0, 0, 0.26), M_BLACK)                        # 机身
b.box((0.34, 0.06, 0.3), (0, 0.19, 0.15), M_STEEL)                      # 接水台
b.img_plane(0.09, 0.05, MX((0, 0.215, 0.42), (0, 0, math.pi)), M_LCD)   # 蓝色屏显
b.cyl(0.012, 0.1, (0, 0.2, 0.28), M_CHROME, seg=10)                     # 出水嘴
b.box((0.36, 0.4, 0.03), (0, 0.01, 0.53), M_BLACK)                      # 顶盖
b.finish(COLLS["Bar"], (CM[0] - 3.148, BACK_Y, 0.9), 0,
         info=("热水机", "电热开水机，泡茶、冲热饮用它。"))

# --- 台下设备（面向通道开门，略突出柜台面 2cm）---
b = B("Fridge")
b.box((0.58, 0.62, 0.84), (0, 0, 0.42), M_STEEL)
b.box((0.52, 0.02, 0.76), (0, 0.32, 0.44), M_WHITEM)                    # 门板
b.box((0.035, 0.04, 0.42), (0.20, 0.33, 0.45), M_CHROME)                # 竖拉手
b.finish(COLLS["Bar"], (CM[0] - 3.281, BACK_Y + 0.01, 0), 0,
         info=("台下冰箱", "纯牛奶、椰奶、咖啡奶都冰在这里。"))
b = B("Ice_Maker")
b.box((0.62, 0.62, 0.84), (0, 0, 0.42), M_STEEL)
b.box((0.56, 0.02, 0.34), (0, 0.32, 0.62), M_WHITEM)                    # 上门板（取冰口）
b.box((0.5, 0.02, 0.26), (0, 0.32, 0.18), M_GRILLE)                     # 下散热格栅
b.finish(COLLS["Bar"], (CM[0], BACK_Y + 0.01, 0), 0,
         info=("制冰机", "在咖啡机正下方，冰块随取随用。"))

b = B("Back_Counter_Misc")
b.cyl(0.08, 0.3, (CM[0] + 1.0, BACK_Y, 1.05), M_CERAMIC, seg=20)
b.cyl(0.07, 0.12, (CM[0] + 1.8, BACK_Y + 0.1, 0.96), M_CUPGLASS, seg=16)
b.box((0.4, 0.35, 0.35), (CM[0] + 2.5, BACK_Y, 1.075), M_DARKBOX)
b.finish(COLLS["Bar"])

# --- 吧台上方铜色吊架 ---
b = B("Bar_Rack")
rx0, rx1 = BAR_C[0] - 1.6, BAR_C[0] + 0.4
for z in (2.45, 2.75):
    for yy in (BAR_C[1] - 0.25, BAR_C[1] + 0.25):
        b.beam((rx0, yy, z), (rx1, yy, z), 0.025, M_BRASS)
    for xx in (rx0, rx1, (rx0 + rx1) / 2):
        b.beam((xx, BAR_C[1] - 0.25, z), (xx, BAR_C[1] + 0.25, z), 0.025, M_BRASS)
for xx in (rx0, rx1):
    for yy in (BAR_C[1] - 0.25, BAR_C[1] + 0.25):
        b.beam((xx, yy, 2.44), (xx, yy, 3.2), 0.025, M_BRASS)
for k in range(8):  # 悬挂的黑色杯串
    b.cyl(0.035, 0.06, (rx0 + 0.9 + 0.03 * (k % 2), BAR_C[1], 2.35 - k * 0.07), M_BLACK, seg=10)
b.finish(COLLS["Bar"])

# =====================================================================
#                           吊灯 + 铜色吊架
# =====================================================================


def pumpkin_lamp(name, loc, a=0.23, bh=0.15, t0=0.18, t1=2.35, n=34, w=0.05, cord_top=H, info=None):
    b = B(name)
    cz = -bh - 0.02
    for k in range(n):
        phi = 2 * math.pi * k / n
        outer = [(a * math.sin(t), bh * math.cos(t)) for t in np.linspace(t0, t1, 14)]
        inner = [((a - w) * math.sin(t), (bh - w * 0.8) * math.cos(t)) for t in np.linspace(t1, t0, 14)]
        b.prism(outer + inner, 0.004, M_WOOD, MX((0, 0, cz), (0, 0, phi)) @ MX(rot=(math.pi / 2, 0, 0)))
    rr = (a - w / 2) * math.sin(t1)
    b.cyl(rr, 0.015, (0, 0, cz + bh * math.cos(t1)), M_WOOD, caps=False, seg=40)
    b.cyl(0.05, 0.02, (0, 0, -0.01), M_WOOD, seg=20)
    b.cyl(0.02, 0.06, (0, 0, cz + 0.06), M_BLACK, seg=12)
    b.sphere(0.035, (0, 0, cz), M_BULB, seg=12)
    L = cord_top - loc[2]
    b.cyl(0.003, L, (0, 0, L / 2), M_BLACK, seg=6, smooth=False)
    b.cyl(0.05, 0.02, (0, 0, L - 0.01), M_BLACK, seg=16)
    ob = b.finish(COLLS["Lighting"], loc, 0, info or ("南瓜木片吊灯", "原木薄片拼成的南瓜吊灯，暖黄色的光是店里的灵魂。"))
    add_light(name + "_Light", 'POINT', (loc[0], loc[1], loc[2] + cz), 45, color=(1.0, 0.68, 0.38), size=0.08)
    return ob


def brass_frame_along(name, s, d, t0, t1, z=3.3, top=H):
    b = B(name)
    p0, p1 = s.pt(t0, d, z), s.pt(t1, d, z)
    b.beam(p0, p1, 0.03, M_BRASS)
    for p in (p0, p1):
        b.beam(p, (p[0], p[1], top), 0.03, M_BRASS)
    q0, q1 = s.pt(t0, d - 0.35, top - 0.6), s.pt(t1, d - 0.35, top - 0.6)
    b.beam(q0, q1, 0.03, M_BRASS)
    for p, q in ((p0, q0), (p1, q1)):
        b.beam((q[0], q[1], q[2]), (q[0], q[1], top), 0.03, M_BRASS)
    b.finish(COLLS["Decor"])


# 沉浸区窗边
s = SEGS[0]
brass_frame_along("Brass_Frame_Immersive", s, 0.75, 0.6, s.L - 0.6)
for px in (292, 520):
    x, _ = P(px, 0)
    pumpkin_lamp("Pendant_Immersive_%d" % px, (x, s.a.y - 0.75, 2.45), cord_top=3.3)
# 斜窗
s = SEGS[2]
brass_frame_along("Brass_Frame_Diag", s, 0.6, 0.5, s.L - 0.5)
for f in (0.3, 0.7):
    p = s.pt(s.L * f, 0.6, 2.55)
    pumpkin_lamp("Pendant_Diag_%d" % int(f * 10), p, cord_top=3.3)
# 东窗
s = SEGS[4]
brass_frame_along("Brass_Frame_East", s, 0.6, 0.5, s.L - 0.5)
for f in (0.3, 0.7):
    p = s.pt(s.L * f, 0.6, 2.55)
    pumpkin_lamp("Pendant_East_%d" % int(f * 10), p, cord_top=3.3)
# 书墙前
s = SEGS[10]
brass_frame_along("Brass_Frame_Books", s, 1.0, 0.8, s.L - 0.8)
for f in (0.33, 0.67):
    p = s.pt(s.L * f, 1.0, 2.5)
    pumpkin_lamp("Pendant_Books_%d" % int(f * 10), p, cord_top=3.3)
# 入口处大号甜甜圈吊灯 (照片7)
pumpkin_lamp("Pendant_Entrance_Big", (P(250, 600)[0], P(250, 600)[1], 3.0), a=0.42, bh=0.12, t0=0.55, t1=2.55,
             n=48, w=0.09, cord_top=H - 0.55,
             info=("入口大吊灯", "进门第一眼看到的环形木片吊灯。"))
# 吧台小花朵吊灯
b = B("Pendant_Bar_Flower")
lp = (P(800, 1300)[0], P(800, 1300)[1] + BAR_SHIFT, 2.3)
b.cyl(0.002, 0.88, (0, 0, 0.46), M_BRASS, seg=6, smooth=False)
for k in range(8):
    a = k * math.pi / 4
    b.box((0.12, 0.05, 0.004), (0.06 * math.cos(a), 0.06 * math.sin(a), -0.02), M_CERAMIC, (0.4, 0, a))
b.sphere(0.04, (0, 0, -0.06), M_BULB, seg=12)
b.finish(COLLS["Lighting"], lp)
add_light("Pendant_Bar_Flower_Light", 'POINT', (lp[0], lp[1], lp[2] - 0.08), 25, color=(1.0, 0.75, 0.5))

# --- 入口灯箱 (门内侧上方) ---
s = SEGS[11]
b = B("Entrance_Lightbox")
c = s.pt(s.L / 2, 0.15, 3.05)
b.box((1.75, 0.08, 0.95), c, M_WHITE, (0, 0, s.ang))
for k in (-0.6, 0.6):
    pp = s.pt(s.L / 2 + k, 0.15, 3.53)
    b.box((0.06, 0.03, 0.1), pp, M_ALU, (0, 0, s.ang))
b.img_plane(1.71, 0.83, MX(s.pt(s.L / 2, 0.195, 3.05), (0, 0, s.ang)), M_LOGO)
b.finish(COLLS["Decor"], info=("素晴 COFFEE SHOP", "门口的发光招牌。「素晴」——素净、晴朗。"))

# =====================================================================
#                              绿植
# =====================================================================


def leaf_pts(L, W):
    up = [(L * s, W / 2 * math.sin(math.pi * s) ** 0.75) for s in np.linspace(0, 1, 9)]
    lo = [(L * s, -W / 2 * math.sin(math.pi * s) ** 0.75) for s in np.linspace(1, 0, 9)[1:-1]]
    return up + lo


def plant(name, loc, kind, seed):
    rng = random.Random(seed)
    b = B(name)
    if kind in ("bird", "tree"):
        b.cyl(0.25, 0.6, (0, 0, 0.3), M_POT, seg=4, r2=0.31, rot=(0, 0, math.pi / 4), smooth=False)
        b.cyl(0.21, 0.02, (0, 0, 0.59), M_SOIL, seg=4, rot=(0, 0, math.pi / 4), smooth=False)
        soil = 0.6
    elif kind == "small":  # 桌间小盆栽（0.86m 桌缝专用，冠幅 ~0.35m 不越桌沿）
        b.cyl(0.13, 0.26, (0, 0, 0.13), M_POTGRAY, seg=20, r2=0.15)
        b.cyl(0.135, 0.02, (0, 0, 0.265), M_SOIL, seg=20)
        soil = 0.27
    else:
        b.cyl(0.22, 0.55, (0, 0, 0.275), M_POTGRAY if seed % 2 else M_POT, seg=24, r2=0.24)
        b.cyl(0.22, 0.02, (0, 0, 0.54), M_SOIL, seg=24)
        soil = 0.55
    if kind == "small":  # 矮丛：叶片短、向上散开
        for k in range(9):
            yaw = rng.uniform(0, 2 * math.pi)
            lean = rng.uniform(0.1, 0.6)
            Ls = rng.uniform(0.16, 0.24)
            top = Vector((math.cos(yaw) * math.sin(lean) * Ls, math.sin(yaw) * math.sin(lean) * Ls,
                          soil + math.cos(lean) * Ls))
            b.beam((0, 0, soil), top, 0.008, M_STEM)
            m = MX(top, (rng.uniform(-0.3, 0.3), -rng.uniform(0.5, 1.0), yaw))
            b.prism(leaf_pts(0.2, 0.09), 0.003, rng.choice(M_LEAF), m)
        return b.finish(COLLS["Plants"], loc, rng.uniform(0, 6.28),
                        ("桌间小盆栽", "圆桌之间摆的小盆绿植，冠幅不越过桌沿。"))
    if kind == "bird":  # 天堂鸟/竹芋
        for k in range(rng.randint(9, 13)):
            yaw = rng.uniform(0, 2 * math.pi)
            lean = rng.uniform(0.05, 0.35)
            Ls = rng.uniform(0.5, 1.2)
            top = Vector((math.cos(yaw) * math.sin(lean) * Ls, math.sin(yaw) * math.sin(lean) * Ls,
                          soil + math.cos(lean) * Ls))
            b.beam((0, 0, soil), top, 0.012, M_STEM)
            pitch = rng.uniform(0.6, 1.3)
            m = MX(top, (rng.uniform(-0.5, 0.5), -pitch, yaw))
            b.prism(leaf_pts(rng.uniform(0.45, 0.7), rng.uniform(0.11, 0.17)), 0.004, rng.choice(M_LEAF), m)
    elif kind == "tree":  # 发财树
        th = rng.uniform(1.1, 1.4)
        b.cyl(0.035, th, (0, 0, soil + th / 2), M_TRUNK, seg=10)
        for k in range(rng.randint(26, 34)):
            yaw = rng.uniform(0, 2 * math.pi)
            z = soil + th + rng.uniform(-0.25, 0.35)
            base = Vector((math.cos(yaw) * 0.05, math.sin(yaw) * 0.05, z))
            tip = base + Vector((math.cos(yaw) * 0.18, math.sin(yaw) * 0.18, rng.uniform(-0.05, 0.12)))
            b.beam(base, tip, 0.008, M_STEM)
            for j in range(5):
                m = MX(tip, (0, rng.uniform(-0.2, 0.4), yaw + (j - 2) * 0.5))
                b.prism(leaf_pts(0.2, 0.07), 0.003, rng.choice(M_LEAF), m)
    else:  # 龟背竹
        for k in range(rng.randint(7, 10)):
            yaw = rng.uniform(0, 2 * math.pi)
            lean = rng.uniform(0.3, 0.8)
            Ls = rng.uniform(0.4, 0.9)
            top = Vector((math.cos(yaw) * math.sin(lean) * Ls, math.sin(yaw) * math.sin(lean) * Ls,
                          soil + math.cos(lean) * Ls))
            b.beam((0, 0, soil), top, 0.012, M_STEM)
            m = MX(top, (0, rng.uniform(-0.2, 0.3), yaw))
            b.prism(leaf_pts(0.42, 0.36), 0.004, rng.choice(M_LEAF), m)
    return b.finish(COLLS["Plants"], loc, rng.uniform(0, 6.28), ("绿植", "店里到处是老板养的绿植：天堂鸟、发财树、龟背竹……"))


# 圆桌之间原摆了三盆天堂鸟：台间距只有 0.4m，盆径 0.5m 且叶片展开 0.7m，
# 必然压进两侧矮凳（截图 3 的错处）。本轮把这三盆移走，只在吧台东端留一盆。
# 另：多人桌 1 北上后原位置被沙发占住，第 1 盆改到书墙前（x0.85, y4.5）。
# 圆桌之间（台距 1.56m 留出 0.86m 缝）摆平面图上的桌间绿植，用紧凑的小盆栽，
# 叶片不越过凳背；书墙前一盆发财树，靠墙不占过道。
# 2026-11-06：原 (608,500) 的天堂鸟就在斜窗长桌吧凳边上、挡着 D2/D3 座位，实景没有，已删掉。
# 同时把斜窗那棵发财树往墙根挪 0.45m（py 325→292）：原位置压进 Sun_Stool_D1 0.36m。
# 书墙前那棵发财树（224,976）实景也没有，已删掉。
# 2026-11-06：右下角 (1028,1075) 那棵龟背竹删掉 —— 那里现在是主入口门洞，
# 一盆冠幅 1.1m 的绿植正堵在门内 0.4m 处，人进不来。
PLANTS = [((793, 854), "small"), ((793, 731), "small"), ((595, 292), "tree"),
          ((497, 440), "monstera")]
for i, ((px, py), kind) in enumerate(PLANTS):
    plant("Plant_%d" % i, (*P(px, py), 0), kind, i * 13 + 1)

# 立式空调 (照片 2/11)
b = B("AC_Cabinet")
b.box((0.5, 0.35, 1.8), (0, 0, 0.9), M_WHITEM)
b.box((0.42, 0.01, 0.8), (0, 0.176, 0.6), M_GRILLE)
b.finish(COLLS["Decor"], (P(940, 620)[0] + 0.1, P(940, 620)[1] - 0.05, 0), 0,
         info=("立式空调", "夏天的救星。"))

# =====================================================================
#                              世界 / 渲染
# =====================================================================
world = bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.09, 0.08, 0.07, 1)
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0

scene.render.engine = 'BLENDER_EEVEE_NEXT'
scene.eevee.taa_render_samples = 48
try:
    scene.eevee.use_raytracing = True
except Exception:
    pass
scene.view_settings.view_transform = 'AgX'
scene.view_settings.exposure = 0.9
scene.render.resolution_x = 1600
scene.render.resolution_y = 1000

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "coffee_shop.blend"))
print("BUILD OK", len(bpy.data.objects), "objects")

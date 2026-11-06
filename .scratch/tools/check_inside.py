"""检查家具是否跑到墙外（地板多边形外）。blender -b coffee_shop.blend --python check_inside.py"""
import bpy
from mathutils import Vector

S = 1.5 / 147 * 1.25
FP = [(160, 115), (610, 115), (610, 295), (920, 600), (1065, 600), (1065, 1180), (920, 1180),
      (920, 1350), (270, 1350), (270, 1175), (160, 1078), (160, 668), (160, 470)]
POLY = [((px - 150) * S, (1360 - py) * S) for px, py in FP]


def inside(x, y):
    c = False
    j = len(POLY) - 1
    for i in range(len(POLY)):
        xi, yi = POLY[i]
        xj, yj = POLY[j]
        if ((yi > y) != (yj > y)) and (x < (xj - xi) * (y - yi) / (yj - yi) + xi):
            c = not c
        j = i
    return c


SKIP = ("Floor", "Ceiling", "Wall", "Door", "Window", "Brass_Frame", "Structure", "Blind")
bad = 0
for ob in bpy.data.objects:
    if ob.type != 'MESH' or ob.name.startswith(SKIP):
        continue
    pts = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    cx = sum(p.x for p in pts) / 8
    cy = sum(p.y for p in pts) / 8
    if not inside(cx, cy):
        print("OUTSIDE  %-24s (%.2f, %.2f)" % (ob.name, cx, cy))
        bad += 1
print("总物件 %d，出界 %d" % (len([o for o in bpy.data.objects if o.type == 'MESH' and not o.name.startswith(SKIP)]), bad))

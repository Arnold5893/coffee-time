"""列出所有对象的世界位置与包围盒尺寸，按 y 排序，便于定位不明物体。"""
import bpy, mathutils

rows = []
for ob in bpy.data.objects:
    if ob.type != 'MESH':
        continue
    p = ob.matrix_world.translation
    pts = [ob.matrix_world @ mathutils.Vector(c) for c in ob.bound_box]
    xs = [v.x for v in pts]; ys = [v.y for v in pts]; zs = [v.z for v in pts]
    rows.append((p.y, p.x, ob.name, max(xs)-min(xs), max(ys)-min(ys), max(zs)-min(zs), min(zs)))
rows.sort()
for y, x, n, sx, sy, sz, z0 in rows:
    if y < 4.0:  # 吧台附近区域
        print("y=%5.2f x=%5.2f  %-26s %4.2fx%4.2fx%4.2f z0=%4.2f" % (y, x, n, sx, sy, sz, z0))

"""列出后柜台面上（z 0.85..1.9，y 0.0..0.8）的所有物件，按 x 排序。
用于确认咖啡机左右两侧哪一段台面是空的 —— 工作区要摆在那儿。
Unity x = -Blender x。
"""
import bpy
import mathutils

rows = []
for o in bpy.data.objects:
    if o.type != 'MESH':
        continue
    bb = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
    xs = [v.x for v in bb]
    ys = [v.y for v in bb]
    zs = [v.z for v in bb]
    # 坐在后柜上：底部在柜台面 0.9 附近、深度在后柜范围、水平位置在吧台那一段
    if min(zs) < 1.20 and max(zs) > 0.85 and min(ys) > -0.10 and max(ys) < 0.95 and max(xs) > 1.5:
        rows.append((min(xs), max(xs), min(zs), max(zs), o.name))

rows.sort()
print("=== 后柜台面物件（Blender 坐标，Unity x = -x）===")
for x0, x1, z0, z1, name in rows:
    print("x %6.2f..%6.2f (Unity %6.2f..%6.2f)  z %4.2f..%4.2f  %s"
          % (x0, x1, -x1, -x0, z0, z1, name))

print("=== 咖啡机 ===")
cm = bpy.data.objects.get("Coffee_Machine")
if cm:
    bb = [cm.matrix_world @ mathutils.Vector(c) for c in cm.bound_box]
    xs = [v.x for v in bb]
    print("Coffee_Machine x %.2f..%.2f  Unity x %.2f..%.2f  z %.2f..%.2f"
          % (min(xs), max(xs), -max(xs), -min(xs), min(v.z for v in bb), max(v.z for v in bb)))

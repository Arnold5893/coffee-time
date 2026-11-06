"""按包围盒中心找 x∈[0.8,2.8], y∈[0.6,2.2] 范围内的对象。"""
import bpy, mathutils

for ob in bpy.data.objects:
    if ob.type != 'MESH':
        continue
    pts = [ob.matrix_world @ mathutils.Vector(c) for c in ob.bound_box]
    xs = [v.x for v in pts]; ys = [v.y for v in pts]; zs = [v.z for v in pts]
    cx, cy = (min(xs)+max(xs))/2, (min(ys)+max(ys))/2
    if 0.8 <= cx <= 2.8 and 0.6 <= cy <= 2.2:
        print("%-28s center=(%5.2f,%5.2f) size=%4.2fx%4.2fx%4.2f z0=%4.2f" % (
            ob.name, cx, cy, max(xs)-min(xs), max(ys)-min(ys), max(zs)-min(zs), min(zs)))

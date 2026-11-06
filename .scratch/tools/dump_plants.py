"""列出绿植与阳光区吧凳的实际包围盒，便于排位。blender -b coffee_shop.blend --python dump_plants.py"""
import bpy
from mathutils import Vector


def bb(ob):
    pts = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    return (min(p.x for p in pts), min(p.y for p in pts), max(p.x for p in pts), max(p.y for p in pts))


for ob in sorted(bpy.data.objects, key=lambda o: o.name):
    if ob.type != 'MESH':
        continue
    if ob.name.startswith(("Plant", "Sun_Stool", "Bar_Stool", "Round1", "Meeting", "Group_")):
        x0, y0, x1, y1 = bb(ob)
        print("%-20s x %5.2f..%5.2f (c %5.2f)  y %5.2f..%5.2f (c %5.2f)" %
              (ob.name, x0, x1, (x0 + x1) / 2, y0, y1, (y0 + y1) / 2))

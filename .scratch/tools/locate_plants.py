import bpy, math
from mathutils import Vector

objs = [o for o in bpy.data.objects if o.type == 'MESH']

def wbb(o):
    pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    return (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs))

def ctr(o):
    b = wbb(o)
    return Vector(((b[0]+b[1])/2, (b[2]+b[3])/2, (b[4]+b[5])/2))

plants = [o for o in objs if o.name.startswith("Plant")]
print("=== PLANTS ===")
for p in plants:
    c = ctr(p); b = wbb(p)
    print("%-10s  xy=(%.2f, %.2f)  z=%.2f..%.2f  potW=%.2f" %
          (p.name, c.x, c.y, b[4], b[5], b[1]-b[0]))

print("\n=== NEAREST NEIGHBOURS (within 2.2m, xy) ===")
for p in plants:
    c = ctr(p)
    near = []
    for o in objs:
        if o is p or o.name.startswith("Plant"):
            continue
        d = (ctr(o).xy - c.xy).length
        if d < 2.2:
            near.append((d, o.name))
    near.sort()
    print("\n-- %s --" % p.name)
    for d, n in near[:8]:
        print("   %5.2f  %s" % (d, n))

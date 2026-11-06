"""平面视角俯视渲染（藏吊顶），用于和 CAD 比对布局。
blender -b coffee_shop.blend --python view_plan.py -- <out> [cx cy scale]
"""
import bpy, sys
from mathutils import Vector

args = list(sys.argv[sys.argv.index("--") + 1:]) if "--" in sys.argv else []
out = args[0] if args else r"D:\coffee_shop\.scratch\plan.png"
cx = float(args[1]) if len(args) > 1 else 6.0
cy = float(args[2]) if len(args) > 2 else 5.0
sc = float(args[3]) if len(args) > 3 else 17.0

for o in bpy.data.objects:
    if o.type != 'MESH':
        continue
    zs = [(o.matrix_world @ Vector(c)).z for c in o.bound_box]
    if min(zs) > 2.55:
        o.hide_render = True

scene = bpy.context.scene
cd = bpy.data.cameras.new("PlanCam")
cd.type = 'ORTHO'
cd.ortho_scale = sc
cd.clip_end = 60
cam = bpy.data.objects.new("PlanCam", cd)
scene.collection.objects.link(cam)
cam.location = (cx, cy, 14.0)
cam.rotation_euler = (0, 0, 0)
scene.camera = cam

ld = bpy.data.lights.new("PlanSun", 'SUN')
ld.energy = 4.0
sun = bpy.data.objects.new("PlanSun", ld)
scene.collection.objects.link(sun)
sun.rotation_euler = (0.5, 0.2, 0.3)

scene.render.resolution_x = 1200
scene.render.resolution_y = 1200
scene.render.filepath = out
bpy.ops.render.render(write_still=True)
print("RENDER ->", out)

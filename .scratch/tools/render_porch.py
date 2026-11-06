"""从店外看入口门 + 门外地台，快速目视检查几何。
blender -b coffee_shop.blend --python render_porch.py -- 输出路径 [eye|top|out]
eye: 站在门外南侧看门   top: 正上方俯视   out: 更远的斜侧视角
"""
import bpy, sys, math
from mathutils import Vector

args = list(sys.argv[sys.argv.index("--") + 1:]) if "--" in sys.argv else []
out = args[0] if args else r"D:\coffee_shop\.scratch\porch_check.png"
mode = args[1] if len(args) > 1 else "eye"

scene = bpy.context.scene
leaf = bpy.data.objects.get("Door_entrance_Leaf")
thr = bpy.data.objects.get("Door_entrance_Threshold")
if thr:
    pts = [thr.matrix_world @ Vector(c) for c in thr.bound_box]
    c = sum(pts, Vector()) / 8
else:
    c = leaf.matrix_world.translation if leaf else Vector((10.74, 2.20, 0))
if mode == "left":
    lk = bpy.data.objects.get("Door_locked")
    pc = [lk.matrix_world @ Vector(v) for v in lk.bound_box]
    c = sum(pc, Vector()) / 8
print("door center:", tuple(round(v, 3) for v in c))

cam_data = bpy.data.cameras.new("ChkCam")
cam = bpy.data.objects.new("ChkCam", cam_data)
scene.collection.objects.link(cam)

if mode == "top":
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 8.0
    cam.location = (c.x, c.y - 1.0, 12.0)
    cam.rotation_euler = (0, 0, 0)
elif mode == "left":
    cam_data.lens = 24
    cam.location = (c.x - 4.5, c.y, 1.65)
    target = Vector((c.x, c.y, 1.20))
    d = target - cam.location
    cam.rotation_euler = (math.atan2(math.hypot(d.x, d.y), d.z), 0, math.atan2(d.y, d.x) - math.pi / 2)
elif mode == "out":
    cam_data.lens = 24
    cam.location = (c.x + 4.2, c.y - 5.2, 2.6)
    target = Vector((c.x, c.y, 1.2))
    d = target - cam.location
    cam.rotation_euler = (math.atan2(math.hypot(d.x, d.y), d.z), 0, math.atan2(d.y, d.x) - math.pi / 2)
else:
    cam_data.lens = 24
    cam.location = (c.x, c.y - 5.6, 1.65)
    target = Vector((c.x, c.y, 1.30))
    d = target - cam.location
    cam.rotation_euler = (math.atan2(math.hypot(d.x, d.y), d.z), 0, math.atan2(d.y, d.x) - math.pi / 2)
scene.camera = cam

ld = bpy.data.lights.new("ChkSun", 'SUN')
ld.energy = 3.5
sun = bpy.data.objects.new("ChkSun", ld)
scene.collection.objects.link(sun)
sun.rotation_euler = (math.radians(50), 0, math.radians(20))

scene.render.resolution_x = 1000
scene.render.resolution_y = 800
scene.render.filepath = out
bpy.ops.render.render(write_still=True)
print("RENDER ->", out)

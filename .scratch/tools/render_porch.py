"""从店外看入口门 + 门廊地台，快速目视检查几何。
blender -b coffee_shop.blend --python render_porch.py -- 输出路径 [eye|top]
"""
import bpy, sys, math
from mathutils import Vector

args = list(sys.argv[sys.argv.index("--") + 1:]) if "--" in sys.argv else []
out = args[0] if args else r"D:\coffee_shop\.scratch\porch_check.png"
mode = args[1] if len(args) > 1 else "eye"

scene = bpy.context.scene
leaf = bpy.data.objects.get("Door_door_left_LeafL")
hinge = leaf.matrix_world.translation if leaf else Vector((0.03, 8.83, 0))
print("hinge:", tuple(round(v, 3) for v in hinge))

cam_data = bpy.data.cameras.new("ChkCam")
cam = bpy.data.objects.new("ChkCam", cam_data)
scene.collection.objects.link(cam)

if mode == "top":
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 13.0
    cam.location = (hinge.x - 1.4, hinge.y + 1.26, 9.0)
    cam.rotation_euler = (0, 0, 0)
else:
    cam_data.lens = 22
    cam.location = (hinge.x - 4.6, hinge.y + 1.26, 1.62)
    target = Vector((hinge.x, hinge.y + 1.26, 1.45))
    d = target - cam.location
    cam.rotation_euler = (math.atan2(math.hypot(d.x, d.y), d.z), 0, math.atan2(d.y, d.x) - math.pi / 2)
scene.camera = cam

ld = bpy.data.lights.new("ChkSun", 'SUN')
ld.energy = 3.5
sun = bpy.data.objects.new("ChkSun", ld)
scene.collection.objects.link(sun)
sun.rotation_euler = (math.radians(50), 0, math.radians(20))

scene.render.resolution_x = 1200
scene.render.resolution_y = 800
scene.render.filepath = out
bpy.ops.render.render(write_still=True)
print("RENDER ->", out)

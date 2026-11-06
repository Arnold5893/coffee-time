"""目视检查东窗阳光区长桌的桌尾与门口的落脚区。
blender -b coffee_shop.blend --python view_counter_end.py -- <out> [inside|top]
inside: 站在店内西南侧斜看桌尾和门    top: 正上方俯视
"""
import bpy, sys, math
from mathutils import Vector

args = list(sys.argv[sys.argv.index("--") + 1:]) if "--" in sys.argv else []
out = args[0] if args else r"D:\coffee_shop\.scratch\counter_end.png"
mode = args[1] if len(args) > 1 else "inside"

thr = bpy.data.objects.get("Door_entrance_Threshold")
cp = [thr.matrix_world @ Vector(c) for c in thr.bound_box]
door = sum(cp, Vector()) / 8
print("door center:", tuple(round(v, 3) for v in door))

scene = bpy.context.scene
cam_data = bpy.data.cameras.new("ChkCam")
cam = bpy.data.objects.new("ChkCam", cam_data)
scene.collection.objects.link(cam)

if mode == "top":
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 6.5
    cam.location = (11.6, 5.4, 12.0)
    cam.rotation_euler = (0, 0, 0)
    cam_data.clip_end = 40
    # 藏掉吊顶/灯罩等高位物件，否则俯视只能看到格栅
    for o in bpy.data.objects:
        if o.type != 'MESH':
            continue
        zs = [(o.matrix_world @ Vector(c)).z for c in o.bound_box]
        if min(zs) > 2.55:
            o.hide_render = True
elif mode == "end":
    cam_data.lens = 30
    cam.location = (9.6, 5.9, 1.55)
    target = Vector((11.55, 4.05, 0.75))
    d = target - cam.location
    cam.rotation_euler = (math.atan2(math.hypot(d.x, d.y), d.z), 0, math.atan2(d.y, d.x) - math.pi / 2)
else:
    cam_data.lens = 22
    cam.location = (7.2, 6.2, 1.75)
    target = Vector((11.3, 3.3, 1.05))
    d = target - cam.location
    cam.rotation_euler = (math.atan2(math.hypot(d.x, d.y), d.z), 0, math.atan2(d.y, d.x) - math.pi / 2)
scene.camera = cam

ld = bpy.data.lights.new("ChkSun", 'SUN')
ld.energy = 3.0
sun = bpy.data.objects.new("ChkSun", ld)
scene.collection.objects.link(sun)
sun.rotation_euler = (math.radians(55), 0, math.radians(-30))

# 隐藏吊顶，否则俯视看不到室内
scene.render.resolution_x = 1100
scene.render.resolution_y = 850
scene.render.filepath = out
bpy.ops.render.render(write_still=True)
print("RENDER ->", out)

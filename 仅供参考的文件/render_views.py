# 预览渲染: blender -b coffee_shop.blend --python render_views.py -- [视角名...]
import bpy, os, sys, math
from mathutils import Vector

ROOT = r"D:\coffee_shop"
OUT = os.path.join(ROOT, "renders")
os.makedirs(OUT, exist_ok=True)
scene = bpy.context.scene

VIEWS = {
    # 名称: (相机位置, 看向点, 焦距mm, 隐藏吊顶)
    "overview": ((-3.5, -4.0, 14.0), (4.6, 6.0, 0.0), 22, True),
    "top": ((4.7, 6.4, 30.0), (4.7, 6.4, 0.0), 0, True),
    "sun_east": ((5.2, 4.8, 1.5), (9.3, 5.2, 1.6), 16, False),        # 对应照片 1
    "bookwall": ((3.9, 4.6, 1.6), (0.1, 5.2, 1.4), 16, False),        # 对应照片 6
    "entrance": ((3.4, 3.2, 1.6), (0.5, 8.6, 2.0), 16, False),        # 对应照片 7
    "bar": ((4.4, 5.0, 1.6), (4.4, 0.5, 1.4), 18, False),             # 对应照片 18
    "immersive": ((2.6, 8.4, 1.6), (2.6, 12.7, 1.6), 16, False),      # 对应照片 11
    "coffee_machine": ((3.2, 1.6, 1.45), (3.2, 0.4, 1.1), 28, False),  # 对应照片 14
}

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
names = args or list(VIEWS)

cam_data = bpy.data.cameras.new("PreviewCam")
cam = bpy.data.objects.new("PreviewCam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
ceil = bpy.data.collections["Ceiling"]
grid = bpy.data.objects["Ceiling_Grid"]

for n in names:
    loc, tgt, lens, hide = VIEWS[n]
    cam.location = loc
    d = Vector(tgt) - Vector(loc)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    if lens == 0:
        cam_data.type = 'ORTHO'
        cam_data.ortho_scale = 14
        cam.rotation_euler = (0, 0, 0)
    else:
        cam_data.type = 'PERSP'
        cam_data.lens = lens
    ceil.hide_render = hide
    for nm in ("Wall_7", "Wall_8", "Wall_9", "Wall_10", "Wall_12", "Door_door_left", "Entrance_Lightbox",
               "Bar_Lightbox", "Bar_Ceiling", "Wall_6"):
        if nm in bpy.data.objects:
            bpy.data.objects[nm].hide_render = hide
    scene.render.filepath = os.path.join(OUT, n + ".png")
    bpy.ops.render.render(write_still=True)
    print("RENDERED", n)

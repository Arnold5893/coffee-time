import bpy, math, os
from mathutils import Vector
ROOT = r"D:/coffee_shop/仅供参考的文件"
scene = bpy.context.scene
cam_data = bpy.data.cameras.new("TopCam")
cam_data.type = 'ORTHO'
cam = bpy.data.objects.new("TopCam", cam_data)
scene.collection.objects.link(cam)

# 只看吧台区域
cx, cy = 5.2, 1.6
half_w, half_h = 6.2, 2.2
cam_data.ortho_scale = half_w * 2
cam.location = (cx, cy, 12)
cam.rotation_euler = (0, 0, 0)
scene.camera = cam

# 俯视时把高于 2.2m 的东西（吊顶/灯箱/吊架/灯）全隐藏，不然只能看到格栅顶
from mathutils import Vector
for ob in bpy.data.objects:
    if ob.type != 'MESH':
        continue
    pts = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    if min(p.z for p in pts) > 2.2:
        ob.hide_render = True

scene.render.engine = 'BLENDER_EEVEE_NEXT'
scene.render.resolution_x = 1600
scene.render.resolution_y = int(1600 * half_h / half_w)
scene.render.filepath = r"D:/coffee_shop/.scratch/top_bar.png"
scene.view_settings.view_transform = 'Standard'
# 加一点环境光，让平面看得清
w = bpy.data.worlds[0] if bpy.data.worlds else bpy.data.worlds.new("W")
scene.world = w
w.use_nodes = True
w.node_tree.nodes["Background"].inputs["Strength"].default_value = 2.0
bpy.ops.render.render(write_still=True)
print("RENDER DONE")

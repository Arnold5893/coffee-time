"""后场机位预览：给定若 Unity 相机坐标 (X,Y,Z) 与注视点，用 Blender 渲染同一构图。
Blender -> Unity 映射是 (-x, z, -y)，所以反解 blender = (-X, Y, -Z)。
用法（在 bash 里）：
  blender -b "仅供参考的文件/coffee_shop.blend" --python .scratch/tools/cam_preview.py -- A B D
"""
import bpy, sys, math, os
from mathutils import Vector

ROOT = r"D:/coffee_shop/仅供参考的文件"
OUT = r"D:/coffee_shop/.scratch"

# 每个机位：名字 -> (blender 相机位置, blender 注视点)
SHOTS = {
    "A": ((9.60, 1.55, 1.60), (6.40, 0.35, 0.90)),   # 从东端往西扫：木桌→空地→台面/咖啡机
    "B": ((3.00, 1.55, 1.60), (8.60, 0.35, 0.90)),   # 从西端往东扫
    "D": ((9.85, 1.50, 1.85), (6.20, 0.30, 0.85)),   # 东端更靠南、压低
    "E": ((8.90, 1.70, 1.55), (6.30, 0.35, 0.92)),   # 木桌边上往西
    "F": ((9.62, 1.68, 1.92), (6.60, 0.45, 0.80)),   # A 的改良：抬高、往南退、略低头
    "G": ((9.55, 1.60, 1.90), (7.00, 0.50, 0.78)),
    "H": ((9.62, 1.82, 1.90), (6.90, 0.42, 0.80)),
    "P": ((3.72, 1.90, 1.63), (8.32, 0.85, 0.38)),
}

scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE_NEXT'
scene.render.resolution_x = 1280
scene.render.resolution_y = 800
scene.view_settings.view_transform = 'Standard'

w = bpy.data.worlds[0] if bpy.data.worlds else bpy.data.worlds.new("W")
scene.world = w
w.use_nodes = True
w.node_tree.nodes["Background"].inputs["Strength"].default_value = 2.0

cam_data = bpy.data.cameras.new("PrevCam")
cam_data.lens_unit = 'FOV'
cam_data.angle = math.radians(60)          # Unity 默认垂直 FOV 60
cam_data.sensor_fit = 'VERTICAL'
cam = bpy.data.objects.new("PrevCam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["A"]
for key in args:
    pos, tgt = SHOTS[key]
    cam.location = Vector(pos)
    d = Vector(tgt) - Vector(pos)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = os.path.join(OUT, "cam_prev_%s.png" % key)
    bpy.ops.render.render(write_still=True)
    print("PREVIEW", key, scene.render.filepath)

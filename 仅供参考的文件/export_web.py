# 导出网页用 GLB: blender -b coffee_shop.blend --python export_web.py
import bpy, os

ROOT = r"D:\coffee_shop"
os.makedirs(os.path.join(ROOT, "web"), exist_ok=True)

# 网页端只保留吊灯光源，筒灯用环境光代替(性能)
for ob in list(bpy.data.objects):
    if ob.type == 'LIGHT' and ("Downlight" in ob.name):
        bpy.data.objects.remove(ob)
    elif ob.type == 'CAMERA':
        bpy.data.objects.remove(ob)

bpy.ops.export_scene.gltf(
    filepath=os.path.join(ROOT, "web", "coffee_shop.glb"),
    export_format='GLB',
    export_extras=True,
    export_lights=True,
    export_apply=True,
    export_image_format='AUTO',
    export_yup=True,
)
print("EXPORT OK")

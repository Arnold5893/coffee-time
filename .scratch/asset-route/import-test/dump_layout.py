"""导出吧台区域对象的位置与尺寸（俯视图布局分析用）"""
import bpy, json, mathutils, os

bpy.ops.wm.open_mainfile(filepath=r"D:\coffee_shop\仅供参考的文件\coffee_shop.blend")
sc = bpy.context.scene
us = sc.unit_settings.scale_length

data = []
for o in sc.objects:
    if o.type != 'MESH':
        continue
    loc = o.matrix_world.translation
    dims = [d * us for d in o.dimensions]
    data.append({
        "name": o.name,
        "x": round(loc.x * us, 3), "y": round(loc.y * us, 3), "z": round(loc.z * us, 3),
        "w": round(dims[0], 2), "d": round(dims[1], 2), "h": round(dims[2], 2),
    })

out = r"D:\coffee_shop\.scratch\asset-route\import-test\layout_data.json"
with open(out, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=1)
print("OK", len(data))

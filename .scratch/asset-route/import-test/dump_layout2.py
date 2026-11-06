import bpy, json, mathutils

bpy.ops.wm.open_mainfile(filepath=r"D:\coffee_shop\仅供参考的文件\coffee_shop.blend")
sc = bpy.context.scene
us = sc.unit_settings.scale_length

data = []
for o in sc.objects:
    if o.type != 'MESH':
        continue
    corners = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
    xs = [c.x*us for c in corners]; ys = [c.y*us for c in corners]; zs = [c.z*us for c in corners]
    data.append({
        "name": o.name,
        "cx": round(sum(xs)/8, 3), "cy": round(sum(ys)/8, 3), "cz": round(sum(zs)/8, 3),
        "minx": round(min(xs),3), "maxx": round(max(xs),3),
        "miny": round(min(ys),3), "maxy": round(max(ys),3),
        "minz": round(min(zs),3), "maxz": round(max(zs),3),
    })

out = r"D:\coffee_shop\.scratch\asset-route\import-test\layout_data.json"
with open(out, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=1)
print("OK", len(data))

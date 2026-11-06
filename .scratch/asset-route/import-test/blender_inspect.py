"""Blender headless: 检查 coffee_shop.blend 场景结构并导出 FBX 供 Unity 验证"""
import bpy, json, sys, os

blend_path = r"D:\coffee_shop\仅供参考的文件\coffee_shop.blend"
report_path = r"D:\coffee_shop\.scratch\asset-route\import-test\blender_report.json"
fbx_path = r"D:\coffee_shop\.scratch\asset-route\import-test\coffee_shop.fbx"

bpy.ops.wm.open_mainfile(filepath=blend_path)
sc = bpy.context.scene
unit_scale = sc.unit_settings.scale_length

objs = list(sc.objects)
meshes = [o for o in objs if o.type == 'MESH']
total_tris = 0
total_verts = 0
mesh_info = []
for o in meshes:
    m = o.data
    m.calc_loop_triangles()
    tris = len(m.loop_triangles)
    total_tris += tris
    total_verts += len(m.vertices)
    dims = [round(d * unit_scale, 3) for d in o.dimensions]
    mesh_info.append({"name": o.name, "tris": tris, "verts": len(m.vertices),
                      "dims_m": dims, "materials": len(m.materials)})

# 场景总包围盒（米）
import mathutils
mins = [1e9]*3; maxs = [-1e9]*3
for o in meshes:
    for corner in o.bound_box:
        w = o.matrix_world @ mathutils.Vector(corner)
        for i in range(3):
            mins[i] = min(mins[i], w[i]*unit_scale)
            maxs[i] = max(maxs[i], w[i]*unit_scale)
scene_dims = [round(maxs[i]-mins[i], 2) for i in range(3)]

mats = bpy.data.materials
mat_info = [{"name": m.name, "has_nodes": m.use_nodes,
             "textures": len([n for n in (m.node_tree.nodes if m.use_nodes else []) if n.type == 'TEX_IMAGE'])} for m in mats]
images = [{"name": im.name, "filepath": im.filepath, "packed": im.packed_file is not None} for im in bpy.data.images]

mesh_info.sort(key=lambda x: -x["tris"])
report = {
    "unit_scale": unit_scale,
    "object_counts": {t: len([o for o in objs if o.type == t]) for t in set(o.type for o in objs)},
    "mesh_object_count": len(meshes),
    "total_tris": total_tris,
    "total_verts": total_verts,
    "scene_dims_m": scene_dims,
    "materials": mat_info,
    "images": images,
    "top_meshes_by_tris": mesh_info[:15],
    "all_mesh_names": [o.name for o in meshes],
}
os.makedirs(os.path.dirname(report_path), exist_ok=True)
with open(report_path, "w", encoding="utf-8") as f:
    json.dump(report, f, ensure_ascii=False, indent=2)

# 导出 FBX（嵌入纹理不可行时至少带材质槽）
bpy.ops.export_scene.fbx(filepath=fbx_path, path_mode='COPY', embed_textures=True)
print("REPORT_WRITTEN", report_path)
print("FBX_EXPORTED", fbx_path)

import bpy, os
ROOT = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(ROOT, "coffee_shop.fbx")
bpy.ops.export_scene.fbx(
    filepath=out,
    path_mode='COPY',
    embed_textures=True,
    object_types={'MESH', 'EMPTY'},
    use_mesh_modifiers=True,
    mesh_smooth_type='FACE',
)
print("FBX EXPORTED:", out)

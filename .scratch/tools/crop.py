"""用 Blender 的 numpy 裁图（本机没有 PIL/ImageMagick）。
用法: blender -b --python crop.py -- <src> <out> <x0> <y0> <x1> <y1> [scale]
坐标按图片像素，原点左上（与平面图 px/py 一致）。
"""
import sys, os
import bpy, numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
x0, y0, x1, y1 = (int(v) for v in argv[2:6])
scale = int(argv[6]) if len(argv) > 6 else 2

img = bpy.data.images.load(src)
W, H = img.size
print("SRC %dx%d" % (W, H))
buf = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
buf = buf[::-1]                      # Blender 像素是自下而上，翻成自上而下

x0, x1 = max(0, x0), min(W, x1)
y0, y1 = max(0, y0), min(H, y1)
crop = buf[y0:y1, x0:x1]
ch, cw = crop.shape[:2]
crop = np.repeat(np.repeat(crop, scale, axis=0), scale, axis=1)
crop = crop[::-1]                    # 存回 Blender 前翻回去

o = bpy.data.images.new("crop", cw * scale, ch * scale, alpha=True)
o.pixels.foreach_set(np.ascontiguousarray(crop, dtype=np.float32).ravel())
o.filepath_raw = out
o.file_format = 'PNG'
o.save()
print("CROP %s %dx%d -> %dx%d" % (out, cw, ch, cw * scale, ch * scale))

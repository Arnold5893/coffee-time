# blender -b --python crop_img.py -- <src> <x0> <y0> <x1> <y1> <out> [scale]
import sys, os
import numpy as np
import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
src, x0, y0, x1, y1, out = argv[0], *[float(v) for v in argv[1:5]], argv[5]
scale = float(argv[6]) if len(argv) > 6 else 1.0

img = bpy.data.images.load(src)
w, h = img.size
px = np.empty(w * h * 4, np.float32)
img.pixels.foreach_get(px)
px = px.reshape(h, w, 4)          # row 0 = bottom
# 传入的是从左上角量的像素
r0, r1 = int((1 - y1 / h) * h), int((1 - y0 / h) * h)
c0, c1 = int(x0), int(x1)
sub = px[r0:r1, c0:c1].copy()
if scale != 1.0:
    sh, sw = sub.shape[:2]
    nw, nh = int(sw * scale), int(sh * scale)
    yi = (np.arange(nh) / scale).astype(int).clip(0, sh - 1)
    xi = (np.arange(nw) / scale).astype(int).clip(0, sw - 1)
    sub = sub[yi][:, xi]
    sh, sw = nh, nw
o = bpy.data.images.new("crop", sw, sh, alpha=True)
o.pixels.foreach_set(np.ascontiguousarray(sub, dtype=np.float32).ravel())
o.filepath_raw = out
o.file_format = 'PNG'
o.save()
print("saved", out, sw, sh)

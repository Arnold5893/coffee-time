import bpy, numpy as np
img = bpy.data.images.load("D:/coffee_shop/.scratch/tools/ref_black.png")
w, h = img.size
px = np.empty(w * h * 4, np.float32)
img.pixels.foreach_get(px)
px = px.reshape(h, w, 4)[::-1]          # 翻转成 row0 = 顶部
rgb = px[:, :, :3]

white = (rgb.min(axis=2) > 0.75)
dark = (rgb.max(axis=2) < 0.28)

def bbox(mask, label):
    ys, xs = np.where(mask)
    if len(xs) == 0:
        print(label, "none"); return None
    b = (xs.min(), xs.max(), ys.min(), ys.max())
    print("%s  x %d..%d (w=%d)  y %d..%d (h=%d)" % (label, b[0], b[1], b[1]-b[0]+1, b[2], b[3], b[3]-b[2]+1))
    return b

bw = bbox(white, "WHITE")
bd = bbox(dark, "DARK ")
if bw and bd:
    print("white 占暗块宽 %.2f  高 %.2f" % ((bw[1]-bw[0]+1)/(bd[1]-bd[0]+1), (bw[3]-bw[2]+1)/(bd[3]-bd[2]+1)))
    print("white 自身宽高比 %.2f" % ((bw[1]-bw[0]+1)/(bw[3]-bw[2]+1)))

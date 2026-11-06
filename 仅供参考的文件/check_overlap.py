"""检查所有 MESH 对象的世界坐标包围盒，报告相交的对象对（排除地板/墙面等建筑件）。"""
import bpy

IGNORE_PREFIX = ("Floor", "Wall", "Ceiling", "Window", "Blind", "Column", "Door",
                 "Baseboard", "Beam", "Grille", "Light_Track", "SEG", "Room")
IGNORE_COLL = {"Architecture"}


def bbox_world(ob):
    import mathutils
    pts = [ob.matrix_world @ mathutils.Vector(c) for c in ob.bound_box]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))


def overlap(a0, a1, b0, b1):
    # 三轴都有正的重叠量才算（>1cm），返回最小轴重叠量
    ov = [min(a1[i], b1[i]) - max(a0[i], b0[i]) for i in range(3)]
    if all(v > 0.01 for v in ov):
        return min(ov)
    return 0.0


boxes = []
for ob in bpy.data.objects:
    if ob.type != 'MESH':
        continue
    if ob.name.split(".")[0].split("_")[0] in IGNORE_PREFIX:
        continue
    coll_names = {c.name for c in ob.users_collection}
    if coll_names & IGNORE_COLL:
        continue
    b0, b1 = bbox_world(ob)
    # 忽略超大件（地板类）
    if (b1[0]-b0[0]) > 8 or (b1[1]-b0[1]) > 8:
        continue
    boxes.append((ob.name, b0, b1))

print("=== OVERLAP CHECK (%d objects) ===" % len(boxes))
hits = []
for i in range(len(boxes)):
    for j in range(i + 1, len(boxes)):
        n1, a0, a1 = boxes[i]
        n2, b0, b1 = boxes[j]
        # 同名前缀跳过（同一组合家具内部）
        if n1.rsplit("_", 1)[0] == n2.rsplit("_", 1)[0]:
            continue
        ov = overlap(a0, a1, b0, b1)
        if ov > 0.03:  # 重叠超过 3cm 才报告
            hits.append((ov, n1, n2))
hits.sort(reverse=True)
for ov, n1, n2 in hits[:40]:
    print("OV %5.2fm  %s  <->  %s" % (ov, n1, n2))
if not hits:
    print("no overlaps")

"""量出关键家具的世界包围盒，核对过道净宽。用法：blender -b coffee_shop.blend --python verify_layout.py"""
import bpy
from mathutils import Vector


def bb(ob):
    pts = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    return (min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts),
            max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts))


def grp(prefixes):
    out = []
    for ob in bpy.data.objects:
        if ob.type != 'MESH':
            continue
        if any(ob.name.startswith(p) for p in prefixes):
            out.append((ob.name, bb(ob)))
    return out


def union(items):
    return (min(i[1][0] for i in items), min(i[1][1] for i in items), min(i[1][2] for i in items),
            max(i[1][3] for i in items), max(i[1][4] for i in items), max(i[1][5] for i in items))


def show(title, items):
    if not items:
        print("!! 未找到", title)
        return None
    u = union(items)
    print("%-22s x %6.2f..%6.2f  y %6.2f..%6.2f" % (title, u[0], u[3], u[1], u[4]))
    return u


back = show("后操作台", grp(["Back_Counter"]))
bar = show("前柜台", grp(["Bar_Counter"]))
stool = show("吧台凳", grp(["Bar_Stool"]))
r4 = show("圆桌4+凳", grp(["Round_Table_4", "Round4_Chair"]))
r3 = show("圆桌3+凳", grp(["Round_Table_3", "Round3_Chair"]))
r2 = show("圆桌2+凳", grp(["Round_Table_2", "Round2_Chair"]))
sofa3 = show("多人桌3沙发", grp(["Group_Sofa_3"]))
sofa2 = show("多人桌2沙发", grp(["Group_Sofa_2"]))
conf = show("会议桌+椅", grp(["Meeting_Table", "Meeting_Chair"]))
sun = show("阳光区东窗凳", grp(["Sun_Stool_E"]))
print()
print("吧台内通道           %.2f m" % (bar[1] - back[4]))
print("吧凳外缘→圆桌4凳外缘  %.2f m" % (r4[1] - stool[4]))
print("吧凳外缘→圆桌4桌边    %.2f m" % (r4[1] + 0.0 - stool[4]))  # 已被椅子挡住，见上
print("多人桌沙发→圆桌西侧凳  %.2f m" % (r4[0] - sofa3[3]))
print("圆桌东侧凳→东窗吧凳   %.2f m" % (sun[0] - r3[3]))
print("会议桌东椅→多人桌沙发  %.2f m" % (sofa3[0] - conf[3]))
print()
print("=== 圆桌组纵向净距 ===")
print("table2 y %.2f..%.2f / table3 %.2f..%.2f / table4 %.2f..%.2f" %
      (r2[1], r2[4], r3[1], r3[4], r4[1], r4[4]))
print("圆桌2 北缘 %.2f (斜窗在该 x 处约 %.2f)" % (r2[4], 15.48 - 0.9839 * ((r2[0] + r2[3]) / 2)))

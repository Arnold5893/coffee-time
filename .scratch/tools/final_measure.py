import bpy
from mathutils import Vector
def bb(n):
    o = bpy.data.objects[n]; pts=[o.matrix_world @ Vector(c) for c in o.bound_box]
    return (min(p.x for p in pts), min(p.y for p in pts), max(p.x for p in pts), max(p.y for p in pts))
f = bb("Floor")
print("ROOM  x %.2f..%.2f (%.2f m)  y %.2f..%.2f (%.2f m)  面积约 %.0f m2" % (f[0],f[2],f[2]-f[0],f[1],f[3],f[3]-f[1],(f[2]-f[0])*(f[3]-f[1])*0.93))
def u(names):
    xs=[];ys=[]
    for n in names:
        b=bb(n); xs+= [b[0],b[2]]; ys+=[b[1],b[3]]
    return min(xs),min(ys),max(xs),max(ys)
back=u(["Back_Counter"]); bar=u(["Bar_Counter"]); st=u(["Bar_Stool_1","Bar_Stool_2","Bar_Stool_3"])
r4=u(["Round_Table_4"]+["Round4_Chair_%d"%i for i in range(3)])
r3=u(["Round_Table_3"]+["Round3_Chair_%d"%i for i in range(2)])
r1=u(["Round_Table_1"]+["Round1_Chair_%d"%i for i in range(4)])
s3=u(["Group_Sofa_3_-1","Group_Sofa_3_1"]); s2=u(["Group_Sofa_2_-1","Group_Sofa_2_1"])
print("GAP  吧台内通道 %.2f | 吧凳→圆桌4凳 %.2f | 圆桌4西凳→多人桌3沙发 %.2f | 圆桌3西凳→多人桌2沙发 %.2f | 圆桌1南凳→吧台凳 %.2f" %
      (bar[1]-back[3], r4[1]-st[3], r4[0]-s3[2], r3[0]-s2[2], r1[1]-st[3]))

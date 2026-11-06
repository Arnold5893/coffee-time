import bpy, numpy as np
img = bpy.data.images.load("D:/coffee_shop/.scratch/vertical-slice/acceptance/02/setup_screenshot_playerview.png")
w,h = img.size
px = np.empty(w*h*4, np.float32); img.pixels.foreach_get(px)
px = px.reshape(h,w,4)[::-1,:,:3]
def avg(x0,y0,x1,y1,label):
    r = px[y0:y1, x0:x1]
    print("%-14s mean=%.3f %.3f %.3f" % (label, r[:,:,0].mean(), r[:,:,1].mean(), r[:,:,2].mean()))
avg(510,290,560,410,"player body")
avg(500,250,565,290,"player head")
avg(700,380,780,430,"round table")
avg(690,300,730,345,"bar stool seat")
avg(300,470,420,560,"loveseat back")

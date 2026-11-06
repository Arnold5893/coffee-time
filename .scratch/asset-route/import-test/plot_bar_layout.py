"""吧台布局核对图 v2：修正全店视图范围，标注实测通道宽度"""
import json
import matplotlib.pyplot as plt
import matplotlib.patches as patches

plt.rcParams['font.sans-serif'] = ['Microsoft YaHei', 'SimHei']
plt.rcParams['axes.unicode_minus'] = False

data = json.load(open(r'D:\coffee_shop\.scratch\asset-route\import-test\layout_data.json', encoding='utf-8'))

def draw_footprint(ax, o, color, alpha=0.9, lw=0.6):
    ax.add_patch(patches.Rectangle((o['minx'], o['miny']), o['maxx']-o['minx'], o['maxy']-o['miny'],
                                   facecolor=color, edgecolor='#343a40', lw=lw, alpha=alpha))

fig, axes = plt.subplots(1, 2, figsize=(17, 9), dpi=140, gridspec_kw={'width_ratios': [1, 1.25]})

# ---- 左：全店俯视图 ----
ax = axes[0]
skip = ('Pendant', 'Painting', 'Plant', 'Ceiling', 'AC_', 'Brass', 'Lightbox', 'Logo', 'BarDownlight', 'Downlight')
for o in data:
    n = o['name']
    if n.startswith(skip):
        continue
    if n == 'Floor':
        draw_footprint(ax, o, '#f1f3f5', lw=1.2); continue
    if n.startswith('Wall'):
        draw_footprint(ax, o, '#868e96'); continue
    if n.startswith('Door'):
        draw_footprint(ax, o, '#e8590c'); continue
    if any(k in n for k in ('Bar_Counter', 'Back_Counter', 'Cashier', 'Coffee_Machine', 'Grinder', 'Tamper')):
        draw_footprint(ax, o, '#e67700'); continue
    if 'Table' in n:
        draw_footprint(ax, o, '#4dabf7'); continue
    if any(k in n for k in ('Sofa', 'Chair', 'Stool')):
        draw_footprint(ax, o, '#94d82d'); continue
    if 'Bookshelf' in n or 'Divider' in n or 'Rack' in n:
        draw_footprint(ax, o, '#9775fa'); continue
    draw_footprint(ax, o, '#ced4da')

ax.annotate('主入口\n(右下侧门)', (8.6, 0.9), fontsize=9, ha='center', color='#e8590c', fontweight='bold')
ax.annotate('吧台区', (4.5, 1.5), fontsize=10, ha='center', color='#e67700', fontweight='bold')
ax.set_title('全店俯视图（橙色=吧台区，蓝=桌，绿=座椅，紫=书架）', fontsize=12, fontweight='bold')
ax.set_xlim(-0.5, 9.8); ax.set_ylim(-0.5, 13.2)
ax.set_aspect('equal'); ax.grid(alpha=0.3)
ax.set_xlabel('X (m)'); ax.set_ylabel('Y (m)')

# ---- 右：吧台区域放大 ----
ax = axes[1]
bar_keys = ('Bar_Counter', 'Back_Counter', 'Cashier', 'Coffee_Machine', 'Grinder', 'Tamper',
            'Bar_Rack', 'Bar_Stool', 'Back_Counter_Misc', 'Wall_6', 'Wall_7', 'Wall_8', 'Door')
for o in data:
    n = o['name']
    if not any(n.startswith(k) for k in bar_keys):
        continue
    if n.startswith('Wall'):
        draw_footprint(ax, o, '#868e96')
    elif n.startswith('Door'):
        draw_footprint(ax, o, '#e8590c')
    elif 'Stool' in n:
        draw_footprint(ax, o, '#94d82d')
    else:
        draw_footprint(ax, o, '#e67700')

labels = [
    ('Cashier', '① 点单/收银\n(前柜台面)', 0, 0.42),
    ('Coffee_Machine', '② 浓缩咖啡机', 0.35, -0.62),
    ('Grinder_1', '③ 磨豆×2+压粉\n(后柜台面)', -0.75, -0.62),
    ('Back_Counter_Misc', '后柜杂物区\n(1.8m, 可重整)', 0.4, -0.62),
]
for name, label, dx, dy in labels:
    o = next(x for x in data if x['name'] == name)
    ax.annotate(label, (o['cx']+dx, (o['miny'] if dy < 0 else o['maxy'])+dy),
                fontsize=9, fontweight='bold', color='#c2255c', ha='center',
                bbox=dict(boxstyle='round,pad=0.25', fc='#fff0f6', ec='#c2255c', lw=0.8))

# 空位标注
ax.add_patch(patches.Rectangle((5.9, 0.12), 1.8, 0.6, facecolor='#d3f9d8', edgecolor='#2b8a3e', lw=1.2, linestyle='--'))
ax.annotate('后柜东段空闲台面 ~1.8m\n④茶罐架 ⑤注水/茶壶位 ⑧托盘', (6.8, -0.55),
            fontsize=9, fontweight='bold', color='#2b8a3e', ha='center',
            bbox=dict(boxstyle='round,pad=0.25', fc='#ebfbee', ec='#2b8a3e', lw=0.8))

# 通道问题标注
ax.add_patch(patches.FancyArrow(4.6, 0.78, 0, 0.26, width=0.03, color='#e03131', head_width=0.12))
ax.add_patch(patches.FancyArrow(4.6, 1.04, 0, -0.26, width=0.03, color='#e03131', head_width=0.12))
ax.annotate('⚠ 前后柜台通道仅 0.32m\n玩家无法进入吧台内侧', (4.75, 0.9), fontsize=10, fontweight='bold',
            color='#e03131', ha='left',
            bbox=dict(boxstyle='round,pad=0.3', fc='#fff5f5', ec='#e03131', lw=1.2))

ax.annotate('主入口', (8.6, 1.15), fontsize=9, ha='center', color='#e8590c', fontweight='bold')
ax.add_patch(patches.FancyArrow(8.6, 1.45, -4.8, 0.6, width=0.05, color='#1971c2', alpha=0.55,
                                linestyle='--', head_width=0.18))
ax.annotate('顾客路径: 进门 → 前台点单', (6.0, 2.6), fontsize=9, color='#1971c2', ha='center')

ax.set_title('吧台区实测布局（右下为东，邻主入口）', fontsize=12, fontweight='bold')
ax.set_xlim(0.4, 9.8); ax.set_ylim(-1.1, 3.2)
ax.set_aspect('equal'); ax.grid(alpha=0.3)
ax.set_xlabel('X (m)'); ax.set_ylabel('Y (m)')

plt.tight_layout()
out = r'D:\coffee_shop\.scratch\asset-route\import-test\bar_layout_review.png'
plt.savefig(out, bbox_inches='tight', facecolor='white')
print('saved')

"""绘制 wayfinder 候选目标依赖关系图 (DAG)"""
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
from matplotlib.patches import FancyArrowPatch, FancyBboxPatch

plt.rcParams['font.sans-serif'] = ['Microsoft YaHei', 'SimHei']
plt.rcParams['axes.unicode_minus'] = False

# 节点: id -> (label, x, y, color, is_first)
nodes = {
    'G1': ('G1 资产与场景路线定案\nBlender导入验证·资产选型·灰盒方案', 1.0, 4.0, '#ff6b6b'),
    'G2': ('G2 吧台近景交互方案\n五原语UI·镜头收紧·操作台布局', 4.0, 4.0, '#ffd43b'),
    'G3': ('G3 竖切实施Spec\n进店→拿铁线→评价→交付→单客闭环', 2.5, 2.8, '#4dabf7'),
    'G4': ('G4 竖切实现\n(to-tickets 执行域)', 2.5, 1.6, '#69db7c'),
    'G5': ('G5 NPC与对话内容方案\n3核心NPC设定·台词库', 5.5, 2.8, '#ffd43b'),
    'G6': ('G6 音频方案\n免版权曲目·音效来源', 7.5, 2.8, '#ffd43b'),
    'G7': ('G7 打烊与通关体验方案\n文案池·演出细节', 7.5, 1.6, '#dee2e6'),
    'G8': ('G8 性能分档方案\nAndroid质量等级(需真机数据)', 5.5, 1.6, '#ffd43b'),
    'G9': ('G9 制作系统全量Spec\n茶线/无咖啡线/评价细化', 2.5, 0.4, '#ced4da'),
}

edges = [
    ('G1', 'G3'), ('G2', 'G3'),
    ('G3', 'G4'),
    ('G4', 'G8'), ('G4', 'G9'),
    ('G5', 'G9'),
    ('G6', 'G7'),
]

fig, ax = plt.subplots(figsize=(13, 7.5), dpi=150)

for src, dst in edges:
    x1, y1 = nodes[src][1], nodes[src][2]
    x2, y2 = nodes[dst][1], nodes[dst][2]
    ax.annotate('', xy=(x2, y2 + 0.28), xytext=(x1, y1 - 0.28),
                arrowprops=dict(arrowstyle='-|>', color='#868e96', lw=2))

for gid, (label, x, y, color) in nodes.items():
    edge = '#c92a2a' if gid == 'G1' else '#495057'
    lw = 3 if gid == 'G1' else 1.5
    box = FancyBboxPatch((x - 1.05, y - 0.28), 2.1, 0.56,
                         boxstyle='round,pad=0.08', facecolor=color,
                         edgecolor=edge, linewidth=lw, zorder=3)
    ax.add_patch(box)
    ax.text(x, y, label, ha='center', va='center', fontsize=9.5, zorder=4,
            fontweight='bold' if gid == 'G1' else 'normal')

ax.text(1.0, 4.62, '★ 建议的第一个 wayfinder 目标', ha='center', fontsize=11,
        color='#c92a2a', fontweight='bold')

legend = [
    mpatches.Patch(color='#ff6b6b', label='第一个目标（阻塞面最大）'),
    mpatches.Patch(color='#ffd43b', label='决策域（wayfinder 地图）'),
    mpatches.Patch(color='#4dabf7', label='规格（to-spec 产物）'),
    mpatches.Patch(color='#69db7c', label='执行域（to-tickets，不用 wayfinder）'),
    mpatches.Patch(color='#ced4da', label='后续阶段'),
]
ax.legend(handles=legend, loc='lower right', fontsize=9, framealpha=0.95)

ax.set_xlim(-0.5, 9.0)
ax.set_ylim(-0.2, 5.2)
ax.axis('off')
ax.set_title('咖啡店 3D 游戏 · wayfinder 候选目标依赖图（DAG）', fontsize=14, fontweight='bold', pad=12)

plt.tight_layout()
plt.savefig(r'D:\coffee_shop\docs\wayfinder-dag.png', bbox_inches='tight',
            facecolor='white')
print('saved')

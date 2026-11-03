# 素晴 COFFEE SHOP · 3D 复刻

## 文件
| 文件 | 作用 |
|---|---|
| `build_shop.py` | 建模脚本（墙体、家具、吧台、灯光、贴图全部由它生成） |
| `coffee_shop.blend` | 生成的 Blender 文件（可直接用 Blender 4.3 打开） |
| `render_views.py` | 渲染预览图到 `renders/`（视角对应店内照片） |
| `export_web.py` | 导出网页用的 `web/coffee_shop.glb` |
| `web/index.html` | 网页查看器（俯瞰 / 漫游 / 点击看介绍 + 菜单） |
| `refs/`, `textures/` | 参考照片 / 生成及裁剪出的贴图 |

## 改完参数后重新生成
```
D:\blender\blender.exe -b --python build_shop.py
D:\blender\blender.exe -b coffee_shop.blend --python render_views.py
D:\blender\blender.exe -b coffee_shop.blend --python export_web.py
```
比例尺在 `build_shop.py` 顶部：`S = 1.5 / 147`（平面图 1 大格 = 1.5 m）。量到真实尺寸后只需改这一行。

## 本地打开网页
```
python -m http.server 8765 -d web
```
然后浏览器访问 http://localhost:8765 （直接双击 html 会因浏览器安全限制加载不了模型）。

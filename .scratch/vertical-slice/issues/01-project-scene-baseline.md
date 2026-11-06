# 01: 工程与场景基线

**What to build:** Unity 6000.3.28f1 URP 工程建在仓库内（Unity 标准 .gitignore）；按 asset-route 02 票管线导入场景 FBX、Extract Textures、转 URP；按 06 票结论将前柜（含收银、Bar_Rack、吧凳、吧台吊灯）北移约 0.9m 使前后柜通道 ≥1.2m。完成后在编辑器中打开场景，全店正确显示、材质颜色正常、通道实测 ≥1.2m。

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] Unity URP 工程在仓库内可打开，批处理构建脚本可用（注意先 export PROGRAMDATA）
- [x] 场景导入后 120 个对象结构完整，包围盒约 10.2×4.5×13.0 m
- [x] 纹理经 Extract Textures 绑定，材质已转 URP，颜色与参考一致
- [x] 前柜北移后通道实测 ≥1.2m，吧凳/灯架随移无穿插

**结果（2026-11-06）：**
- 工程建于 `game/`，URP 17 管线资产 + 持久化 Renderer 子资产 + ACES 全局 Volume
- 通道轴实测为 **-Z**（修正 06 票"Blender +Y = Unity +Z"的假设；FBX 导入后实为 -Z）。原通道 0.32m，前柜组 6 对象整体 -Z 移 0.88m，实测通道 1.20m
- 材质：FBX 在 URP 激活后导入，天然生成 URP/Lit（无需转换器；官方批处理转换器在本版本有 Base2DMaterialUpgrader 无默认构造的 bug，已绕开）
- 截图管线：URP 不支持 Camera.Render()，改走 Play 模式 RenderTexture（必须 ARGBHalf，否则 URP 跳过后处理）；验收图存于 `.scratch/vertical-slice/acceptance/01/`
- `BuildScript.BuildWindows` 批处理构建通过（`game/Builds/Windows/CoffeeTime.exe`）
- 已知留待后续：验证用基线光照偏亮（ambient 0.25 + 直射 0.8），三时段氛围光照属后续票据；`downlight` 材质自发光 30x HDR，正式灯光方案时统一处理

**2026-11-06 修正（fix-03，场景几何）**

- 前柜北移改为**固化进 Blender 源文件**（`build_shop.py` 的 `BAR_SHIFT`），Unity `ProjectSetup` 的自动平移降级为幂等守卫（已达标时 shift=0）。原因：位移只存在于 Unity 脚本时，Blender 视图与渲染结果不一致，圆桌被吧凳顶穿却查不出来。
- 店面整体放大 25%（`SCALE`，约 11.6×15.9m / 184㎡）。原比例下"圆桌组 1.2m 过道 / 阳光区 12 个斜窗吧凳 / 平面图座位数"三者互斥，无解。
- 验收图：`.scratch/vertical-slice/acceptance/fix-03/`

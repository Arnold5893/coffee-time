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

**2026-11-06 修正（fix-04，家具与设备）**

- **矮凳统一为店内实际那把简式木椅**：`armchair()` 重写为"四条方腿 + 后腿延伸成靠背立柱 + 圆角座板/望板 + 布艺坐垫 + 微弯曲木靠背板 + 四边横档"。原曲木桶形围板＋平板扶手版式结构过度复杂，且与实拍不符。用户明确「不止一种矮凳，不需要和实图一模一样，只要符合逻辑、是正常能使用的凳子」。
- **删除打印机**：上一轮按假设新增的 `Printer`（黑色机身＋白色出纸口）实物不存在，已从 `build_shop.py` 移除。判定依据：参考裁图中白色区域宽高比 3.13、占暗块宽 83%、位于上三分之一，与出纸口（0.30×0.10）一致，与收银机 LCD（0.27×0.19）不符 —— 收银机保留。
- **删除斜窗吧凳旁的天堂鸟**（原 `PLANTS` 的 `(608,500)`）：挡在 `Sun_Stool_D2/D3` 前，实物没有。
- **顺带修正**：斜窗发财树由 `py=325` 移至 `py=292`，消除与 `Sun_Stool_D1` 的 0.36m 交叠。
- 实测：104 件家具出界 0；吧台内通道 1.20m、吧凳→圆桌 4 凳 1.27m、圆桌西凳→多人桌沙发 1.07m（椅子变小后净距略增）。
- 验收图：`.scratch/vertical-slice/acceptance/fix-04/`

**2026-11-06 修正（fix-05，再次核对实物）**

- 删除 **收银机**（`Cashier`）：上一轮把用户裁图里的黑色设备认成打印机（按白色区域宽高比 3.13 判定），实际那台是收银机。用户明确「店里没有收银机，点单在吧台口头完成」。吧台 info 从「点单收银」改为「点单位」。
- 打印机维持删除：设计方案全文未提打印，属上一轮按假设虚构，不再恢复。
- 删除**书墙前的发财树**（原 `PLANTS` 的 `(224,976)`）：实景没有。
- 现状：102 件家具，出界 0；`PLANTS` 剩 5 盆（2 桌间小盆栽、斜窗发财树、沉浸区龟背竹、东侧龟背竹）。
- 验收图：`.scratch/vertical-slice/acceptance/fix-05/`

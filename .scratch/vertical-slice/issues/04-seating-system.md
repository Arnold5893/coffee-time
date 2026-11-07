# 04: 座位系统

**What to build:** 座位配置化数据（编号/区域/位置/朝向/桌面点位/座椅类型，首切至少覆盖吧台凳与客人用桌位）；座位空闲/占用状态；玩家靠近空座出现坐下入口，坐下/起身动画与状态转换；起身后释放座位。

**Blocked by:** 02 玩家移动与镜头

**Status:** resolved

- [x] 座位数据来自配置，不硬编码（FR-SEAT-01）
- [x] 玩家可坐下/起身，状态正确转换（FR-SEAT-02）
- [x] 已占用座位不可被重复使用
- [x] Edit Mode 测试：占用/释放、重复占用拒绝
- [x] 沙发类（一件家具两个座位）也接进座位表（2026-11-07 补完）

**结果（2026-11-06）：**

**A. 配置只写语义，不写坐标（`Assets/Resources/Config/seats.json`）**

33 个座位分 4 个区域：吧台 3、阳光区 12、圆桌 12、会议桌 6。每条只有这些字段：

```json
{ "id": "bar-1", "zone": "bar", "kind": "barStool", "anchor": "Bar_Stool_1",
  "faceTarget": "Bar_Counter", "seatHeight": 0.66, "surfaceHeight": 1.05 }
```

- `anchor` 绑场景里的椅子对象，`faceTarget` 说"朝哪张桌子"，`faceDir` 用在没有独立桌面对象的座位（阳光区窗户和长桌是同一面墙）。
- **配置里没有一个世界坐标**。坐下点、朝向、站立点、桌面点位全部由 `SeatingManager` 在运行时从场景几何解析 —— 家具在 `build_shop.py` 里挪位后重跑 `ProjectSetup`，座位自动跟随，JSON 不用改。
- `faceDir` 用方向名（N/S/E/W/NE…），`seatHeight` 留 0 时按座椅类型取默认（吧凳 0.66 / 软包椅 0.45）。

**B. 三种几何的求解方式**

| 量 | 算法 | 为什么这么做 |
| --- | --- | --- |
| 朝向 | `faceTarget` 包围盒的 `ClosestPoint` 减座位点，水平化 | 不用"指向目标中心"：吧台、长桌是长条形，朝中心会算歪 |
| 坐下点 | 椅子渲染包围盒中心的 XZ，y 取包围盒底 | — |
| 站立点 | 座位正后方 0.55m 起，按 0.82/0.66/0.52 逐级缩，每个距离再试 ±22°/±45°/±68°/±90° | 店里不少椅子背后是墙或邻座，只沿正后方硬退会退到人贴在椅子上 |
| 桌面点位 | 朝桌前打一条**向下射线**，命中什么就放什么上面 | 不取包围盒顶面 —— 吧台顶上还立着陈列柜（顶到 1.62m），算出来杯子会浮空 |

`surfaceHeight` 显式给了就不打射线：吧台被陈列柜挡、阳光区长桌和窗户是同一网格，这两种射线探不准。

**C. 占用状态机（`SeatRegistry.cs`，纯 C# 不依赖 UnityEngine）**

- `SeatState.TryOccupy(who)`：空串/null 占用者拒绝；已占用一律 false（这就是"客人不抢已占用座位"的底层保障）。
- `SeatState.Release(who)`：只有当前占用者本人能放；本来就是空的返回 false，不假装成功。
- `SeatRegistry.Add()` 遇到重复 id 直接抛异常 —— 配置写错了应当当场炸，而不是悄悄覆盖。

**D. 坐下 / 起身（`SeatInteraction.cs`）**

- 靠近空座 1.30m 出提示，左键坐下；`blend` 到 0.75 以上再按左键才起身，避免一次点击既坐又起。
- 过渡 0.50s 坐下 / 0.35s 起身：位置走 smoothstep 插值，视觉体下沉到"头顶 ≈ 座面 + 0.80m"，腿胶囊同步收短压进身体，上身前倾 7°。
- 期间 `CharacterController` 与 `PlayerMotor` 都关掉（否则重力会把人往下拖），起身结束时恢复并把位置钉在站立点。
- 姿态是占位实现，12 票换骨骼模型后整段替换成 Animator。

**E. 提示 UI**

世界空间 Canvas 挂在玩家下（屏幕空间 Overlay 进不了 `RenderTexture`，验收截图里会看不见）。摆在座位上方 `座面 + 1.15m` —— 站到座位后方时不会被自己脑袋挡住。

字体踩了两个坑：`Text` 忘了设 `text`（空白）、`LegacyRuntime.ttf` 不含中文字形（还是空白）。现在用 `Font.CreateDynamicFontFromOSFont` 优先取系统中文（实测落到 Microsoft YaHei），取不到才退回内置字体。

**F. 连带修掉的三个既有问题**

这一票暴露了场景碰撞体的一个老 bug，不修则阳光区 6 个座位坐不到：

1. **斜墙的轴对齐包围盒把阳光区封了**。东北角斜窗墙的网格顶点是世界坐标、transform 无旋转，按整体 AABB 烘出来的碰撞体是 `4.49 × 4.43` 的一整片（实际墙体只有 5.5m 长、0.6m 厚），把半个阳光区焊死。现在对"横向两轴都 >1.8m 且立着"的网格改用**顶点 PCA 主轴**烘旋转 OBB，结果 `0.68 × 5.75`，沿墙轴 `(0.71, 0, -0.70)`。
2. **吧台座位的桌面点取到陈列柜顶**（1.62m）→ 改为射线探桌面，吧台改用配置的台面高 1.05m。
3. **阳光区两排座位的朝向配反了**（东北角写成 NE、东窗写成 E，实际窗户都在反侧）→ 改成 SW / W。

**G. 测试基建**

之前工程连 Test Framework 都没装，也没有任何 asmdef（所有脚本混在 `Assembly-CSharp`，测试程序集根本无法引用）。这一票补上：

- `manifest.json` 加 `com.unity.test-framework 1.6.0`、`com.unity.ugui 2.0.0`（都是 Unity 内置包，离线可用）。
- 程序集三分：`CoffeeShop.Runtime` / `CoffeeShop.Editor` / `CoffeeShop.Tests.EditMode`。运行时程序集要显式引用 `Unity.RenderPipelines.{Core,Universal}.Runtime` 与 `UnityEngine.UI`。

**H. 验收**

Edit Mode 20 个用例全过：

```
total="20" passed="20" failed="0"
```

覆盖：占用/释放/重复占用拒绝/非法占用者/他人释放被拒/重复 id 抛异常/全量释放、配置合法性与 id 唯一、首切区域覆盖、座椅类型与方向名解析。

流程测试（`PlayTest.RunSeat` → `game/seat_flow.txt`，`failures=0`）：

```
registry: seats=33 missing=0 errors=0 bar=3 meeting=6 round=12 sun=12
geometry: badFacing=0 badSurface=0 badStand=0 badSeat=0 cramped=2
  ok   按下交互后进入坐姿 / 坐下过渡走完 / 座位被标记为占用
  ok   坐下期间角色控制器关闭（不会被重力拖走）
  ok   别的角色抢不到已占用的座位 / 玩家不能在同一张座位上再坐一次
  ok   起身后座位被释放 / 角色控制器恢复
  ok   释放后的座位可以再次占用
  ok   圆桌椅子也能坐下（座面高度与吧台凳不同）
SEAT RESULT: PASS
```

**回归**：02 票 `probes=48 deadEnds=0 outOfRoom=0` PASS；03 票 12 项全过 PASS（碰撞体改动没有破坏移动与进门）。

**I. 验收图**

- `acceptance/04/setup_screenshot_seat_prompt.png`：玩家站在吧台凳旁，座位上方浮出「坐下」，背后是吧台与咖啡机。
- `acceptance/04/setup_screenshot_seated.png`：坐下后（`blend=1.00`、座位 `occupied=True`），人落在吧台前。

**遗留**

- **sun-5 / sun-6 两个座位只能原地起立**：它们背后紧贴圆桌 2 的椅子（阻挡体 `Round2_Chair_2`、`Round_Table_2`），0.55m 内所有角度都被占满。测试里单独计为 `cramped`（上限 3），不是当失败处理。05 票让客人落座时要能识别这类座位，或者后续微调那块布局。
- 沙发类座位（`Immersive_Sofa_*`、`Group_Sofa_*`、`Loveseat`）还没接进配置 —— 一张沙发两个座，位置要沿宽度切分，首切先用 33 把椅子够用。
- 坐下姿态是胶囊占位体（整体下沉 + 收腿），不是真骨骼动画，等 12 票。
- 提示文案暂时写死「坐下」，没做键位提示；07 票做吧台 UI 时统一。
- `game/editmode_results.xml`、`game/*.txt` 目前会生成在工程根，尚未纳入 ignore（用户要求这轮不动日志）。

---

## 补完：双人沙发一椅两座（2026-11-07）

04 收尾时留了个尾巴——店内 10 张双人沙发（`Immersive_Sofa_*` / `Group_Sofa_*`）各是**一件家具两个座位**，
但配置只按"一个 anchor 一个座位"建，所以这 20 个位置一直没进座位表。本轮补上，座位 33 → **53**。

**做法**：新增字段 `lateralOffset`（沿坐者左右偏移，米），同一个 anchor 写两条记录，取 ∓0.30
正好落在两块坐垫中心（`loveseat()` 里坐垫在 `(k-0.5)*0.6`）。左右方向用**已解算出的朝向**推：
`lateral = (facing.z, 0, -facing.x)`，不读对象自身 X 轴（FBX 根节点带 -90°X，局部轴与世界轴对不上）。
校验加了 ±0.65m 上限。

**两个坑**：

1. 20 个沙发位一开始**全部原地起立**：站位默认"正后方 0.55m"，减去探测胶囊半径 0.28 只剩 0.27m，
   而沙发背框在 -0.32m，被自己挡住。沙发更深，默认站位改 **0.66m**（仍守住"不超出座位 0.70m"）。
2. "离 3m 远不提示"那条用例原来写死"吧凳正后方 3m"，沙发位进来后该点正好贴着别的座位 →
   改成绕一圈找一处离**所有**座位都够远的空地再断言。

**夹缝座改成白名单**（`sun-5`/`sun-6`/`sofa-i2-r-1`/`sofa-i2-r-2`）：这 4 个背后确实紧贴家具/墙，
属真实布局；但别处一旦冒出新的夹缝座，断言仍会红——不是简单放开上限。

**验收**：`seats=53 missing=0`；`SEAT RESULT: PASS`（`bad*=0`，夹缝 4 个全在白名单）；
EditMode **61/61**；02 回归 `probes=48 deadEnds=0`；03 回归 12 项 `failures=0`。
归档 `.scratch/vertical-slice/acceptance/04-sofa/`。

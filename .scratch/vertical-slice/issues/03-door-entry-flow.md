# 03: 进店流程

**What to build:** 开局玩家在店门外（门廊区）。走到门前点击门 → 开门动作＋门铃音效（占位）→ 玩家自己走进店内 → 门自动关闭。进店后无任何引导、箭头、任务提示。进入流程状态机：门外→点击门→门开→玩家进入→门关。

**Blocked by:** 02 玩家移动与镜头

**Status:** resolved

- [x] 门外有出生点与可站立的门廊区
- [x] 点击门触发开门动作与门铃（FR-INIT-05）
- [x] 进店由玩家移动走完，不是过场动画
- [x] 进店后零引导（FR-INIT-06）
- [x] 门状态机转换正确，重复开关无异常

**结果（2026-11-06）：**

**A. 门外地台（Blender 源，`build_shop.py`）**

- 新增 `Porch` 对象：入口墙段外挑 **2.80m 深 × 4.93m 宽**（门洞两侧各留 1.20m），顶面与室内地面齐平（`z=0`），厚 0.15m。世界包围盒 `x[-2.672, 0.128] y[7.627, 12.552] z[-0.15, 0]`，与地板多边形边界 `x=0.128` 无缝相接。
- 只做地台，不做外立面（用户拍板："只加一块地台，不做外立面"）。材质 `M_PORCH`（`porch_paving`，0.44 灰、粗糙 0.85）。
- 地台边缘没有可见护栏，但**三面加了不可见碰撞围挡**（见 C），玩家不会从平台边缘掉进空场景。

**B. 门拆成可动件（Blender 源）**

- 原 `Door_door_left` 是一个整体网格（侧立框 + 上亮子 + 两扇门），没法单独转。
- 拆成 3 个对象：
  - `Door_door_left`（门框：侧立框 + 上亮子玻璃 + 上横档）
  - `Door_door_left_LeafL` / `_LeafR`（两扇门，**对象原点放在铰链轴上**，局部 +X 由铰链指向自由边，局部 Z 朝上）
- 每扇门补了铝竖梃、上横档，木质长拉手改成内外各一根。
- 侧门 `Door_door_right` 按同样方式拆（现在也是可开的双开扇）。
- 对象数 149 → 154（+4 门扇 +1 地台）。

**C. 碰撞体与分层（`ProjectSetup.cs`）**

- **门框不烘碰撞体**：门框是"环形"网格（两侧立框 + 上方亮子），按整体包围盒烘一个 box 会把整个门洞封死 —— 这是本轮最直接的卡点，第一次跑测试时玩家走到门前 0.38m 就再也推不动了。
- 门扇各自烘包围盒碰撞体，关门时挡人（实测玩家被顶在门外 0.38m 处）。
- 新增自定义层 **`Boundary`（层 8）**，由 `EnsureLayer()` 写进 `ProjectSettings/TagManager.asset`。门外三面围挡挂在这一层：
  - 角色会撞上（CharacterController 不看层）；
  - **交互射线和镜头避让都跳过它**（`~LayerMask.GetMask("Boundary")`），否则一道看不见的墙会把"点门"和第三人称镜头全挡住。

**D. 玩家与出生点**

- 出生点从店内改到**门外地台**：`doorCenter - 门法线 × 1.55m`，朝门站（`facing = 门法线`），实测 `(1.52, -10.09)`。
- 门法线**不能用"整体包围盒中心 − 门心"**算：店面是 L 形多边形，那个方向是斜的（第一版算出来 `(-0.91, 0.40)`，门斜着开、地台围挡也斜着摆）。改成"垂直于两扇门铰链连线 + 用『往内一步是否还在 Floor 的包围盒里』定正负"。
- 店内另设 `InteriorAnchor`：从店面中心向外螺旋找一块不压家具的空位（`r=0.6m, ang=90°`），供 02 票的跟随镜头验收图取景与移动冒烟测试做锚点。不能拿门口当视点 —— 门两侧就是书墙，第三人称镜头会被顶到 1m 内。

**E. 门的运行时逻辑（`Assets/Scripts/Interaction/EntranceDoor.cs`）**

- 状态机 `Closed → Opening → Open → Closing → Closed`：
  - `Opening` / `Closing` 期间不接受新点击（重复点击无副作用），其余状态都可以；
  - `Open` 时若玩家越过门槛 `enterDepth = 0.55m` 记 `HasEntered` 并转 `Closing`；
  - `Closing` 时若玩家还卡在门洞里（前后 0.40m、横向在门宽内）立刻转回 `Opening`，不夹人。
- 门扇绕**世界 Y 轴**用 `Quaternion.AngleAxis(θ, Vector3.up) * closedRot` 旋转（不用 local 轴：FBX 子物体带 100 倍缩放、根节点绕 X 转了 -90°，局部轴不等于世界轴）。开 85°，0.55s 开、0.75s 关。
- 开合方向**不写死正负号**：`SwingSign()` 把门扇自由边分别转 ±90°，取"更朝向店内"的那个符号。门换到别的墙上也不会开反。
- 交互射线：光标锁定时从屏幕中心、未锁定时跟鼠标；命中门扇碰撞体即开门。两扇门之间有 **4cm 门缝**，正对缝隙时射线会直接穿过去打到店内地面 —— 加了一条兜底：只要门平面交点比命中的东西更近、且交点落在门洞矩形内（门宽 × 2.45m 高），也算点到了门。
- `rayRange = 8m` 是射线长度（第三人称镜头在角色身后 3.6m，用 4.5m 够不到），`playerReach = 2.6m` 单独判"人站在门口附近"。

**F. 门铃（`Assets/Scripts/Audio/ProceduralSfx.cs`）**

- 仓库里没有任何音频资产，门铃用程序化合成：E6(1318.5Hz) → A5(880Hz) 两声，各自带 2× / 3× 泛音、指数衰减 7.0，总长 1.3s，首尾 8ms 淡入淡出防爆音。`AudioClip.Create` 运行时生成，不落盘。
- 相机补了 `AudioListener`（之前没有，会一直刷 "no audio listeners"）。
- 12 票接正式音效时把 `ProceduralSfx.DoorBell()` 换成 `AudioClip` 资产即可，调用点不动。

**G. 零引导**

- 进店后不弹任何 UI、箭头、任务提示，门关了就是关着。`EntranceDoor` 不持有任何 UI 引用。

**H. 自动化验收（`Assets/Scripts/Testing/EntryFlowTest.cs`，入口 `PlayTest.RunEntry`）**

用虚拟输入驱动真实 `PlayerMotor`、真实射线交互，全程不直接改状态。输出 `game/entry_flow.txt`：

```
spawnDepth=-1.55 (负值=在门外)
  ok   出生点在门外地台上
closedDoor: moved=1.17m depth=-0.38 state=Closed
  ok   角色确实朝门走了
  ok   门关着时被挡在门外，没有穿透
cam=(4.47,3.15) aimScreen=(320,325) screen=(640,480) hit=True bell=1
  ok   射线打到门并触发开门
  ok   门铃响了一次
open again while opening -> False bells=1
  ok   开门过程中重复点击无副作用
door open: amount=1.00 state=Open after=0.55s
  ok   门扇开到 85°
afterWalkIn: depth=3.25 hasEntered=True state=Closed
  ok   玩家已越过门槛进店
  ok   流程记录到已进店
door closed: state=Closed amount=0.00 after=0.00s
  ok   进店后门自动关闭
reopen=True amount=0.00 bells=2
  ok   第二次开门正常
  ok   第二次开门门铃再响一次
failures=0
ENTRY RESULT: PASS
```

**I. 回归**

- 02 票移动冒烟测试重跑仍 PASS：`probes=48 deadEnds=0 outOfRoom=0`，吧台阻挡 0.29m。
- 几何复核：102 件家具、出界 0（`Porch` 在 `check_inside.py` 里加入跳过列表，它本就在地板多边形外）。

**J. 验收图**

- `.scratch/vertical-slice/acceptance/03/setup_screenshot_entrance.png`：角色站在门外地台上、面对关闭的玻璃双开门，透过玻璃看得到店内。
- `.scratch/vertical-slice/acceptance/03/setup_screenshot_entrance_open.png`：门扇向店内开到位，门洞通畅。

**遗留**

- 第三人称镜头在出生点会被地台边缘"悬空"——镜头跳过 `Boundary` 层，所以它会飘在地台上方之外看进来。视觉上没问题（背景是纯色），但正式场景有外街景后要重新确认。
- 门外仍是空场景（无外立面、无街道），按用户要求只做了地台。

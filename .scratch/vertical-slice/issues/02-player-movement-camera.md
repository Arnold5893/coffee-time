# 02: 玩家移动与镜头

**What to build:** 玩家角色（Quaternius 默认外观占位）在店内走路：第三人称高位斜俯视跟随镜头，可旋转不可缩放；与家具/墙体/吧台的碰撞生效；遮挡角色时自动淡化。PC 键盘移动＋鼠标旋转，输入层预留移动端摇杆接口。

**Blocked by:** 01 工程与场景基线

**Status:** resolved

- [x] 角色可走路，不可跑/跳/蹲/传送（对应 SRS FR-MOV-02）
- [x] 镜头可旋转、平滑跟随，任何输入不能缩放（FR-MOV-01）
- [x] 碰撞规则生效：家具、墙体、吧台不可穿越
- [x] 遮挡淡化生效（FR-MOV-03）
- [x] 店内走一圈无死角卡死

**结果（2026-11-06）：**

**A. 输入层（`Assets/Scripts/Controls/`）**

- `PlayerIntent`（`Move`/`Look` 向量）＋ `IPlayerInputSource` 接口，把"意图"和"设备"彻底分开。
- `KeyboardMouseInputSource`：WASD/方向键走 `Input.GetAxisRaw("Horizontal"/"Vertical")`，鼠标 `"Mouse X"/"Mouse Y"`，灵敏度 2.4；左键锁光标、Esc 解锁、左 Alt 切 Y 轴反向。工程仍是旧版 Input Manager（`activeInputHandler: 0`），未引入 Input System 包。
- `VirtualJoystickInputSource`：`SetMove` / `AddLook` / `Clear`，`Read()` 读完即清零 look —— 这就是票据要求的"移动端摇杆接口"，UI 之后直接往里灌值即可，不改下游任何代码。
- `PlayerInputRouter`（`[DefaultExecutionOrder(-200)]`）持有当前设备与最新 `Intent`，`SetDevice` 切换、`SetSuppressed` 只关物理设备（虚拟输入照常放行，便于自动化与过场自己驱动）。

**B. 移动（`Assets/Scripts/Player/PlayerMotor.cs`）**

- `CharacterController`：高 1.66 / 半径 0.28 / `skinWidth` 0.03 / `stepOffset` 0.2 / `slopeLimit` 50°。
- 只有一种速度 `walkSpeed = 1.5 m/s`（约等于真人慢走），无冲刺、无跳跃、无蹲、无传送入口 —— 唯一能改位置的 `Teleport()` 只给过场/复位用，不在玩家输入路径上。
- 方向按**相机水平朝向**解算：`fwd = 相机前向投影到 XZ`，`right = Cross(up, fwd)`，`dir = fwd * Move.y + right * Move.x`；角色朝移动方向按 `turnSpeed = 720°/s` 转。
- `gravity = -18`，落位后 `verticalVelocity` 归零。

**C. 镜头（`Assets/Scripts/Cameras/ThirdPersonCameraRig.cs`）**

- 高位斜俯视：`distance = 3.6m`（**常量**）、`pitch = 35°`、`minPitch 20° / maxPitch 58°`。
- **不可缩放**：代码里没有任何滚轮/双指/视野缩放读取，也没有放大 `distance` 的公开入口，从结构上排除。`SphereCast` 撞到墙时的 `minDistance = 1.0m` 是避让缩量，不是可调缩放。
- 平滑：位置 `SmoothDamp` 0.10s、yaw 0.05s；`SnapToTarget()` 用于开场瞬移就位，`ConfigureInitialView(yaw, pitch)` / `RestoreDefaultView()` 给后续票据预设视角。

**D. 遮挡淡化（`Assets/Scripts/Cameras/CameraOcclusionFader.cs`）**

- 从相机朝"角色胸口"（`target + up*1.05`）打 `SphereCastNonAlloc`，`probeRadius = 0.09`，命中 `Renderer` 就换成 URP Lit 透明克隆（`_Surface=1`、`_ZWrite=0`、`SrcAlpha/OneMinusSrcAlpha`、`_SURFACE_TYPE_TRANSPARENT`），`fadedAlpha = 0.32`；离开后还原。
- 克隆材质走 `cloneCache` 缓存、只在运行时创建，不污染资产目录。
- 踩过的坑：`probeRadius` 初值 0.22 太粗，把角色脚边的桌面全判成"遮挡"，整屏发灰；收紧到 0.09 并把探测终点从脚下抬到胸口才正常。

**E. 碰撞体烘焙（`ProjectSetup.cs` 6.5 节）**

- 从 FBX 的 `MeshFilter` 逐个生成 `BoxCollider`，跳过 `Ceiling / Pendant / Painting / Lightbox / Hood`（挂在头顶、不该挡人），共 **100 个**，加程序化玩家 1 个 = 101。
- 薄片加厚：桌面、隔板这类世界厚度 < 0.06m 的件，原地加厚到 0.25m 并**保持顶面高度不变**（`center` 沿局部上轴回退半个增量）。
  - FBX 子物体带 100 倍缩放、根节点绕 X 转 -90°，所以不能用局部 `size.y` 判断厚度。改为先把世界上方向 `InverseTransformDirection` 到局部、找出对应轴 `upAxis`，再用世界厚度 `size[upAxis] * |lossyScale[upAxis]|` 判断，并把 0.25m 换算回局部（`0.25 / |lossyScale[upAxis]|`）。
- 这一条是整张票的关键卡点：`Floor` 的 `BoxCollider` 原本厚度为 0，`CharacterController` 常驻穿透态，`isGrounded` 恒 false，水平位移被整体吃掉 —— 表现为"角色一步不动"。修完后 `Floor` 碰撞体为 `(11.54, 0.25, 15.75)`、`center.y = -0.1`，顶面恰在 `y = 0`。

**F. 玩家占位外观**

- 票据写"Quaternius 默认外观占位"，但仓库里没有任何角色模型资产（`game/Assets` 下只有 `coffee_shop.fbx`）。当前用程序化占位：胶囊腿 + 胶囊身 + 球头（`PlayerBody.mat` / `PlayerSkin.mat`）+ 一个立方体朝向标，共 4 个 primitive，全部 `DestroyImmediate` 掉自带 `Collider`，只留 `CharacterController`。
- 换模型时只需替换视觉子物体，`PlayerMotor` / 镜头 / 淡化都不依赖外观。

**G. 自动化验收**

- `Assets/Editor/PlayTest.cs`（入口，`-executeMethod PlayTest.Run`，**不要加 `-quit`**）打开 Main、关掉 `ScreenshotRig`、注入 `PlayerMotor` / `PlayerInputRouter` / `ThirdPersonCameraRig` / 房间与前后柜包围盒、挑 7 个起点（出生点 + 5 对家具中点 + 地板中心，压家具时就地螺旋外扩 ≤1.2m 找空位）。
- `Assets/Scripts/Testing/MovementSmokeTest.cs`：
  - 切到虚拟摇杆、抑制键鼠，**8 个方向 × 7 个起点各推 1.2s**，`freeDirs < 3` 记死点；
  - 吧台穿刺：从前后柜中点朝后操作台推 2.5s，要求平面最小距离 `≥ 0.20m`；
  - 越界：任一点跑出地板包围盒（内收 0.25m）即失败。
  - **必须 `Time.captureDeltaTime = 1f/60f`**：批处理下渲染帧率可达 ~8500fps，`deltaTime` 小到 1e-4 量级时 `CharacterController` 的扫掠位移被接触偏移吃光，位移恒为 0（第一次跑出 `outOfRoom=0` 但全部"原地不动"的假 PASS 就是这么来的）。跑完在 `Finish()` 里复位为 0。
- 输出 `game/movement_smoke.txt`：

```
  anchor=(-6.5,-7.3) freeDirs=8/8
  anchor=(-5.4,-1.5) freeDirs=7/8
  anchor=(-7.6,-8.0) freeDirs=3/8
  anchor=(-7.4,-6.5) freeDirs=6/8
  anchor=(-2.6,-5.7) freeDirs=5/8
  anchor=(-5.6,-7.4) freeDirs=6/8
  anchor=(-4.5,-8.7) freeDirs=7/8
probes=56 deadEnds=0 outOfRoom=0
barBlock: start=(-5.40,-1.45) moved=0.51 minPlanarDistToBackCounter=0.29 (>=0.20 means blocked outside)
wallEscape=False
SMOKE RESULT: PASS
```

  56 个探测点全部可达、0 死点、0 越界；吧台把角色顶在 0.29m 外（≥0.20 即视为在柜外被挡住）。最低的 `freeDirs=3/8` 出现在 `(-7.6,-8.0)`——那是桌椅夹出的角落，本身就该窄，仍有 3 个方向可走，不算卡死。

**H. 验收图**

- `.scratch/vertical-slice/acceptance/02/setup_screenshot_playerview.png`：跟随镜头下角色居中偏下、高位斜俯视、无穿模、无异常淡化。
- `.scratch/vertical-slice/acceptance/02/setup_screenshot_occlusion.png`：隔断书架半透明、角色可透视，只淡化该书架，其余家具不透明。

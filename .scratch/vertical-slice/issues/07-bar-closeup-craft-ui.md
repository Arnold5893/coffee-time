# 07: 吧台近景与做饮品 UI

**What to build:** 玩家到吧台触发操作后，镜头切换为原地近景（收紧视野、非固定机位），退出恢复跟随镜头；五种操作原语的 UI 控件（按住进度条/点击按钮/选项列表/放置热点/等待进度）并接线到 06 的领域层；玩家站位在拓宽后的吧台内侧通道。PC 与移动端同一套交互逻辑。

**Blocked by:** 02 玩家移动与镜头、06 做饮品系统（领域层）

**Status:** resolved

- [x] 原地近景切入 ≤3 秒、退出恢复（FR-MOV-05、NFR-03）
- [x] 五原语 UI 可完成一杯拿铁全流程（FR-MAKE-03）
- [x] 偏差有即时画面/音效反馈，无文字评级（FR-EVAL-03）
- [x] 玩家可在吧台内侧通道自由移动（配合 01 的通道拓宽）
- [x] UI 不直接改写业务数据，只发操作请求（NFR-05）

**结果（2026-11-07）：**

四个新文件 + 两处接线：`Bar/BarStation.cs`、`Bar/CraftPanel.cs`、`Drinks/CraftSession.cs`、
`Cameras/CameraFocus.cs`，以及 `ThirdPersonCameraRig` 的近景模式、输入层的界面模式。

**A. 近景是"同一台相机换构图"，不是切固定机位（FR-MOV-05）**

`CameraFocusState` 是纯计算：进度 0→1，时长 0.9s（构造时按 3s 上限截断，改配置也改不坏），
距离 3.6→2.25m、俯角 35→32°、FOV 60→42°，**都走同一条 smoothstep 曲线**，所以没有硬切感。
因为是同一台相机，镜头仍然跟着角色、仍然能被右键转动——这就是"非固定机位"。

机位摆哪一侧是现算的：吧台内侧通道只有 1.2m 宽，站到人正后方会顶进前柜、整屏都是后背
（第一版截图就是这个结果）。所以沿"人→工作点"的左右两侧各 SphereCast 一次，选没挡住的那侧；
跟随点再朝工作点前移 0.42m、抬高 0.16m，把台面推到画面中间、把人让到边上。

**B. 五原语对应五种控件，全部只发请求（NFR-05）**

| 原语 | 控件 | 落到领域层 |
| --- | --- | --- |
| click | 圆形主按钮，指针按下→抬起 | `Click(按下时长)` |
| hold | 同一个按钮按住不放 + 进度条 | `Hold(dt)` 攒、`Release()` 结算 |
| select | 一排选项按钮 | `Choose(选项)` |
| place | 一排热点按钮 | `Put(热点)` |
| wait | 不用操作，进度条自己走 | `Await(dt)` |

界面手里只有 `CraftSession`，没有 `DrinkMaker`、没有 `OrderBook`，也没有任何 setter：
每个动作都是一次请求，被拒绝（`NotCurrent`/`Blocked`/`Served`）就只是被拒绝。
测试里"当前是放置步时发 Choose"验证了状态一点没动（NFR-05）。

**C. 判偏只有红闪 + 闷响，没有文字评级（FR-EVAL-03）**

`DrinkMaker.StepFinished` 里 `Off` → 全屏红闪（0.45s 衰减）+ `ProceduralSfx.CraftThud()` 低频闷响，
然后出「重做 / 继续」两个按钮（对应 7.9.4 的两个选择）。
`CraftSession` 不向界面暴露 `DrinkQuality`，EditMode 用反射把这条钉住了。

**D. 一处数据修正：取杯是"放置"不是"点击"**

配方第一步"取杯放到萃取口下"原来是 `click`，但它本质是放置（FR-MAKE-03 里 place 的定义就是"放杯、上托盘"）。
改成 `place` + 两个热点（萃取口 / 台面），错误的就是放到台面上。步骤数、判定步数都没变（7 步 / 判 3），
06 的测试只是把"取杯"从 `Click()` 换成 `Put("萃取口")`。

**E. 输入层**

`PlayerIntent` 加 `Cancel`（PC Esc / 移动端返回）；`PlayerInputRouter.SetUiMode(bool)` 进界面时
不锁光标、不发世界交互、走路清零，但保留右键转视角（近景仍可看）。
`KeyboardMouseInputSource.UiMode` 与 `VirtualJoystickInputSource.PressCancel()` 一一对应，PC 与移动端同一套逻辑。

**踩到的坑**

1. 指针事件必须走 uGUI 的 `IPointerDownHandler/IPointerUpHandler`（鼠标与触屏同一条路），
   场景里没有 EventSystem，`ProjectSetup` 按需补一个。
2. `UiKit.TextButton` 建了 Text 却忘了赋值 → 按钮底色在、字是空的。截图里一眼看出来。
3. 进度条一开始用改 `anchorMax` 控制宽度，读到的是 0：截图时序问题——测试脚本在一次
   `yield` 里灌完进度，界面还没刷新就拍了。改成 `Image.fillAmount` 并**拍前等一帧**才稳定。
4. 判"按住够不够"的阈值不能写 0.99：0.99×2.5s = 2.475s < 2.5s，照样判偏。
   测试与截图都改成"顶到 1 才松手"。

**验收**：EditMode **107/107**（07 新增 18：CraftSession 12 + CameraFocus 6）；
`RunCraft` **PASS**（44 项：近景 0.87s、FOV 60→42、距离 3.6→2.25、五原语做完整杯、
判偏红闪、退出后 FOV/距离恢复、通道内走 1.83m）；回归 `Run` `probes=48 deadEnds=0`、
`RunEntry` `failures=0`、`RunSeat` `SEAT RESULT: PASS`（`bad*=0`，夹缝 4 全在白名单）。
归档 `.scratch/vertical-slice/acceptance/07/`。

---

## 返工（2026-11-07，老板验收后）

**几何：删掉两处不存在的物件**（`build_shop.py`，仍走"唯一几何源"铁律）

1. `Pendant_Bar_Flower` 整件删除（含 `Pendant_Bar_Flower_Light`）——实景没有这盏灯；
   8 片陶瓷薄片在 Unity 里读成一团白色扇形纸片，挂在黑色吊线下面。
2. `Coffee_Machine` 顶部杯架的黑色细杆全部删除（`b.beam` 竖杆 + 顶部横杆），**杯子留下**（杯子端正地坐在机顶，读得通）。
   8mm 细杆在游戏视角里就是一堆悬浮黑线。

导出流程补一条：`coffee_shop.fbx` 必须**手动复制**到 `game/Assets/Models/coffee_shop.fbx`，
否则 Unity 用的还是上一版（这次差点拿旧模型验完收工）。

**交互：两套都删了，等重新设计**

- 「按住攒进度条」磨豆 → 删（老板：太拉胯）。
- 「点一下压粉 + 全屏红闪」→ 删（老板：压粉做得不好，且不应该把屏幕变红）。
- 连同 `PressDown/PressUp/TapAction`、进度条、`Flash` 全屏遮罩一起删除。
- 现在 `click/hold` 步骤是**占位**：一个「完成这一步」按钮，直接按配方 `param` 结算，
  只为保持流程跑通与回归不断；面板上写「交互重做中（占位）」。
- **领域层不动**：`Recipe` 的五原语与 `hold=达标秒数 / click=按下达标秒数` 仍成立，
  要换的是"玩家用什么动作产出这些秒数"。

**判偏画面反馈**：全屏红闪已删，现在只有一声闷响 + 「重做 / 继续」。
FR-EVAL-03 要求的"即时画面/音效反馈"目前只有音效，画面部分待新设计一起定。

**回归**：`RunCraft` **PASS**（44 项，failures=0）、EditMode **107/107**。

# 07 验收：吧台近景与做饮品 UI

**日期：** 2026-11-07（返工二：直接操作层）　**提交：** （见 git log）　**票：** `.scratch/vertical-slice/issues/07-bar-closeup-craft-ui.md`

## 结论

交互已从「按住攒进度条 / 点一下压粉」整体换成**直接操作物体**：绕圈磨豆、下拖压粉、拖杯子取杯与出杯、
奶缸保持打奶、横向划一道淋糖浆。判偏**只做局部缺陷**（粉面低一截、粉饼压歪），
没有全屏红闪、没有文字评级。整条拿铁线一次跑完，`craft_flow.txt` 里是 50 项记录，`failures=0`。

## 截图

| 文件 | 看什么 |
| --- | --- |
| `setup_screenshot_bar_prompt.png` | 站在工位上、未开始：底部提示「按左键开始制作 · 经典拿铁」；机位沿通道轴摆，才看得见人与吧台 |
| `setup_screenshot_bar_deviation.png` | 杯子拖到了「台面」而不是萃取口：判偏事件 + 闷响，「重做 / 继续」，**没有任何文字评级、没有红屏** |
| `setup_screenshot_bar_focus.png` | 绕圈磨豆磨到 59%：**粉面明显低于碗口**，能看见一圈碗壁（这就是「粉还不够」） |
| `setup_screenshot_bar_defect.png` | 歪着压到底之后：**粉饼明显倾向一侧、高的一角顶出碗沿**（10.8°），界面上一个字都没提示 |
| `setup_screenshot_bar_restored.png` | 退出后界面消失、FOV/距离回到跟随构图，只留「开始制作」提示 |

## 数据

- 近景切入 **0.87s**（上限 3s）；FOV **60→34**；距离 **3.60→0.85m**；俯角 35→46°；相机落点 `(-7.03, 1.61, -0.95)`
- 工作区：咖啡机西侧空台面（Unity x −7.62…−6.52，1.10m 宽），锚点 `(-7.03, 0.91, -0.41)`
- 手势计数：`turnFrames=151`、`pushFrames=41`；终态 `powder=100% tilt=10.8°`
- 整杯评价：`错误 0/3（0%）→ Good`（两次判偏都点了重做，所以最终不留错）
- 吧台内侧通道直线行走 **1.83m**，走完仍在通道内（z 偏差 0.00）
- EditMode **107/107**（`CraftSessionTests` 12 条 + `CameraFocusTests` 6 条）
- 回归：`SMOKE RESULT: PASS`（probes=48 deadEnds=0）、`ENTRY RESULT: PASS`（failures=0）、
  `SEAT RESULT: PASS`（bad*=0，夹缝 4 个全在白名单）

## 复跑

```bash
# 场景（改过 ProjectSetup / 相机序列化参数才需要）
Unity.exe -batchmode -projectPath D:/coffee_shop/game -executeMethod ProjectSetup.Run -quit

# 截图 / 端到端 / 回归（都不能加 -quit）
Unity.exe -batchmode -projectPath D:/coffee_shop/game -executeMethod PlayCapture.Run
Unity.exe -batchmode -projectPath D:/coffee_shop/game -executeMethod PlayTest.RunCraft
Unity.exe -batchmode -projectPath D:/coffee_shop/game -executeMethod PlayTest.Run
Unity.exe -batchmode -projectPath D:/coffee_shop/game -executeMethod PlayTest.RunEntry
Unity.exe -batchmode -projectPath D:/coffee_shop/game -executeMethod PlayTest.RunSeat

# 单元测试
Unity.exe -batchmode -projectPath D:/coffee_shop/game -runTests -testPlatform EditMode -testResults ...
```

> 批处理前必须 `export PROGRAMDATA='C:\ProgramData'` 和 `ALLUSERSPROFILE`，否则 URP 初始化报错。

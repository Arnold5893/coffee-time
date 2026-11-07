# 07 验收：吧台近景与做饮品 UI

**日期：** 2026-11-07　**提交：** （见 git log）　**票：** `.scratch/vertical-slice/issues/07-bar-closeup-craft-ui.md`

## 结论

五项验收全部通过。吧台工位 → 按交互 → 原地近景（0.87s 切入）→ 五原语做完整杯 → 出杯自动收起
→ 镜头回跟随构图，全链路在 `craft_flow.txt` 里是一条 44 项的记录，`failures=0`。

## 截图

| 文件 | 看什么 |
| --- | --- |
| `setup_screenshot_bar_prompt.png` | 站在工位上、未开始：底部提示「按左键开始制作 · 经典拿铁」；镜头沿通道轴摆，才看得见人与吧台（正后方会顶进前柜） |
| `setup_screenshot_bar_focus.png` | 近景 + 按住磨豆：顶部步骤名/提示、进度条 52%、圆形「按住」、跳过/出杯/退出 |
| `setup_screenshot_bar_deviation.png` | 压粉只点了一下（0.06s < 0.4s）：全屏红闪 + 闷响，「重做 / 继续」，**没有任何文字评级** |
| `setup_screenshot_bar_restored.png` | 退出后界面消失、FOV/距离回到跟随构图，只留「开始制作」提示 |

## 数据

- 近景切入 **0.87s**（上限 3s）；FOV **60→42**；距离 **3.60→2.25m**；俯角 35→32°
- 整杯评价：`错误 0/3（0%）→ Good`（两次判偏都点了重做，所以最终不留错）
- 吧台内侧通道直线行走 **1.83m**，走完仍在通道内（z 偏差 0.00）
- EditMode **107/107**（新增 `CraftSessionTests` 12 条 + `CameraFocusTests` 6 条）
- 回归：`SMOKE RESULT: PASS`（probes=48 deadEnds=0）、`ENTRY RESULT: PASS`（failures=0）、
  `SEAT RESULT: PASS`（bad*=0，夹缝 4 个全在白名单）

## 复跑

```bash
# 场景（改过 ProjectSetup 才需要）
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

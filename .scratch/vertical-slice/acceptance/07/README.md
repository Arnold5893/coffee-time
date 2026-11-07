# 07 验收：吧台近景与做饮品 UI

**日期：** 2026-11-07（返工三：建模重做）　**提交：** （见 git log）　**票：** `.scratch/vertical-slice/issues/07-bar-closeup-craft-ui.md`

## 结论

1. **交互**：从「按住攒进度条 / 点一下压粉」整体换成**直接操作物体**——绕圈磨豆、下拖压粉、
   拖杯子取杯与出杯、奶缸保持打奶、横向划一道淋糖浆。
2. **判偏只做局部缺陷**（粉堆小一圈、粉饼压歪偏到碗边），没有全屏红闪、没有文字评级。
3. **建模重做**：道具不再是 `CreatePrimitive(Cylinder/Cube)`，改成一条二维轮廓绕 Y 轴转一圈
   （`PropMesh.Revolve`）+ 圆管把手 + 圆角板，各有真实轮廓。

`craft_flow.txt` 50 项，`failures=0`。

## 截图

| 文件 | 看什么 |
| --- | --- |
| `setup_screenshot_bar_prompt.png` | 站在工位上、未开始：底部提示「按左键开始制作 · 经典拿铁」；机位沿通道轴摆，才看得见人与吧台 |
| `setup_screenshot_bar_deviation.png` | 杯子拖到了「台面」而不是萃取口：判偏事件 + 闷响，「重做 / 继续」，**没有任何文字评级、没有红屏** |
| `setup_screenshot_bar_focus.png` | 绕圈磨豆磨到 59%：**粉堆只有半个碗底大，四周一圈亮钢碗壁**（这就是「粉还不够」）。左边是压粉器（钢底盘 + 木把手），右边是马克杯（把手 / 杯口瓷厚） |
| `setup_screenshot_bar_tamp.png` | 压下到一半、还没抬手：**压粉器底盘正对碗口沉进去**，杆和木把手露在外面 |
| `setup_screenshot_bar_defect.png` | 歪着压到底之后：**粉饼偏到碗的一边、另一侧露出碗底**（10.8°），界面上一个字都没提示 |
| `setup_screenshot_bar_restored.png` | 退出后界面消失、FOV/距离回到跟随构图，只留「开始制作」提示 |

## 道具建模

| 器物 | 轮廓特征 |
| --- | --- |
| 滤碗 | 锥形外壁（底窄口宽）+ 3mm 圆润卷边 + 直筒内腔 + 内底倒角 |
| 粉饼 | 比例化轮廓 + 顶面缓丘 + Perlin 颗粒起伏 |
| 压粉器 | 倒角底盘 + 收腰杆 + 深色木质柱形把手 |
| 马克杯 | 锥形杯身 + 杯口 3.5mm 瓷厚 + 内壁 + C 形把手 |
| 奶缸 | 鼓形壶身 + 收口 + 顶点变形拉出的单边壶嘴 + 环形把手 |
| 糖浆瓶 | 瓶身 + 肩 + 瓶颈 + 金属盖 |
| 工作垫 | 圆角板两层：深色包边 + 内嵌的面 |

尺寸按真实器具 **1.6 倍**，比例关系保持真实（杯 > 奶缸 > 压粉器 ≈ 滤碗）。

## 数据

- 近景切入 **0.87s**（上限 3s）；FOV **60→34**；距离 **3.60→0.80m**；俯角 35→54°
- 工作区：咖啡机西侧空台面（Unity x −7.62…−6.52，1.10m 宽），锚点 `(-7.03, 0.91, -0.35)`
- 道具排 −0.26…+0.28（出杯位 / 压粉器座 / 粉碗 / 台面 / 托盘 / 萃取口 / 蒸汽位），垫子 0.66 × 0.24
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

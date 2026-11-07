# fix-08 验收 · 坐姿穿模 + 后场操作台过长

反馈（2026-11-06，用户）：

1. `setup_screenshot_seated.png` 里人坐下时**椅子穿模**，要求不穿模、并且看得到椅背；
2. 参考实景照片，**吧台后方的操作台没有这么长**——只到咖啡机位置就结束，之后空一段距离，另一侧摆一张放杂物的木桌。

## 改了什么

### A. 坐姿（`game/Assets/Scripts/Seating/SeatInteraction.cs`）

原来是"整体下沉 + 压腿"的糊法，臀部会陷进座面、大腿捅穿椅面。改成**按部件重摆**：

- 大腿：躺平前伸，坐在座面前沿之上；
- 小腿：从膝盖垂下到地面；
- 躯干：底面**贴**座面（不下沉），进深收窄到 0.40×0.28×0.30，给靠背让位；
- `Bind()` 自动取 `PlayerVisual_Legs`，缺件时退回整体摆姿。

### B. 后场操作台（`仅供参考的文件/build_shop.py`，几何唯一源）

- `BACK_END_PX` 910 → **662**（咖啡机 630 + 0.40m）：原来在咖啡机以东凭空多出 3.2m 空台面，纯属建模偏差；台面现在**止于咖啡机东缘**（世界 x≈6.53）。
- 灯箱 `Bar_Lightbox`、配电箱**不跟着收**（实景里它们照旧走满整段后墙）。
- 新增 `Back_Clutter_Table`：1.80×0.58×0.75m 旧木桌，摆在台面末端以东 1.1m 的空地上，桌面/下层搁板堆书、黑音响、敞口纸箱、小风扇、白瓷杯、小盆栽（盆栽就地生成，不走 `PLANTS` 的地面基准）。
- `Back_Counter_Misc` 只保留水槽附近 3 件。

## 验收

| 项 | 结果 |
| --- | --- |
| Blender 出界检查 | 总物件 101，**出界 0** |
| EditMode 单测 | **20/20 通过**（`editmode_results.xml`） |
| 座位系统 `PlayTest.RunSeat` | **PASS**，`geometry: badFacing=0 badSurface=0 badStand=0 badSeat=0 cramped=2` |
| 02 回归 `PlayTest.Run` | **PASS**，`probes=48 deadEnds=0 outOfRoom=0` |
| 03 回归 `PlayTest.RunEntry` | **PASS**，12 项全 ok，`failures=0` |

## 图

- `setup_screenshot_seated.png` / `seated_hip_zoom.png`：坐姿——臀部落在座面上（未陷进椅面），大腿水平前伸、小腿垂下，**椅背可见**且没有被身体穿透。
- `setup_screenshot_backarea.png`：后场一条线拍全「旧木桌（书/音响/纸箱）→ 空地 → 台面末端（咖啡机）」。
- `plan_backarea_top.png`：吧台区正交俯视，台面止于咖啡机、之后留空、再往东是木桌，一眼可量。
- `setup_screenshot_entry_corner.png`：门区回归，桌尾收头后门前落脚区未被压到。

## 机位怎么定的（避免下次又瞎试）

后场机位不再靠"看包围盒差不多"硬凑，先用 `.scratch/tools/cam_preview.py` 在 Blender 里预演构图
（该脚本把 Unity 相机坐标反解成 Blender 坐标：`blender = (-X, Y, -Z)`，垂直 FOV 60、1280×800，与批处理截图一致），
定稿后再换算进 `ProjectSetup.cs`：

```
Blender (9.62,1.82,1.90) → (6.90,0.42,0.80)
== Unity (-9.62,1.82,-1.90) → (-6.90,0.42,-0.80)
== ctb.center(-8.52, ?, -0.43) + 相机(-1.10, ·, -1.47) / 注视(+1.62, ·, -0.37)
```

`cam_prev_P.png` 复用旧机位参数，渲染结果与旧 `setup_screenshot_backarea.png` 构图一致，说明这套换算可信。

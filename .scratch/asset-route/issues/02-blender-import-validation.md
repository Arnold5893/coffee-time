# 02 · Blender 场景资产导入验证

Type: prototype
Status: resolved
Blocked by: 01

## Question

`仅供参考的文件/coffee_shop.blend`（及其 `.glb` 导出）导入 Unity 6 URP 后是否可用？给出三选一结论：**直接用 / 改造后用 / 废弃重建**。

验证项：

- 比例：店内尺寸与平面图一致（设计方案 12.1），角色身高参照下门、桌、吧台高度正确
- 材质与贴图：导入后材质是否保留或需重接；URP 转换成本
- 网格质量：法线、破面、多余顶点，移动端可用性初判
- 结构：家具是否独立对象（可单独做碰撞与交互点位），还是整店一个网格
- 结论影响：若"废弃重建"，灰盒阶段改用方块搭建，正式美术资产全部走选型路线

## 完成标准

给出三选一结论＋关键证据（截图、面数、问题清单），结论写入本票 Answer 并登记到 map。

## Answer（2026-11-06）

**结论：改造后用。** 证据：import-test/unity_import_report.txt 与 unity_import_screenshot.png。

| 验证项 | 结果 |
| --- | --- |
| 比例 | ✅ 单位 1.0 米，Unity 包围盒 10.21 × 4.50 × 13.00 m，与平面图尺度一致 |
| 结构 | ✅ 120 个独立网格对象全部保留并按区域命名（Immersive_/Bar_/Sun_Stool_/Coffee_Machine/Grinder/Tamper），交互点位与碰撞可直接挂 |
| 网格质量 | ✅ 全店 92,366 三角形（Blender 侧与 Unity 侧完全一致），移动端无压力；法线无异常 |
| 材质 | ⚠️ 56 个材质的颜色属性保留；纹理需在导入设置中 Extract Textures（FBX 已内嵌 12 张）；转 URP 为一次性操作 |
| 灯光 | ⚠️ 26 个 Blender 灯光不迁移，按设计方案三时段在 Unity 重做（本来就要重做） |

**管线定案**：以 `.blend` 为源 → Blender 无头导出 FBX（嵌入纹理）→ Unity 导入 → Extract Textures → 转 URP。纹理源文件已从 GLB 抢救至 `仅供参考的文件/textures/`（此前丢失），`.glb` 保留为纹理备份。

**GLB 直导路线**（glTFast）因本机 UPM 环境问题未测，FBX 路线已满足需求，不再投入。

对 06（吧台灰盒）的影响：吧台区域对象齐备（Bar_Counter/Back_Counter/Cashier/Coffee_Machine/Grinder_1/2/Tamper/Bar_Rack/Bar_Stool_1-3），灰盒可直接在导入场景上验证，无需方块重建。

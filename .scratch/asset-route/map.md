# Map: 资产与场景路线定案（asset-route）

## Destination

首版资产与场景路线定案：得出 Blender 场景资产在 Unity 6 中的可用性结论（直接用 / 改造 / 重建），并选定角色、动画、家具的免费资产基线，使竖切 Spec（G3）可以开工。

## Notes

- 域：Unity 6000.0 LTS + URP；目标平台 PC + Android（首版无 iPad）。
- Blender 资产位于 `仅供参考的文件/coffee_shop.blend`（及 `.glb` 导出）；店内照片、平面图、真实菜单可用作校核。
- 视觉风格：日式木质、暖光、接近写实自然比例、不像素化（设计方案 12.5）。
- 本图只产出决策，不产出交付物；竖切实现属于 to-tickets 执行域，不在本图。
- 相关技能：解决票据时按需调用 `prototype` / `research` / `grilling`。
- 依赖文档：《咖啡店3D游戏_设计方案.md》v4、`docs/fsr.md` v2.0、`docs/srs.md` v0.1。
- 本环境不可用子代理：research 票据在会话内逐个解决，不并行派发。

## Decisions so far

<!-- 每条已关闭票据一行：概要 + 链接 -->

- [01 Unity 开发环境就绪](issues/01-unity-environment-setup.md): Unity 6000.3.28f1 + Personal 许可证 + Android Build Support 就绪；批处理调用须先 export PROGRAMDATA
- [03 角色资产选型](issues/03-character-asset-selection.md): 定 Quaternius Universal Base Characters + Modular Outfits（CC0，模块化发型/服装/肤色，Humanoid 骨骼，URP 支持）；否决 RPM（服务停汰）
- [04 动画来源选型](issues/04-animation-source-selection.md): Mixamo 主库 + Mecanim Humanoid retarget；吧台按住类与端盘走程序化 IK；缺口 Blender 自制
- [05 家具道具选型](issues/05-furniture-prop-asset-selection.md): Poly Haven 写实基线 + ambientCG 材质；吧台特定器具（咖啡机/磨豆机/茶罐/茶壶/托盘）Blender 自制
- [02 Blender 场景资产导入验证](issues/02-blender-import-validation.md): 结论"改造后用"——比例/结构/面数全优；管线定为 .blend→FBX→Extract Textures→转 URP；纹理已从 GLB 抢救
- [06 吧台灰盒布局方案](issues/06-bar-greybox-layout.md): 九工位定案（5 就位＋茶罐架/注水位/托盘放后柜东段 1.8m 空位）；通道拓宽至 ≥1.2m（前柜北移 ~0.9m，模型 0.32m 系建模偏差，实际店内两人可通行）

## Not yet specified

- 正式美术资产清单：哪些家具/道具自制、哪些用免费资产、哪些需要改色——依赖导入验证与选型结论。
- NPC 外观池的具体组合规则——依赖角色资产选型结果。
- 店门外门面与窗外静景（树＋建筑剪影）的资产来源——依赖整体风格基线定案。
- 打烊灯光演出的实现方案——属于阶段四，暂不需要精确化。

## Out of scope

- 竖切的代码实现与排期（属 to-tickets 执行域）。
- 通关卡片文案池（设计方案待确认清单第 1 项，属阶段四）。
- 40 款完整菜单的资产（首版只做 14 款）。
- 音频曲目与音效选型（独立决策域 G6，不阻塞竖切）。

---

**地图完成（2026-11-06）**：六张票全部解决，资产路线已定——Blender 场景"改造后用"（管线 .blend→FBX→URP）、角色 Quaternius、动画 Mixamo+程序化 IK、道具 Poly Haven＋自制吧台器具、吧台通道按真实店铺拓宽。前方清晰：进入 G3 竖切 Spec（to-spec）。遗留迷雾（正式美术清单、NPC 外观池规则、门面/外景资产）属后续阶段的目的地。

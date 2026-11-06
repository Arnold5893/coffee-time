# 03 · 角色资产选型

Type: research
Status: resolved
Blocked by: （无）

## Question

选定玩家角色与 NPC 使用的免费角色资产方案，要求：

- 支持"预设模块＋色板"组合（发型/发色/服装可换，设计方案 3.1）
- 风格匹配日式木质暖光场景：接近写实、自然比例、不像素化
- 免费许可（优先 CC0 / Asset Store 标准许可），商用限制需记录
- 骨骼可复用通用动画（走路、坐下、吧台操作）

输出：2–3 个候选＋推荐项＋许可证摘要。

## 完成标准

推荐一项并给出理由；许可证兼容性结论明确；登记到 map。

## Answer（2026-11-06）

**结论：选用 Quaternius Universal Base Characters + Modular Outfits。**

候选对比：

| 方案 | 模块化（发型/服装/色板） | 风格匹配 | 许可 | 结论 |
| --- | --- | --- | --- | --- |
| Quaternius Universal Base Characters + Modular Outfits | 6 基础体型（男女）、20 发型混搭、眼/肤色着色器可调、服装模块化 | 自然比例、低多边形干净风格，与日式木质兼容 | CC0 | ✅ 推荐 |
| Mixamo 自带角色 | 弱（固定角色不可拆分模块） | 半写实 | Adobe royalty-free，不可再分发 | 备选 |
| KayKit 角色包 | 有模块但偏低龄卡通 | 风格偏萌系，不符"接近写实" | CC0 | 否决 |
| Ready Player Me | 强（捏脸+服装） | 半写实 | 服务正被逐步淘汰，第三方依赖风险 | 否决 |

要点：Humanoid 骨骼可直接用 Unity Mecanim retarget 动画；约 13k 三角面，移动端友好；URP 官方支持；源文件含 .blend 可自行改造。NPC 随机外观池＝基础体型 × 20 发型 × 肤色/眼色组合。

后续验证点：风格匹配需在 06 灰盒中目测确认（与场景同框截图）。

来源：quaternius.com/packs/universalbasecharacters.html；cinevva.com/guides/free-3d-model-sites；genies.com（RPM 停服信息）。

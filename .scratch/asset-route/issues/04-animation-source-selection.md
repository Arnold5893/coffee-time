# 04 · 动画来源选型

Type: research
Status: resolved
Blocked by: （无）

## Question

确定角色动画来源方案。所需动画集（按竖切与首版需要）：

- 走路、站立 idle、坐下/起身
- 座位行为：看书、学习、看电脑、戴耳机听歌
- 吧台操作：按住类（磨豆/倒奶/注水）、点击类（取杯/压粉）、端杯/端托盘行走
- 老板：制作、送餐、坐吧台后玩电脑

调查方向：Mixamo 覆盖率、免费动画包、需要自制或程序化的部分（如搅拌棒归位可用 IK/程序动画）。

输出：动画来源矩阵（动画 × 来源 × 许可），标出缺口。

## 完成标准

动画来源矩阵完成，缺口（需自制/程序化的）列明；登记到 map。

## Answer（2026-11-06）

**结论：Mixamo 为主库 + Unity Mecanim Humanoid retarget；吧台精细操作用程序化 IK 补齐；缺口用 Blender 自制。**

动画来源矩阵：

| 动画 | 来源 | 状态 |
| --- | --- | --- |
| 走路 / 站立 idle / 坐下 / 起身 | Mixamo | ✅ 库覆盖 |
| 看电脑 / 打字（座位行为） | Mixamo（sitting/typing 类） | ✅ 覆盖，具体剪辑实施时选定 |
| 看书 / 戴耳机听歌 | Mixamo 近似剪辑 + 道具挂点 | ⚠️ 部分覆盖，书本/耳机用道具表现补足 |
| 吧台按住类（磨豆 / 倒奶 / 注水） | 程序化 IK（手部跟随器具）+ 上身循环摆动 | ⚠️ Mixamo 无精确剪辑，走程序化 |
| 端杯 / 端托盘行走 | Mixamo walk + 上半身 IK 持物遮罩 | ⚠️ 组合实现 |
| 搅拌棒自动归位 | 纯程序化位移，无需骨骼动画 | ✅ |
| 老板制作/送餐/玩电脑 | 与玩家同套 Mixamo 剪辑复用 | ✅ |

要点：Mixamo 免费（Adobe ID）、royalty-free 可用于个人与商业项目（不可再分发源文件）；仅支持双足人形；Unity 6 官方支持 Mecanim Humanoid retarget（docs.unity3d.com/6000.4）；如需在引擎外 retarget 到自定义骨骼，可用浏览器工具 Rig Bake（免费，导出 .glb）。备选动画包：Kevin Iglesias Basic Motions FREE（Asset Store）、Quaternius 动画包（itch.io，CC0）。

来源：Mixamo 条款（nasrtech.dev/blog/mixamo-to-maya 引述 Adobe FAQ）；Unity 6000.4 Retargeting 文档；rigbake.com。

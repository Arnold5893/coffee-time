# coffee_shop

第三人称单人 3D 咖啡店体验游戏：玩家在大学咖啡店内自由行动、就座、点单、与 NPC 和老板对话、游玩拉花/冲煮两款小游戏。

- 引擎：Unity 6 + URP；目标平台：PC、手机、iPad（PC 60 帧、移动端 30 帧）
- 架构原则：状态拥有者＋操作请求＋状态机＋领域事件；业务逻辑跨平台共用
- 本仓库当前包含 Blender 场景资产与参考素材；完整设计见根目录《咖啡店3D游戏_完整设计方案.md》

## Agent skills

### Issue tracker

Issues and specs live as local markdown files under `.scratch/<feature-slug>/`. See `docs/agents/issue-tracker.md`.

### Triage labels

Uses the five default labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout: root `GLOSSARY.md` + `docs/adr/` (created lazily by the domain-modeling skill). See `docs/agents/domain.md`.

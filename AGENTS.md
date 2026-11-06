# coffee_shop

第三人称单人 3D 咖啡店体验游戏。核心命题："有一天，我推开朋友那家店的门，坐了一下午。"——玩家在店内自由待着，帮老板做饮品是自愿的插曲。

- 引擎：Unity 6 + URP；目标平台：PC、手机、iPad
- 架构原则：状态拥有者＋操作请求＋状态机＋领域事件；只有状态拥有者可改状态
- 核心系统：做饮品（四条基底线＋手动/自动双驱动）、订单、帮工会话、三时段＋天气、打烊流程
- 完整设计见根目录《咖啡店3D游戏_设计方案.md》（v4）；`仅供参考的文件/` 内为 Blender 资产与旧参考，非交付内容

## Agent skills

### Issue tracker

Issues and specs live as local markdown files under `.scratch/<feature-slug>/`. See `docs/agents/issue-tracker.md`.

### Triage labels

Uses the five default labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout: root `GLOSSARY.md` + `docs/adr/` (created lazily by the domain-modeling skill). See `docs/agents/domain.md`.

# 05 票验收 · 订单与客人闭环（领域层）

纯领域层，不接 UI / 场景表现。全部是 Edit Mode 用例，跑一次约 3 秒。

## 用例分布（59 条全绿，其中 05 票新增 39 条）

| 测试类 | 条数 | 覆盖 |
| --- | --- | --- |
| `OrderStateMachineTests` | 18 | FR-ORD-03 合法/非法转换、事件序列、饮用人数 1/2、兜底作废 |
| `OrderDeliveryTests` | 6 | FR-ORD-02/04 送达点＝客人当前座位、没落座先不送 |
| `CustomerLoopTests` | 9 | FR-NPC-02 单客闭环、FR-NPC-05 不抢座/无空位等候、FR-ORD-05 等餐不催 |
| `BossQueueTests` | 6 | 老板自动队列计时、只做没人挑的单、只送自己做的单、端到端自跑通 |
| `Seat*Tests`（04 票） | 20 | 座位占用回归未破 |

`editmode_results.xml` 是完整报告。

## 关键设计（详见 `.scratch/vertical-slice/issues/05-order-customer-loop-domain.md`）

- 状态机用**静态合法表 + 操作请求**：`Order.IsLegal(from,to)` / `Customer.IsLegal(from,to)`，
  测试把 8×8 全组合跑一遍，再用反向断言证明"表说非法 ⇒ 实现确实返回 false 且不发事件"。
- 交付**不收座位参数**，当场向 `ICustomerSeat` 问客人现在坐哪 —— 送达点跟随座位是结构性保证。
- 客人**只从空闲座位里挑**，选完再 `TryOccupy` 复核；没有空位就停在 `WaitingForSeat` 每拍重试。
- 等待期间**不催不走**：`Complain()` 没有任何调用点，`Complaints` 恒为 0。
- 节拍用 `Tick()` 而不是计时器，领域层不含时钟，测试完全可复现。

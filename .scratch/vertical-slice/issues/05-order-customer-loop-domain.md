# 05: 订单与客人闭环（领域层）

**What to build:** 订单状态机（待办→已被认领/老板处理→制作中→待送→已交付→已饮用→完成，记录饮用人数）；一名客人的完整行为闭环（进店→前台点单→找空位落座→等餐→饮用分次消耗→离开）；老板自动制作队列（动画占位＋计时，无偏差）。纯领域层＋Edit Mode 测试，不接 UI/场景表现。

**Blocked by:** 04 座位系统

**Status:** resolved

- [x] 订单状态机合法/非法转换测试通过（FR-ORD-03）
- [x] 订单与客人绑定，送达点跟随客人当前座位（FR-ORD-02）
- [x] 客人闭环状态机测试通过：进店→点单→落座→饮用→离开（FR-NPC-02 单客）
- [x] 客人不抢已占用座位；无空位时等候（FR-NPC-05）
- [x] 等待期间客人不离开、不催促（FR-ORD-05）
**结果（2026-11-07）：**

纯领域层，四个文件全部不引用 `UnityEngine`（`Order` / `OrderBook` / `Customer` / `CustomerDirector`），
另加老板队列 `BossWorkQueue`。Edit Mode 用例 39 条（全仓 59 条全绿）。

**A. 订单状态机（`Assets/Scripts/Orders/Order.cs`）**

主链严格一格一格走：`Pending → Claimed → Making → Ready → Delivered → Drunk → Done`。

- **认领必须带身份**（`OrderActor.Boss` / `Player`，`None` 被拒），认领后不可换人 —— "老板处理"和"玩家挑单"是同一个 `Claimed` 态，只是 `Maker` 不同。
- **交付必须带座位 id**，空串直接失败且状态不动 —— 没座位就老实停在待送。
- **饮用按口记**：`SipsTotal = 饮用人数 × 3`，1 人 3 口、2 人 6 口，喝满才翻 `Drunk`。FR-ORD-02 的"1/2 人"就是这条。
- **`Abandoned` 是主链外的兜底态**（Pending/Claimed/Making/Ready/Delivered 都能进）。竖切要求"任何中断后都有明确状态"，没有它就会出现永远挂在待办里的单。
- `IsLegal(from,to)` 是一张静态表，测试用 `TestCase` 把 8×8 全跑一遍，并反过来验证"表说非法 ⇒ 实现确实返回 false 且不发事件"。

**B. 送达点跟随客人当前座位（`OrderBook` + `ICustomerSeat`）**

`OrderBook.Deliver(orderId)` **不收座位参数**，而是当场向 `ICustomerSeat.SeatOf(customerId)` 问。
这样"送达点＝客人当前座位"是结构性保证，不可能被调用方传错。客人没落座 → `SeatOf` 返回空 → 交付失败、订单留在待送，
下一拍再试（FR-ORD-04）。测试里塞一个可改的假座位口，验证"客人换座后新单送到新座位"。

**C. 客人闭环（`Customer` + `CustomerDirector`）**

阶段图：`Outside → Entering → Ordering → SeekingSeat →（WaitingForSeat ⇄ SeekingSeat）→ SeatedWaiting → Drinking → Leaving → Gone`。

- **不抢已占用座位**是结构性保证：`PickSeat()` 只在 `IsFree` 的座位里挑，选完还要 `TryOccupy` 复核（防并发抢）。
- **无空位就在 `WaitingForSeat` 停住**，每拍重试；座位空出来自己走过去落座。
- **等待期间不催不走**：`Complain()` 在整个代码库里没有任何调用点，`Complaints` 永远是 0；`TickWaiting()` 只累加计数。测试用 `Tick(30)` / `Tick(40)` 断言阶段不变、投诉为 0、订单不动。
- 座位偏好：**先非吧凳**（吧凳留给玩家），一张桌子都没有才退而求其次；`AllowBarStool` 默认 `false`。
- 节拍用 `Tick()` 不用计时器：领域层不含时钟，测试一拍一拍推，结果完全可复现。
- 顺序固定：找座 → 交付 → 喝一口 → 喝完放座结单，避免测试去猜内部顺序。

**D. 老板自动队列（`Assets/Scripts/Boss/BossWorkQueue.cs`）**

`Enqueue` 只收 `Pending` 的单；`Tick(dt)` 里 `Claim(Boss) → StartMaking → 计时 18s → FinishMaking → 自动交付`。
自动驱动不记任何判定偏差（那是手动驱动的产物），所以整条链路只有"时间到了"一个条件。
玩家先挑走的单（`Maker == Player`）老板既不接手也不代送（谁做谁送）。

**E. 端到端自跑通**

`单客闭环接上老板队列可以自己跑完` 这条用例把三个东西接起来跑：客人进店点单 → 老板 18 秒做出来 → 自动送到
客人当前座位 → 客人喝三口 → 结单 → 离店放座，全程没有手工推进任何状态。

**验证**：`EditMode 59/59 通过`（`.scratch/editmode_05.xml`），其中 05 票新增 39 条。

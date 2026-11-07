using System;
using System.Collections.Generic;
using CoffeeShop.Orders;
using CoffeeShop.Seating;

namespace CoffeeShop.Customers
{
    /// <summary>
    /// 单客闭环的编排者（FR-NPC-02 最小版）。
    ///
    /// 为什么需要它：客人不认识座位表，订单簿不认识客人；两头都要动的时候必须有人按顺序发请求。
    /// 这里就是那个"状态拥有者的协调者"——对外只暴露操作请求（Spawn / PlaceOrder / Tick），
    /// 内部把三件事串起来：
    ///   客人阶段变化 → 占座/放座；
    ///   订单变成待送 → 按客人**当前**座位交付（FR-ORD-04）；
    ///   交付后 → 客人分次饮用 → 喝完整单完成、客人离店。
    ///
    /// 节拍用 `Tick()` 而不是计时器：领域层不含时间，测试才能逐拍断言。
    /// </summary>
    public class CustomerDirector : ICustomerSeat
    {
        readonly SeatRegistry seats;
        readonly OrderBook orders;
        readonly List<Customer> customers = new List<Customer>();

        /// <summary>客人上不上吧台凳。默认不上——吧凳是留给玩家的位置（客人优先找桌子）。</summary>
        public bool AllowBarStool { get; set; }

        public IReadOnlyList<Customer> Customers => customers;
        public SeatRegistry Seats => seats;
        public OrderBook Orders => orders;

        /// <summary>一次交付尝试的结果，供测试与日志断言。</summary>
        public event Action<string, bool, string> DeliveryAttempted;

        public CustomerDirector(SeatRegistry seats, OrderBook orders)
        {
            this.seats = seats ?? throw new ArgumentNullException(nameof(seats));
            this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
            orders.Seats = this;   // 订单簿通过这里问"客人现在坐哪"
        }

        // ---- ICustomerSeat ----

        public string SeatOf(string customerId)
        {
            var c = Find(customerId);
            return c?.SeatId;
        }

        public Customer Find(string customerId)
        {
            if (string.IsNullOrWhiteSpace(customerId)) return null;
            for (int i = 0; i < customers.Count; i++)
                if (string.Equals(customers[i].Id, customerId, StringComparison.Ordinal)) return customers[i];
            return null;
        }

        /// <summary>客人到店：登记并直接算"已进门"（场景表现层负责把模型挪进店内）。</summary>
        public Customer Spawn(string customerId, string sku, int drinkers = 1, bool enters = true)
        {
            if (Find(customerId) != null) throw new InvalidOperationException($"客人 id 重复: {customerId}");
            var c = new Customer(customerId, sku, drinkers);
            customers.Add(c);
            if (enters) c.TryEnter();
            return c;
        }

        /// <summary>点单：进订单簿，并把客人推进到"找空位"。</summary>
        public Order PlaceOrder(string customerId)
        {
            var c = Find(customerId) ?? throw new InvalidOperationException($"没有这个客人: {customerId}");
            if (c.Phase != CustomerPhase.Entering)
                throw new InvalidOperationException($"{customerId} 当前阶段不能点单: {c.Phase}");

            var order = orders.Place(c.Id, c.Sku, c.Drinkers);
            if (!c.TryBeginOrder(order.Id))
                throw new InvalidOperationException($"{customerId} 无法进入点单阶段");
            c.TrySeekSeat();
            return order;
        }

        /// <summary>
        /// 推进一拍。顺序刻意固定：
        /// 1) 找座/换座；2) 待送的单按当前座位交付；3) 喝一口；4) 喝完离店、放座、结单。
        /// 固定顺序 = 可复现，测试不用猜内部顺序。
        /// </summary>
        public void Tick()
        {
            for (int i = 0; i < customers.Count; i++)
            {
                var c = customers[i];
                switch (c.Phase)
                {
                    case CustomerPhase.SeekingSeat:
                    case CustomerPhase.WaitingForSeat:
                        TrySit(c);
                        break;

                    case CustomerPhase.SeatedWaiting:
                        c.TickWaiting();
                        HandleDining(c);
                        break;

                    case CustomerPhase.Drinking:
                        TakeSip(c);
                        break;

                    case CustomerPhase.Leaving:
                        ReleaseSeat(c);
                        c.TryBeGone();
                        break;
                }
            }
        }

        public void Tick(int times)
        {
            for (int i = 0; i < times; i++) Tick();
        }

        void TrySit(Customer c)
        {
            var seat = PickSeat();
            if (seat == null)
            {
                // 没空位：在原地等（FR-NPC-05）。不催、不走、不降评价（FR-ORD-05）。
                if (c.Phase == CustomerPhase.SeekingSeat) c.TryWaitForSeat("无空位");
                c.TickWaiting();   // 踏进等候的那一拍也算在等
                return;
            }

            // 占位是被拒绝也不意外的操作：可能刚被别的客人抢走
            if (!seats.TryOccupy(seat.Id, c.Id)) { c.TickWaiting(); return; }

            if (c.Phase == CustomerPhase.WaitingForSeat)
            {
                c.TrySeekSeat();          // 等候 → 找座（有座位了才往回走）
            }
            if (!c.TrySitDown(seat.Id))
            {
                seats.Release(seat.Id, c.Id);   // 阶段没走通就把座位还回去，不能占着不动
                c.TickWaiting();
            }
        }

        /// <summary>
        /// 客人坐着的时候每拍检查一次单子：做好了就送，送到手就开喝。
        /// 拆成"看状态"而不是"我交付成功才开喝"，是因为老板队列会自己把单交付掉——
        /// 客人该响应的是**结果**（已交付），不是某一次调用的返回值。
        /// </summary>
        void HandleDining(Customer c)
        {
            var order = orders.OpenOrderOf(c.Id);
            if (order == null) return;

            if (order.Status == OrderStatus.Ready)
            {
                bool ok = orders.Deliver(order.Id);
                DeliveryAttempted?.Invoke(order.Id, ok, c.SeatId ?? "");
            }

            if (order.Status == OrderStatus.Delivered)
                c.TryStartDrinking();   // 客人还没坐稳时送不出去，状态没变这里也不会动
        }

        void TakeSip(Customer c)
        {
            var order = orders.Find(c.OrderId);
            if (order == null) { c.TryLeave(); return; }

            if (order.Status == OrderStatus.Delivered)
            {
                orders.Sip(order.Id);      // 没喝满仍是已交付
                return;
            }

            if (order.Status == OrderStatus.Drunk)
            {
                orders.Complete(order.Id); // 喝完 → 结单 → 走人
                c.TryLeave();
            }
        }

        void ReleaseSeat(Customer c)
        {
            if (!string.IsNullOrEmpty(c.SeatId)) seats.Release(c.SeatId, c.Id);
        }

        /// <summary>
        /// 挑座位：先挑非吧凳（客人坐桌子），一个都没有才退而求其次。
        /// 只在**空闲**座位里挑，所以"不抢已占用座位"是结构性保证，不靠调用方自觉。
        /// </summary>
        public SeatState PickSeat()
        {
            SeatState fallback = null;
            for (int i = 0; i < seats.All.Count; i++)
            {
                var s = seats.All[i];
                if (!s.IsFree) continue;
                if (s.Kind != SeatKind.BarStool) return s;
                if (fallback == null) fallback = s;
            }
            return AllowBarStool ? fallback : null;
        }

        /// <summary>客人提前给中断（测试/异常用）：放座、作废未结的单。</summary>
        public int Abandon(string customerId, string reason)
        {
            var c = Find(customerId);
            if (c == null) return 0;
            ReleaseSeat(c);
            // SeekingSeat 不能直接离开（那张图里的"等待中不离开"是硬规则），
            // 所以先落到等候再走，全程走的都是合法边。
            if (!c.TryLeave())
            {
                c.TryWaitForSeat("放弃等位");
                c.TryLeave();
            }
            c.TryBeGone();
            return orders.AbandonAllOf(customerId, reason);
        }

        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(orders.Describe()).Append(" | ");
            for (int i = 0; i < customers.Count; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(customers[i]);
            }
            return sb.ToString();
        }
    }
}

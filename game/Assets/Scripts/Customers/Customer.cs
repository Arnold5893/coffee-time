using System;
using System.Collections.Generic;

namespace CoffeeShop.Customers
{
    /// <summary>
    /// 一名客人的阶段（FR-NPC-02 单客最小闭环）：
    /// 进店 → 前台点单 → 找空位落座 → 等餐 → 分次饮用 → 离开。
    ///
    /// 没有座位时停在 `WaitingForSeat`（FR-NPC-05），并且**不离开、不催促**（FR-ORD-05）——
    /// 这一条是硬规则：阶段卡在等候时，`Complaints` 计时器也不会涨。
    /// </summary>
    public enum CustomerPhase
    {
        Outside = 0,        // 还在门外
        Entering = 1,       // 刚推门进来
        Ordering = 2,       // 在前台点单
        SeekingSeat = 3,    // 找空位
        WaitingForSeat = 4, // 没空位，在门口/墙边等
        SeatedWaiting = 5,  // 已落座，等餐
        Drinking = 6,       // 已交付，分次饮用
        Leaving = 7,        // 起身离店
        Gone = 8,           // 已离店（终态）
    }

    public readonly struct CustomerEvent
    {
        public readonly string CustomerId;
        public readonly CustomerPhase From;
        public readonly CustomerPhase To;
        public readonly string Note;

        public CustomerEvent(string customerId, CustomerPhase from, CustomerPhase to, string note)
        {
            CustomerId = customerId;
            From = from;
            To = to;
            Note = note ?? string.Empty;
        }

        public override string ToString() =>
            $"{CustomerId}: {From}→{To}{(Note.Length > 0 ? " (" + Note + ")" : "")}";
    }

    /// <summary>
    /// 一名客人的状态拥有者。只管"我在哪个阶段、坐哪、点了什么"，
    /// 落座的实际占位、订单的创建与交付由 <see cref="CustomerDirector"/> 编排。
    /// </summary>
    public class Customer
    {
        public string Id { get; }
        public CustomerPhase Phase { get; private set; }
        public string SeatId { get; private set; }
        public string OrderId { get; private set; }
        public string Sku { get; }
        public int Drinkers { get; }

        /// <summary>催过几次。竖切里永远是 0——等待期间客人不催促（FR-ORD-05）。</summary>
        public int Complaints { get; private set; }

        /// <summary>在"等"的状态里过了几个 tick，用来断言"等很久也不催、不走"。</summary>
        public int WaitedTicks { get; private set; }

        readonly List<CustomerEvent> history = new List<CustomerEvent>();
        public IReadOnlyList<CustomerEvent> History => history;

        public event Action<CustomerEvent> Changed;

        public bool IsInside => Phase != CustomerPhase.Outside && Phase != CustomerPhase.Gone;
        public bool IsFinished => Phase == CustomerPhase.Gone;

        public Customer(string id, string sku, int drinkers = 1)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("客人 id 不能为空", nameof(id));
            if (drinkers < 1 || drinkers > 2) throw new ArgumentOutOfRangeException(nameof(drinkers), "饮用人数只能是 1 或 2");
            Id = id;
            Sku = sku ?? string.Empty;
            Drinkers = drinkers;
            Phase = CustomerPhase.Outside;
        }

        /// <summary>阶段合法图。Director 与测试共用，避免两处各写一套。</summary>
        public static bool IsLegal(CustomerPhase from, CustomerPhase to)
        {
            switch (from)
            {
                case CustomerPhase.Outside: return to == CustomerPhase.Entering;
                case CustomerPhase.Entering: return to == CustomerPhase.Ordering;
                case CustomerPhase.Ordering: return to == CustomerPhase.SeekingSeat;
                case CustomerPhase.SeekingSeat:
                    return to == CustomerPhase.WaitingForSeat || to == CustomerPhase.SeatedWaiting;
                case CustomerPhase.WaitingForSeat:
                    return to == CustomerPhase.SeekingSeat || to == CustomerPhase.Leaving;
                case CustomerPhase.SeatedWaiting:
                    return to == CustomerPhase.Drinking || to == CustomerPhase.Leaving;
                case CustomerPhase.Drinking: return to == CustomerPhase.Leaving;
                case CustomerPhase.Leaving: return to == CustomerPhase.Gone;
                default: return false;   // Gone 是终态
            }
        }

        public bool TryEnter() => Move(CustomerPhase.Entering, null);

        public bool TryBeginOrder(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId)) return false;
            if (!Move(CustomerPhase.Ordering, orderId)) return false;
            OrderId = orderId;
            return true;
        }

        public bool TrySeekSeat() => Move(CustomerPhase.SeekingSeat, null);

        public bool TryWaitForSeat(string note) => Move(CustomerPhase.WaitingForSeat, note);

        /// <summary>占好座位落座。座位 id 由 Director 拿到后再告知，客人本身不碰 SeatRegistry。</summary>
        public bool TrySitDown(string seatId)
        {
            if (string.IsNullOrWhiteSpace(seatId)) return false;
            if (!Move(CustomerPhase.SeatedWaiting, seatId)) return false;
            SeatId = seatId;
            return true;
        }

        public bool TryStartDrinking() => Move(CustomerPhase.Drinking, null);

        public bool TryLeave() => Move(CustomerPhase.Leaving, null);

        public bool TryBeGone()
        {
            if (!Move(CustomerPhase.Gone, null)) return false;
            SeatId = null;   // 走了就不再占座位
            return true;
        }

        /// <summary>
        /// 一个"等"的节拍。只累加计数，不改阶段、不催——这是 FR-ORD-05 的领域层落点。
        /// </summary>
        public void TickWaiting()
        {
            if (Phase == CustomerPhase.SeekingSeat || Phase == CustomerPhase.WaitingForSeat ||
                Phase == CustomerPhase.SeatedWaiting)
                WaitedTicks++;
        }

        /// <summary>催一下。竖切里没人调用它（老板不会催、客人不会催），留着是为了测试能证明"没被调用"。</summary>
        public void Complain() => Complaints++;

        bool Move(CustomerPhase to, string note)
        {
            if (!IsLegal(Phase, to)) return false;
            var from = Phase;
            Phase = to;
            var e = new CustomerEvent(Id, from, to, note);
            history.Add(e);
            Changed?.Invoke(e);
            return true;
        }

        public override string ToString() =>
            $"{Id}({Sku},{Drinkers}人,{Phase},seat={SeatId ?? "-"},order={OrderId ?? "-"},waited={WaitedTicks},complaints={Complaints})";
    }
}

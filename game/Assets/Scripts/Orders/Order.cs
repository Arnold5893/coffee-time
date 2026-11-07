using System;
using System.Collections.Generic;

namespace CoffeeShop.Orders
{
    /// <summary>
    /// 订单状态（FR-ORD-03 的主链，按顺序推进）：
    /// 待办 → 已被认领 → 制作中 → 待送 → 已交付 → 已饮用 → 完成。
    ///
    /// `Abandoned` 是主链之外的兜底态：客人没等到就走了 / 订单作废。
    /// 竖切要求"任何中断后都有明确状态"，没有这个兜底就会出现一张永远挂在待办里的单。
    /// </summary>
    public enum OrderStatus
    {
        Pending = 0,    // 待办：已下单，没人认领
        Claimed = 1,    // 已被认领：玩家挑走了，或老板接手了（Maker 记是谁）
        Making = 2,     // 制作中
        Ready = 3,      // 待送：做好了，等端过去
        Delivered = 4,  // 已交付：已经放到客人桌上
        Drunk = 5,      // 已饮用：所有饮用人都喝完了
        Done = 6,       // 完成：可以结单归档
        Abandoned = 7,  // 兜底：客人走了 / 作废
    }

    /// <summary>谁在做这单。玩家做与老板做走同一份配方，这里只记身份（FR-MAKE-02、FR-HELP-02）。</summary>
    public enum OrderActor
    {
        None = 0,
        Boss = 1,
        Player = 2,
    }

    /// <summary>一次状态变化。只读结构体，供动画 / 音频 / UI / 测试订阅。</summary>
    public readonly struct OrderEvent
    {
        public readonly string OrderId;
        public readonly OrderStatus From;
        public readonly OrderStatus To;
        public readonly string Note;

        public OrderEvent(string orderId, OrderStatus from, OrderStatus to, string note)
        {
            OrderId = orderId;
            From = from;
            To = to;
            Note = note ?? string.Empty;
        }

        public override string ToString() => $"{OrderId}: {From}→{To}{(Note.Length > 0 ? " (" + Note + ")" : "")}";
    }

    /// <summary>
    /// 一张订单的状态拥有者（FR-ORD-02 / FR-ORD-03）。
    ///
    /// 规则：
    ///   - 状态只能沿着主链一格一格走，跳步一律失败并保持原状；
    ///   - 认领必须带身份（老板 / 玩家），认领后不可换人；
    ///   - 交付必须带座位 id（送达点＝客人当前座位，由 OrderBook 解析，见 FR-ORD-04）；
    ///   - 饮用按"口"记，喝满「饮用人数 × 每人几口」才算已饮用（FR-ORD-02 的 1/2 人共用一杯）；
    ///   - 失败的操作不改任何状态、不发事件——测试可以直接断言"非法转换是空操作"。
    /// </summary>
    public class Order
    {
        /// <summary>每位饮用人的口数。竖切只用来把"分次饮用"拆成可断言的几步，不做真实液体模拟。</summary>
        public const int SipsPerDrinker = 3;

        public string Id { get; }
        public string CustomerId { get; }
        public string Sku { get; }

        /// <summary>饮用人数：1 或 2（FR-ORD-02）。</summary>
        public int Drinkers { get; }

        public OrderStatus Status { get; private set; }
        public OrderActor Maker { get; private set; }
        public string DeliveredSeatId { get; private set; }
        public int SipsTaken { get; private set; }

        /// <summary>已经喝掉的总口数目标。</summary>
        public int SipsTotal => Drinkers * SipsPerDrinker;

        public bool IsOpen => Status != OrderStatus.Done && Status != OrderStatus.Abandoned;
        public bool IsFinished => !IsOpen;

        readonly List<OrderEvent> history = new List<OrderEvent>();

        /// <summary>状态变化事件（领域事件出口）。</summary>
        public event Action<OrderEvent> Changed;

        public IReadOnlyList<OrderEvent> History => history;

        public Order(string id, string customerId, string sku, int drinkers = 1)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("订单 id 不能为空", nameof(id));
            if (string.IsNullOrWhiteSpace(customerId)) throw new ArgumentException("订单必须绑定客人", nameof(customerId));
            if (drinkers < 1 || drinkers > 2) throw new ArgumentOutOfRangeException(nameof(drinkers), "饮用人数只能是 1 或 2");

            Id = id;
            CustomerId = customerId;
            Sku = sku ?? string.Empty;
            Drinkers = drinkers;
            Status = OrderStatus.Pending;
        }

        /// <summary>认领（待办 → 已被认领）。</summary>
        public bool TryClaim(OrderActor actor)
        {
            if (actor == OrderActor.None) return false;
            if (!TryMove(OrderStatus.Claimed, actor.ToString())) return false;
            Maker = actor;
            return true;
        }

        /// <summary>开始做（已被认领 → 制作中）。</summary>
        public bool TryStartMaking() => TryMove(OrderStatus.Making, null);

        /// <summary>做好了（制作中 → 待送）。</summary>
        public bool TryFinishMaking() => TryMove(OrderStatus.Ready, null);

        /// <summary>交付到座位（待送 → 已交付）。座位 id 为空表示还没落座，交付失败但状态不变。</summary>
        public bool TryDeliver(string seatId)
        {
            if (string.IsNullOrWhiteSpace(seatId)) return false;
            if (!TryMove(OrderStatus.Delivered, seatId)) return false;
            DeliveredSeatId = seatId;
            return true;
        }

        /// <summary>
        /// 喝一口（已交付 → 已饮用）。没喝满返回 false 但状态仍是已交付，
        /// 这样"分次饮用"在领域层就是可断言的：第 3/6 口才翻成已饮用。
        /// </summary>
        public bool TrySip()
        {
            if (Status != OrderStatus.Delivered) return false;
            if (SipsTaken < SipsTotal) SipsTaken++;
            if (SipsTaken < SipsTotal) return false;
            return TryMove(OrderStatus.Drunk, $"sips={SipsTaken}/{SipsTotal}");
        }

        /// <summary>结单归档（已饮用 → 完成）。</summary>
        public bool TryComplete() => TryMove(OrderStatus.Done, null);

        /// <summary>兜底作废（客人走了 / 中断）。已交付之后仍可作废——客人走了没人喝，单不能挂着。</summary>
        public bool TryAbandon(string reason)
        {
            if (IsFinished) return false;
            return TryMove(OrderStatus.Abandoned, reason);
        }

        /// <summary>主链合法图。测试直接对着它断言，UI 禁用按钮也读它，避免两处写两套规则。</summary>
        public static bool IsLegal(OrderStatus from, OrderStatus to)
        {
            switch (from)
            {
                case OrderStatus.Pending: return to == OrderStatus.Claimed || to == OrderStatus.Abandoned;
                case OrderStatus.Claimed: return to == OrderStatus.Making || to == OrderStatus.Abandoned;
                case OrderStatus.Making: return to == OrderStatus.Ready || to == OrderStatus.Abandoned;
                case OrderStatus.Ready: return to == OrderStatus.Delivered || to == OrderStatus.Abandoned;
                case OrderStatus.Delivered: return to == OrderStatus.Drunk || to == OrderStatus.Abandoned;
                case OrderStatus.Drunk: return to == OrderStatus.Done;
                default: return false;   // Done / Abandoned 是终态
            }
        }

        bool TryMove(OrderStatus to, string note)
        {
            if (!IsLegal(Status, to)) return false;
            var from = Status;
            Status = to;
            var e = new OrderEvent(Id, from, to, note);
            history.Add(e);
            Changed?.Invoke(e);
            return true;
        }

        public override string ToString() =>
            $"{Id}({Sku},{Drinkers}人,{Status},maker={Maker},seat={DeliveredSeatId ?? "-"},sips={SipsTaken}/{SipsTotal})";
    }
}

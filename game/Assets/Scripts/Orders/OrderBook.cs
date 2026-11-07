using System;
using System.Collections.Generic;

namespace CoffeeShop.Orders
{
    /// <summary>
    /// "这个客人现在坐哪"的查询口（FR-ORD-02 送达点为客人当前座位）。
    /// 单独抽成接口，是为了让订单域完全不认识座位系统与场景——
    /// 测试里塞一个假实现就能验证"客人换座后送达点跟着变"。
    /// </summary>
    public interface ICustomerSeat
    {
        /// <summary>返回客人当前座位 id；没落座返回 null 或空串。</summary>
        string SeatOf(string customerId);
    }

    /// <summary>
    /// 订单簿：所有订单的状态拥有者入口。
    ///
    /// 为什么不再让外部直接操作 Order：交付要解析"客人当前座位"，认领要分配身份，
    /// 这些规则一旦散到调用方就会各写一遍。这里收成唯一入口，外部只能发操作请求。
    /// </summary>
    public class OrderBook
    {
        readonly List<Order> orders = new List<Order>();
        readonly Dictionary<string, Order> byId = new Dictionary<string, Order>(StringComparer.Ordinal);
        int seq;

        /// <summary>客人座位查询口。没接的时候交付一律失败（宁可送不出去，也不送到错的座位）。</summary>
        public ICustomerSeat Seats { get; set; }

        /// <summary>下单事件（新订单进簿）。</summary>
        public event Action<Order> Placed;

        /// <summary>任意订单的状态变化，转发给 UI / 音频 / 老板队列订阅。</summary>
        public event Action<OrderEvent> Changed;

        public IReadOnlyList<Order> All => orders;

        public int Count => orders.Count;

        /// <summary>还没走完（未完成 / 未作废）的单。</summary>
        public int OpenCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < orders.Count; i++) if (orders[i].IsOpen) n++;
                return n;
            }
        }

        /// <summary>下单。id 自动按序生成，便于测试断言与日志排序。</summary>
        public Order Place(string customerId, string sku, int drinkers = 1)
        {
            var order = new Order($"ord-{++seq:000}", customerId, sku, drinkers);
            order.Changed += e => Changed?.Invoke(e);
            orders.Add(order);
            byId.Add(order.Id, order);
            Placed?.Invoke(order);
            return order;
        }

        public Order Find(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId)) return null;
            return byId.TryGetValue(orderId, out var o) ? o : null;
        }

        /// <summary>某客人当前还开着的单（一人一单的最小版：有多张时返回第一张）。</summary>
        public Order OpenOrderOf(string customerId)
        {
            for (int i = 0; i < orders.Count; i++)
            {
                if (orders[i].IsOpen && string.Equals(orders[i].CustomerId, customerId, StringComparison.Ordinal))
                    return orders[i];
            }
            return null;
        }

        public bool Claim(string orderId, OrderActor actor)
        {
            var o = Find(orderId);
            return o != null && o.TryClaim(actor);
        }

        public bool StartMaking(string orderId)
        {
            var o = Find(orderId);
            return o != null && o.TryStartMaking();
        }

        public bool FinishMaking(string orderId)
        {
            var o = Find(orderId);
            return o != null && o.TryFinishMaking();
        }

        /// <summary>
        /// 交付：座位不是参数，而是此刻向 ICustomerSeat 问出来的（FR-ORD-04 客人没落座就先不送）。
        /// 客人还没坐下 → 返回 false，订单留在"待送"，等下一帧再试。
        /// </summary>
        public bool Deliver(string orderId)
        {
            var o = Find(orderId);
            if (o == null) return false;
            var seat = Seats?.SeatOf(o.CustomerId);
            return o.TryDeliver(seat);
        }

        public bool Sip(string orderId)
        {
            var o = Find(orderId);
            return o != null && o.TrySip();
        }

        public bool Complete(string orderId)
        {
            var o = Find(orderId);
            return o != null && o.TryComplete();
        }

        /// <summary>作废某客人所有未结的单（客人提前离开时的兜底，避免单永远挂在待办里）。</summary>
        public int AbandonAllOf(string customerId, string reason)
        {
            int n = 0;
            for (int i = 0; i < orders.Count; i++)
            {
                var o = orders[i];
                if (!o.IsOpen) continue;
                if (!string.Equals(o.CustomerId, customerId, StringComparison.Ordinal)) continue;
                if (o.TryAbandon(reason)) n++;
            }
            return n;
        }

        public string Describe()
        {
            var counts = new int[8];
            for (int i = 0; i < orders.Count; i++) counts[(int)orders[i].Status]++;
            return $"orders={Count} open={OpenCount} " +
                   $"pending={counts[0]} claimed={counts[1]} making={counts[2]} ready={counts[3]} " +
                   $"delivered={counts[4]} drunk={counts[5]} done={counts[6]} abandoned={counts[7]}";
        }
    }
}

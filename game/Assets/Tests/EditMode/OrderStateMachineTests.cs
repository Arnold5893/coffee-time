using System.Linq;
using CoffeeShop.Orders;
using NUnit.Framework;

namespace CoffeeShop.Tests
{
    /// <summary>
    /// 05 票 / FR-ORD-03：订单状态机。
    /// 全部是纯对象，不加载场景。
    /// </summary>
    public class OrderStateMachineTests
    {
        static Order NewOrder(int drinkers = 1) => new Order("ord-001", "cust-1", "latte", drinkers);

        [Test]
        public void 新订单是待办且绑定客人()
        {
            var o = NewOrder();
            Assert.AreEqual(OrderStatus.Pending, o.Status);
            Assert.AreEqual("cust-1", o.CustomerId);
            Assert.IsTrue(o.IsOpen);
            Assert.AreEqual(OrderActor.None, o.Maker);
            Assert.AreEqual(0, o.History.Count, "刚建的单不该有状态事件");
        }

        [Test]
        public void 饮用人数只能是1或2()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new Order("x", "c", "latte", 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new Order("x", "c", "latte", 3));
            Assert.AreEqual(2, NewOrder(2).Drinkers);
        }

        [Test]
        public void 主链七步可以一路走到底()
        {
            var o = NewOrder();
            Assert.IsTrue(o.TryClaim(OrderActor.Player));
            Assert.AreEqual(OrderStatus.Claimed, o.Status);
            Assert.IsTrue(o.TryStartMaking());
            Assert.AreEqual(OrderStatus.Making, o.Status);
            Assert.IsTrue(o.TryFinishMaking());
            Assert.AreEqual(OrderStatus.Ready, o.Status);
            Assert.IsTrue(o.TryDeliver("sun-3"));
            Assert.AreEqual(OrderStatus.Delivered, o.Status);
            Assert.IsFalse(o.TrySip(), "第一口还没喝完");
            Assert.IsFalse(o.TrySip(), "第二口还没喝完");
            Assert.IsTrue(o.TrySip(), "第三口喝满");
            Assert.AreEqual(OrderStatus.Drunk, o.Status);
            Assert.IsTrue(o.TryComplete());
            Assert.AreEqual(OrderStatus.Done, o.Status);
            Assert.IsFalse(o.IsOpen);
        }

        [Test]
        public void 每一步都发一条领域事件且顺序正确()
        {
            var o = NewOrder();
            var seen = new System.Collections.Generic.List<OrderStatus>();
            o.Changed += e => seen.Add(e.To);

            o.TryClaim(OrderActor.Boss);
            o.TryStartMaking();
            o.TryFinishMaking();
            o.TryDeliver("round-1");
            o.TrySip(); o.TrySip(); o.TrySip();
            o.TryComplete();

            CollectionAssert.AreEqual(
                new[]
                {
                    OrderStatus.Claimed, OrderStatus.Making, OrderStatus.Ready,
                    OrderStatus.Delivered, OrderStatus.Drunk, OrderStatus.Done,
                }, seen);
        }

        [TestCase(OrderStatus.Making)]
        [TestCase(OrderStatus.Ready)]
        [TestCase(OrderStatus.Delivered)]
        [TestCase(OrderStatus.Drunk)]
        [TestCase(OrderStatus.Done)]
        public void 待办不能跳步(OrderStatus target)
        {
            var o = NewOrder();
            Assert.IsFalse(Order.IsLegal(OrderStatus.Pending, target));
            Assert.IsFalse(DoTransition(o, target), $"待办不该能直接到 {target}");
            Assert.AreEqual(OrderStatus.Pending, o.Status, "非法转换必须是空操作");
            Assert.AreEqual(0, o.History.Count, "非法转换不能发事件");
        }

        [Test]
        public void 制作中不能直接交付也不能回头认领()
        {
            var o = NewOrder();
            o.TryClaim(OrderActor.Player);
            o.TryStartMaking();

            Assert.IsFalse(o.TryDeliver("sun-1"), "没做完不能送");
            Assert.IsFalse(o.TryClaim(OrderActor.Boss), "已经认领过就不能再认领");
            Assert.AreEqual(OrderStatus.Making, o.Status);
            Assert.AreEqual(OrderActor.Player, o.Maker, "认领人不能被换掉");
        }

        [Test]
        public void 认领必须带身份()
        {
            var o = NewOrder();
            Assert.IsFalse(o.TryClaim(OrderActor.None), "没身份等于没认领");
            Assert.AreEqual(OrderStatus.Pending, o.Status);
            Assert.IsTrue(o.TryClaim(OrderActor.Boss));
            Assert.AreEqual(OrderActor.Boss, o.Maker);
        }

        [Test]
        public void 交付必须带座位()
        {
            var o = NewOrder();
            o.TryClaim(OrderActor.Boss);
            o.TryStartMaking();
            o.TryFinishMaking();

            Assert.IsFalse(o.TryDeliver(null));
            Assert.IsFalse(o.TryDeliver("   "));
            Assert.AreEqual(OrderStatus.Ready, o.Status, "没座位就留在待送，不能假装送到了");
            Assert.IsTrue(o.TryDeliver("meeting-2"));
            Assert.AreEqual("meeting-2", o.DeliveredSeatId);
        }

        [Test]
        public void 单人一杯要喝满三口才算已饮用()
        {
            var o = new Order("ord-001", "cust-1", "latte", 1);
            o.TryClaim(OrderActor.Boss); o.TryStartMaking(); o.TryFinishMaking(); o.TryDeliver("bar-1");

            Assert.AreEqual(3, o.SipsTotal);
            Assert.IsFalse(o.TrySip(), "第一口");
            Assert.IsFalse(o.TrySip(), "第二口");
            Assert.AreEqual(OrderStatus.Delivered, o.Status, "没喝完不能算已饮用");
            Assert.IsTrue(o.TrySip(), "第三口");
            Assert.AreEqual(OrderStatus.Drunk, o.Status);
        }

        [Test]
        public void 两人共饮一杯要喝满六口()
        {
            var o = new Order("ord-001", "cust-1", "latte", 2);
            o.TryClaim(OrderActor.Boss); o.TryStartMaking(); o.TryFinishMaking(); o.TryDeliver("round-4");

            Assert.AreEqual(6, o.SipsTotal);
            for (int i = 0; i < 5; i++) Assert.IsFalse(o.TrySip(), $"第 {i + 1} 口不该喝满");
            Assert.IsTrue(o.TrySip(), "第 6 口");
            Assert.AreEqual(OrderStatus.Drunk, o.Status);
        }

        [Test]
        public void 没交付不能喝()
        {
            var o = NewOrder();
            o.TryClaim(OrderActor.Boss); o.TryStartMaking(); o.TryFinishMaking();
            Assert.IsFalse(o.TrySip());
            Assert.AreEqual(0, o.SipsTaken);
            Assert.AreEqual(OrderStatus.Ready, o.Status);
        }

        [Test]
        public void 终态不再接受任何转换()
        {
            var o = NewOrder();
            o.TryClaim(OrderActor.Player);
            o.TryStartMaking(); o.TryFinishMaking(); o.TryDeliver("sun-2");
            o.TrySip(); o.TrySip(); o.TrySip();
            o.TryComplete();

            Assert.IsFalse(o.TryAbandon("x"));
            Assert.IsFalse(o.TryComplete());
            Assert.AreEqual(OrderStatus.Done, o.Status);
        }

        [Test]
        public void 作废是兜底分支任何未完成状态都能走()
        {
            foreach (var prep in new System.Action<Order>[]
                     {
                         o => { },
                         o => o.TryClaim(OrderActor.Boss),
                         o => { o.TryClaim(OrderActor.Boss); o.TryStartMaking(); },
                         o => { o.TryClaim(OrderActor.Boss); o.TryStartMaking(); o.TryFinishMaking(); },
                         o => { o.TryClaim(OrderActor.Boss); o.TryStartMaking(); o.TryFinishMaking(); o.TryDeliver("bar-1"); },
                     })
            {
                var o = NewOrder();
                prep(o);
                Assert.IsTrue(o.TryAbandon("客人走了"), $"从 {o.Status} 应当能作废");
                Assert.AreEqual(OrderStatus.Abandoned, o.Status);
                Assert.IsFalse(o.IsOpen);
            }
        }

        [Test]
        public void 合法图与状态机实现一致()
        {
            var all = (OrderStatus[])System.Enum.GetValues(typeof(OrderStatus));
            foreach (var from in all)
            {
                foreach (var to in all)
                {
                    var o = DriveTo(from);
                    // "已交付 → 已饮用"要靠喝满口数，不是一次请求，单独按结果判定
                    bool actual;
                    if (from == OrderStatus.Delivered && to == OrderStatus.Drunk)
                    {
                        for (int i = 0; i < o.SipsTotal; i++) o.TrySip();
                        actual = o.Status == OrderStatus.Drunk;
                    }
                    else
                    {
                        actual = DoTransition(o, to);
                    }
                    Assert.AreEqual(Order.IsLegal(from, to), actual,
                        $"合法图与实现在 {from}→{to} 上不一致");
                }
            }
        }

        // ---- helpers ----

        static Order DriveTo(OrderStatus target)
        {
            var o = NewOrder(1);
            if (target == OrderStatus.Pending) return o;
            o.TryClaim(OrderActor.Boss);
            if (target == OrderStatus.Claimed) return o;
            o.TryStartMaking();
            if (target == OrderStatus.Making) return o;
            o.TryFinishMaking();
            if (target == OrderStatus.Ready) return o;
            o.TryDeliver("sun-1");
            if (target == OrderStatus.Delivered) return o;
            o.TrySip(); o.TrySip(); o.TrySip();
            if (target == OrderStatus.Drunk) return o;
            o.TryComplete();
            return o;
        }

        static bool DoTransition(Order o, OrderStatus to)
        {
            switch (to)
            {
                case OrderStatus.Claimed: return o.TryClaim(OrderActor.Boss);
                case OrderStatus.Making: return o.TryStartMaking();
                case OrderStatus.Ready: return o.TryFinishMaking();
                case OrderStatus.Delivered: return o.TryDeliver("sun-9");
                case OrderStatus.Drunk: return o.TrySip();
                case OrderStatus.Done: return o.TryComplete();
                case OrderStatus.Abandoned: return o.TryAbandon("test");
                default: return false;
            }
        }
    }
}

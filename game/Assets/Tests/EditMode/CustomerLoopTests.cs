using CoffeeShop.Customers;
using CoffeeShop.Orders;
using CoffeeShop.Seating;
using NUnit.Framework;

namespace CoffeeShop.Tests
{
    /// <summary>
    /// 05 票 / FR-NPC-02（单客最小闭环）、FR-NPC-05（无空位等候）、FR-ORD-05（等待期间不催促）。
    /// 全部纯对象，一拍一拍推进，不依赖时间。
    /// </summary>
    public class CustomerLoopTests
    {
        static SeatRegistry Seats(int tables = 3, int bars = 2)
        {
            var r = new SeatRegistry();
            for (int i = 1; i <= tables; i++) r.Add(new SeatState($"round-{i}", "round", SeatKind.Armchair));
            for (int i = 1; i <= bars; i++) r.Add(new SeatState($"bar-{i}", "bar", SeatKind.BarStool));
            return r;
        }

        static (CustomerDirector dir, OrderBook book, SeatRegistry seats) NewShop(int tables = 3, int bars = 2)
        {
            var seats = Seats(tables, bars);
            var book = new OrderBook();
            return (new CustomerDirector(seats, book), book, seats);
        }

        [Test]
        public void 单客完整闭环_进店点单落座等餐饮用离开()
        {
            var (dir, book, seats) = NewShop();
            var c = dir.Spawn("guest-1", "latte");
            Assert.AreEqual(CustomerPhase.Entering, c.Phase);

            var order = dir.PlaceOrder("guest-1");
            Assert.AreEqual(CustomerPhase.SeekingSeat, c.Phase, "点完单就去找座位");
            Assert.AreEqual("guest-1", order.CustomerId, "订单必须绑在客人身上");

            dir.Tick();
            Assert.AreEqual(CustomerPhase.SeatedWaiting, c.Phase);
            Assert.IsFalse(string.IsNullOrEmpty(c.SeatId));
            Assert.AreEqual("guest-1", seats.Find(c.SeatId).Occupant, "落座要真的占住座位");
            var satOn = c.SeatId;

            // 老板做单（制作过程属 07 票，这里只推进订单状态）
            Assert.IsTrue(book.Claim(order.Id, OrderActor.Boss));
            Assert.IsTrue(book.StartMaking(order.Id));
            Assert.IsTrue(book.FinishMaking(order.Id));

            dir.Tick();
            Assert.AreEqual(CustomerPhase.Drinking, c.Phase, "待送的单会按客人当前座位交付");
            Assert.AreEqual(OrderStatus.Delivered, order.Status);
            Assert.AreEqual(c.SeatId, order.DeliveredSeatId);

            // 单人一杯三口
            dir.Tick();
            Assert.AreEqual(1, order.SipsTaken);
            dir.Tick();
            Assert.AreEqual(2, order.SipsTaken);
            dir.Tick();
            Assert.AreEqual(OrderStatus.Drunk, order.Status, "第三口喝满");
            Assert.AreEqual(CustomerPhase.Drinking, c.Phase, "喝完这一拍还没起身");

            dir.Tick();
            Assert.AreEqual(CustomerPhase.Leaving, c.Phase);
            Assert.AreEqual(OrderStatus.Done, order.Status, "喝完离店，单也要结掉");

            dir.Tick();
            Assert.AreEqual(CustomerPhase.Gone, c.Phase);
            Assert.IsTrue(seats.Find(satOn).IsFree, "走了要把座位放掉");
            Assert.AreEqual(0, book.OpenCount, "闭环结束后不该有挂着的单");
            Assert.AreEqual(0, c.Complaints);
        }

        [Test]
        public void 客人只坐桌子不抢吧台凳()
        {
            var (dir, _, seats) = NewShop(tables: 1, bars: 2);
            dir.Spawn("guest-1", "latte");
            dir.PlaceOrder("guest-1");
            dir.Tick();

            Assert.AreEqual("round-1", dir.Find("guest-1").SeatId);
            Assert.IsTrue(seats.Find("bar-1").IsFree, "吧凳是留给玩家的");
        }

        [Test]
        public void 客人不抢已占用座位()
        {
            var (dir, _, seats) = NewShop(tables: 2, bars: 1);
            Assert.IsTrue(seats.TryOccupy("round-1", "other-1"));
            Assert.IsTrue(seats.TryOccupy("round-2", "other-2"));

            dir.Spawn("guest-1", "latte");
            dir.PlaceOrder("guest-1");
            dir.Tick();

            Assert.AreEqual(CustomerPhase.WaitingForSeat, dir.Find("guest-1").Phase, "没空桌就在门口等");
            Assert.AreEqual("other-1", seats.Find("round-1").Occupant, "不能把别人挤走");
            Assert.AreEqual("other-2", seats.Find("round-2").Occupant);
            Assert.IsTrue(seats.Find("bar-1").IsFree, "默认也不去坐吧凳");
        }

        [Test]
        public void 无空位时一直等候_不离开不催促()
        {
            var (dir, book, seats) = NewShop(tables: 1, bars: 0);
            Assert.IsTrue(seats.TryOccupy("round-1", "other-1"));

            var c = dir.Spawn("guest-1", "latte");
            var order = dir.PlaceOrder("guest-1");

            dir.Tick(30);
            Assert.AreEqual(CustomerPhase.WaitingForSeat, c.Phase, "没座位就一直等，不能自己走");
            Assert.AreEqual(0, c.Complaints, "等待期间不催促（FR-ORD-05）");
            Assert.GreaterOrEqual(c.WaitedTicks, 30);
            Assert.AreEqual(OrderStatus.Pending, order.Status, "等座位不影响订单");
            Assert.AreEqual("other-1", seats.Find("round-1").Occupant);
        }

        [Test]
        public void 座位空出来后等候的客人落座()
        {
            var (dir, _, seats) = NewShop(tables: 1, bars: 0);
            Assert.IsTrue(seats.TryOccupy("round-1", "other-1"));

            var c = dir.Spawn("guest-1", "latte");
            dir.PlaceOrder("guest-1");
            dir.Tick(5);
            Assert.AreEqual(CustomerPhase.WaitingForSeat, c.Phase);

            Assert.IsTrue(seats.Release("round-1", "other-1"));
            dir.Tick();

            Assert.AreEqual(CustomerPhase.SeatedWaiting, c.Phase);
            Assert.AreEqual("round-1", c.SeatId);
            Assert.AreEqual("guest-1", seats.Find("round-1").Occupant);
        }

        [Test]
        public void 等餐期间不催促也不离开()
        {
            var (dir, book, _) = NewShop();
            var c = dir.Spawn("guest-1", "latte");
            var order = dir.PlaceOrder("guest-1");
            dir.Tick();

            book.Claim(order.Id, OrderActor.Boss);
            book.StartMaking(order.Id);

            dir.Tick(40);
            Assert.AreEqual(CustomerPhase.SeatedWaiting, c.Phase, "餐没好就一直坐着等");
            Assert.AreEqual(0, c.Complaints, "不催（FR-ORD-05）");
            Assert.AreEqual(OrderStatus.Making, order.Status, "等待不该反过来动订单");
            Assert.AreEqual(1, book.OpenCount);
        }

        [Test]
        public void 客人还没落座时订单做好也先不送()
        {
            var (dir, book, seats) = NewShop(tables: 1, bars: 0);
            Assert.IsTrue(seats.TryOccupy("round-1", "other-1"));

            var c = dir.Spawn("guest-1", "latte");
            var order = dir.PlaceOrder("guest-1");
            dir.Tick();

            book.Claim(order.Id, OrderActor.Boss);
            book.StartMaking(order.Id);
            book.FinishMaking(order.Id);

            dir.Tick(5);
            Assert.AreEqual(OrderStatus.Ready, order.Status, "客人没坐，单先挂在待送（FR-ORD-04）");
            Assert.AreEqual(CustomerPhase.WaitingForSeat, c.Phase);

            seats.Release("round-1", "other-1");
            dir.Tick();                                  // 落座
            Assert.AreEqual(CustomerPhase.SeatedWaiting, c.Phase);
            dir.Tick();                                  // 交付
            Assert.AreEqual(OrderStatus.Delivered, order.Status);
            Assert.AreEqual("round-1", order.DeliveredSeatId);
        }

        [Test]
        public void 客人提前离开时订单作废且座位放回()
        {
            var (dir, book, seats) = NewShop();
            var c = dir.Spawn("guest-1", "latte");
            var order = dir.PlaceOrder("guest-1");
            dir.Tick();
            var seat = c.SeatId;

            Assert.AreEqual(1, dir.Abandon("guest-1", "客人走了"));
            Assert.AreEqual(OrderStatus.Abandoned, order.Status);
            Assert.AreEqual(0, book.OpenCount);
            Assert.IsTrue(seats.Find(seat).IsFree, "人走了座位必须还回去，否则永远死锁");
            Assert.AreEqual(CustomerPhase.Gone, c.Phase);
        }

        [Test]
        public void 阶段图与实现一致()
        {
            var all = (CustomerPhase[])System.Enum.GetValues(typeof(CustomerPhase));
            foreach (var from in all)
            {
                foreach (var to in all)
                {
                    Assert.AreEqual(Expect(from, to), Customer.IsLegal(from, to), $"{from}→{to}");
                }
            }
        }

        static bool Expect(CustomerPhase from, CustomerPhase to)
        {
            switch (from)
            {
                case CustomerPhase.Outside: return to == CustomerPhase.Entering;
                case CustomerPhase.Entering: return to == CustomerPhase.Ordering;
                case CustomerPhase.Ordering: return to == CustomerPhase.SeekingSeat;
                case CustomerPhase.SeekingSeat: return to == CustomerPhase.WaitingForSeat || to == CustomerPhase.SeatedWaiting;
                case CustomerPhase.WaitingForSeat: return to == CustomerPhase.SeekingSeat || to == CustomerPhase.Leaving;
                case CustomerPhase.SeatedWaiting: return to == CustomerPhase.Drinking || to == CustomerPhase.Leaving;
                case CustomerPhase.Drinking: return to == CustomerPhase.Leaving;
                case CustomerPhase.Leaving: return to == CustomerPhase.Gone;
                default: return false;
            }
        }
    }
}

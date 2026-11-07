using CoffeeShop.Boss;
using CoffeeShop.Customers;
using CoffeeShop.Orders;
using CoffeeShop.Seating;
using NUnit.Framework;

namespace CoffeeShop.Tests
{
    /// <summary>
    /// 05 票 / 老板自动制作队列（FR-MAKE-02 自动驱动、FR-BOSS-01 雏形）。
    /// 计时靠喂 dt，测试可以一秒一秒推，不依赖真实时间。
    /// </summary>
    public class BossQueueTests
    {
        static OrderBook NewBook() => new OrderBook { Seats = new AlwaysSeated("sun-1") };

        class AlwaysSeated : ICustomerSeat
        {
            readonly string seat;
            public AlwaysSeated(string seat) { this.seat = seat; }
            public string SeatOf(string customerId) => seat;
        }

        [Test]
        public void 排队后要等够时间才做好()
        {
            var book = NewBook();
            var boss = new BossWorkQueue(book) { SecondsPerDrink = 18f, AutoDeliver = false };
            var o = book.Place("cust-1", "latte");

            Assert.IsTrue(boss.Enqueue(o.Id));
            boss.Tick(0.1f);
            Assert.AreEqual(OrderStatus.Making, o.Status, "老板一上手就是制作中");
            Assert.AreEqual(OrderActor.Boss, o.Maker);

            boss.Tick(10f);
            Assert.AreEqual(OrderStatus.Making, o.Status, "才 10 秒，没做完");
            Assert.That(boss.Progress, Is.InRange(0.4f, 0.7f));

            boss.Tick(8.5f);
            Assert.AreEqual(OrderStatus.Ready, o.Status, "到 18 秒才出杯");
            Assert.IsNull(boss.Current);
        }

        [Test]
        public void 做完自动送到客人当前座位()
        {
            var book = NewBook();
            var boss = new BossWorkQueue(book) { SecondsPerDrink = 5f };
            var o = book.Place("cust-1", "latte");

            boss.Enqueue(o.Id);
            boss.Tick(5f);
            Assert.AreEqual(OrderStatus.Delivered, o.Status);
            Assert.AreEqual("sun-1", o.DeliveredSeatId);
        }

        [Test]
        public void 玩家挑走的单老板不再做()
        {
            var book = NewBook();
            var boss = new BossWorkQueue(book) { SecondsPerDrink = 5f };
            var o = book.Place("cust-1", "latte");

            boss.Enqueue(o.Id);
            Assert.IsTrue(book.Claim(o.Id, OrderActor.Player), "玩家先挑走了");

            boss.Tick(0.1f);
            Assert.AreEqual(OrderActor.Player, o.Maker, "老板不能抢玩家的单");
            Assert.AreEqual(OrderStatus.Claimed, o.Status, "老板也不该替玩家开做");

            boss.Tick(30f);
            Assert.AreEqual(OrderStatus.Claimed, o.Status);
        }

        [Test]
        public void 客人还没落座时先挂着待送()
        {
            var seat = new MutableSeat();
            var book = new OrderBook { Seats = seat };
            var boss = new BossWorkQueue(book) { SecondsPerDrink = 5f };
            var o = book.Place("cust-1", "latte");

            boss.Enqueue(o.Id);
            boss.Tick(5f);
            Assert.AreEqual(OrderStatus.Ready, o.Status, "客人没坐，先不送（FR-ORD-04）");

            boss.Tick(5f);
            Assert.AreEqual(OrderStatus.Ready, o.Status, "还没坐，继续等");

            seat.Seat = "round-3";
            boss.Tick(1f);
            Assert.AreEqual(OrderStatus.Delivered, o.Status);
            Assert.AreEqual("round-3", o.DeliveredSeatId);
        }

        [Test]
        public void 玩家做的单老板不代送()
        {
            var book = NewBook();
            var boss = new BossWorkQueue(book) { SecondsPerDrink = 5f };
            var o = book.Place("cust-1", "latte");
            book.Claim(o.Id, OrderActor.Player);
            book.StartMaking(o.Id);
            book.FinishMaking(o.Id);

            boss.Tick(1f);
            Assert.AreEqual(OrderStatus.Ready, o.Status, "谁做谁送，老板只送自己的单");
        }

        [Test]
        public void 单客闭环接上老板队列可以自己跑完()
        {
            var seats = new SeatRegistry();
            seats.Add(new SeatState("round-1", "round", SeatKind.Armchair));
            var book = new OrderBook();
            var dir = new CustomerDirector(seats, book);
            var boss = new BossWorkQueue(book) { SecondsPerDrink = 18f };

            var c = dir.Spawn("guest-1", "latte");
            var order = dir.PlaceOrder("guest-1");

            Assert.IsTrue(boss.Enqueue(order.Id), "没人挑的单进老板队列");

            // 老板做 18 秒，客人自己找座等餐
            for (int i = 0; i < 17; i++) { boss.Tick(1f); dir.Tick(); }
            Assert.AreEqual(OrderStatus.Making, order.Status, "才 17 秒，没出杯");
            Assert.AreEqual(CustomerPhase.SeatedWaiting, c.Phase, "餐没好就坐着等");

            boss.Tick(1f); dir.Tick();     // 第 18 秒出杯 → 老板自动交付 → 客人开喝
            Assert.AreEqual(OrderStatus.Delivered, order.Status);
            Assert.AreEqual(CustomerPhase.Drinking, c.Phase);

            for (int i = 0; i < 10; i++) { boss.Tick(1f); dir.Tick(); }
            Assert.AreEqual(OrderStatus.Done, order.Status);
            Assert.AreEqual(CustomerPhase.Gone, c.Phase);
            Assert.IsTrue(seats.Find("round-1").IsFree);
            Assert.AreEqual(0, book.OpenCount);
        }

        class MutableSeat : ICustomerSeat
        {
            public string Seat;
            public string SeatOf(string customerId) => Seat;
        }
    }
}

using CoffeeShop.Orders;
using NUnit.Framework;

namespace CoffeeShop.Tests
{
    /// <summary>
    /// 05 票 / FR-ORD-02、FR-ORD-04：订单与客人绑定，送达点跟随客人**当前**座位。
    /// 用一个假的座位查询口，把订单域和座位系统彻底隔开。
    /// </summary>
    public class OrderDeliveryTests
    {
        class FakeSeat : ICustomerSeat
        {
            public string Seat;
            public string SeatOf(string customerId) => Seat;
        }

        static (OrderBook book, FakeSeat seats, Order order) ReadyOrder()
        {
            var seats = new FakeSeat();
            var book = new OrderBook { Seats = seats };
            var o = book.Place("cust-1", "latte", 1);
            Assert.IsTrue(book.Claim(o.Id, OrderActor.Boss));
            Assert.IsTrue(book.StartMaking(o.Id));
            Assert.IsTrue(book.FinishMaking(o.Id));
            return (book, seats, o);
        }

        [Test]
        public void 送达点等于客人当前座位()
        {
            var (book, seats, order) = ReadyOrder();
            seats.Seat = "sun-3";

            Assert.IsTrue(book.Deliver(order.Id));
            Assert.AreEqual("sun-3", order.DeliveredSeatId);
        }

        [Test]
        public void 客人换了座位送达点跟着换()
        {
            var (book, seats, order) = ReadyOrder();
            seats.Seat = "round-1";
            Assert.IsTrue(book.Deliver(order.Id));
            Assert.AreEqual("round-1", order.DeliveredSeatId);

            // 第二张单：客人在同一次停留里换了座位，送达点必须按新座位算
            var o2 = book.Place("cust-1", "americano", 1);
            book.Claim(o2.Id, OrderActor.Player);
            book.StartMaking(o2.Id);
            book.FinishMaking(o2.Id);
            seats.Seat = "meeting-5";
            Assert.IsTrue(book.Deliver(o2.Id));
            Assert.AreEqual("meeting-5", o2.DeliveredSeatId);
        }

        [Test]
        public void 客人还没落座就送不出去且订单停在待送()
        {
            var (book, seats, order) = ReadyOrder();
            seats.Seat = null;

            Assert.IsFalse(book.Deliver(order.Id));
            Assert.AreEqual(OrderStatus.Ready, order.Status, "没座位要留在待送，不能凭空交付");
            Assert.IsNull(order.DeliveredSeatId);

            seats.Seat = "bar-2";
            Assert.IsTrue(book.Deliver(order.Id), "客人坐下后同一张单还能继续送");
            Assert.AreEqual("bar-2", order.DeliveredSeatId);
        }

        [Test]
        public void 没有接到座位查询口时一律不交付()
        {
            var book = new OrderBook();
            var o = book.Place("cust-1", "latte");
            book.Claim(o.Id, OrderActor.Boss);
            book.StartMaking(o.Id);
            book.FinishMaking(o.Id);

            Assert.IsFalse(book.Deliver(o.Id));
            Assert.AreEqual(OrderStatus.Ready, o.Status);
        }

        [Test]
        public void 订单簿按客人找单()
        {
            var book = new OrderBook { Seats = new FakeSeat() };
            var a = book.Place("cust-1", "latte");
            var b = book.Place("cust-2", "flatwhite");

            Assert.AreEqual(a, book.OpenOrderOf("cust-1"));
            Assert.AreEqual(b, book.OpenOrderOf("cust-2"));
            Assert.IsNull(book.OpenOrderOf("cust-9"));
            Assert.AreEqual(2, book.OpenCount);
        }

        [Test]
        public void 作废只影响该客人的单()
        {
            var book = new OrderBook { Seats = new FakeSeat() };
            var a = book.Place("cust-1", "latte");
            var b = book.Place("cust-2", "flatwhite");

            Assert.AreEqual(1, book.AbandonAllOf("cust-1", "客人走了"));
            Assert.AreEqual(OrderStatus.Abandoned, a.Status);
            Assert.AreEqual(OrderStatus.Pending, b.Status, "别人的单不能连坐");
            Assert.AreEqual(1, book.OpenCount);
        }
    }
}

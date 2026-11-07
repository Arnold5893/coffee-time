using System.Collections.Generic;
using CoffeeShop.Orders;

namespace CoffeeShop.Boss
{
    /// <summary>
    /// 老板的自动制作队列（FR-MAKE-02 的自动驱动、FR-BOSS-01 的雏形）。
    ///
    /// 竖切里玩家挑剩的单由老板真实做掉——所以这里必须有**计时**，而不是立刻把单推到待送：
    /// 玩家抬头能看到老板确实在忙，店才是活的（故事 21）。
    ///
    /// 自动驱动不记录任何判定偏差（那是手动驱动的产物），所以整条链路只有"时间到了"一个条件。
    /// 计时用外部喂进来的 `dt`，不用 Unity 的 Time —— 领域层不含时钟，测试可以一秒一秒推进。
    /// </summary>
    public class BossWorkQueue
    {
        readonly OrderBook orders;
        readonly List<string> queue = new List<string>();

        string current;
        float timer;

        /// <summary>做一杯要多久。设计方案给的是单杯 15–30 秒，取中间值。</summary>
        public float SecondsPerDrink { get; set; } = 18f;

        /// <summary>做完自动端过去（老板自己的单才送；玩家做的单归玩家送）。</summary>
        public bool AutoDeliver { get; set; } = true;

        public IReadOnlyList<string> Queue => queue;
        public string Current => current;
        public float Progress => current == null ? 0f : timer / SecondsPerDrink;

        public BossWorkQueue(OrderBook orders)
        {
            this.orders = orders;
        }

        /// <summary>把单插到队尾。已经在队里 / 正在做 / 已结束的单会被忽略（幂等）。</summary>
        public bool Enqueue(string orderId)
        {
            var o = orders.Find(orderId);
            if (o == null || !o.IsOpen) return false;
            if (o.Status != OrderStatus.Pending) return false;
            if (current == orderId || queue.Contains(orderId)) return false;
            queue.Add(orderId);
            return true;
        }

        /// <summary>推进队列。dt 是秒；调用方每帧喂 Time.deltaTime。</summary>
        public void Tick(float dt)
        {
            if (current == null) StartNext();

            if (current != null)
            {
                timer += dt;
                if (timer >= SecondsPerDrink)
                {
                    orders.FinishMaking(current);   // 制作中 → 待送
                    current = null;
                    timer = 0f;
                }
            }

            if (AutoDeliver) DeliverReadyOnes();
        }

        void StartNext()
        {
            while (queue.Count > 0)
            {
                var id = queue[0];
                queue.RemoveAt(0);

                var o = orders.Find(id);
                if (o == null || !o.IsOpen || o.Status != OrderStatus.Pending) continue;

                if (!orders.Claim(id, OrderActor.Boss)) continue;   // 已被玩家挑走的单不该在这里
                if (!orders.StartMaking(id)) continue;              // 状态被别处改了，跳过即可
                current = id;
                timer = 0f;
                return;
            }
        }

        void DeliverReadyOnes()
        {
            for (int i = 0; i < orders.All.Count; i++)
            {
                var o = orders.All[i];
                if (o.Status != OrderStatus.Ready) continue;
                if (o.Maker != OrderActor.Boss) continue;   // 玩家做的单自己送（谁做谁送）
                orders.Deliver(o.Id);                        // 客人没落座会失败，下一拍再试
            }
        }

        public string Describe() =>
            $"boss queue={queue.Count} current={current ?? "-"} progress={Progress:F2}";
    }
}

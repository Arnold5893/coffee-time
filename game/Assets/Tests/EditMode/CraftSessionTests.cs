using System.IO;
using System.Linq;
using CoffeeShop.Drinks;
using NUnit.Framework;
using UnityEngine;

namespace CoffeeShop.Tests
{
    /// <summary>
    /// 07 票 / FR-MAKE-03、FR-EVAL-03、NFR-05：制作界面的业务入口。
    /// <see cref="CraftSession"/> 是 UI 唯一能碰的东西——这里验证它把五原语都转成了领域请求，
    /// 并且任何越权/错误操作都只会被拒绝，不会改写状态。
    /// </summary>
    public class CraftSessionTests
    {
        static Recipe Load(string sku)
        {
            string path = Path.Combine(Application.dataPath, RecipeBook.AssetRelativePath);
            Assert.IsTrue(File.Exists(path), "找不到配方配置: " + path);
            var file = RecipeBook.Parse(File.ReadAllText(path));
            var r = RecipeBook.Find(file, sku);
            Assert.IsNotNull(r, "缺少 SKU: " + sku);
            return r;
        }

        static Recipe Mini(params RecipeStep[] steps) =>
            new Recipe { sku = "mini", name = "试做", line = "latte", steps = steps };

        /// <summary>把经典拿铁做完（不含最后出杯的点击），返回会话。</summary>
        static CraftSession DriveToServe(Recipe recipe)
        {
            var s = new CraftSession(recipe);
            s.PutAt(0);                       // cup（place，放到萃取口）
            s.HoldFor(2.6f);                  // grind
            s.Tap(0.5f);                      // tamp
            for (int i = 0; i < 8; i++) s.Tick(1f);   // extract（wait 6s）
            s.Tap(0.1f);                      // move
            s.HoldFor(2.2f);                  // milk
            return s;
        }

        [Test]
        public void 五原语能做出好的一杯()
        {
            var s = DriveToServe(Load("classic-latte"));
            Assert.AreEqual("serve", s.StepId, "后段五原语都走过了，停在出杯");
            Assert.AreEqual(0, s.Deviations, "全程没有判偏");

            s.Tap(0.1f);
            Assert.IsTrue(s.ReadyToServe, "最后一步做完，等出杯");
            var review = s.Serve();
            Assert.IsNotNull(review);
            Assert.AreEqual(DrinkQuality.Good, review.Quality, review.ToString());
            Assert.IsTrue(s.Maker.Served);
        }

        [Test]
        public void 放置放错位置会判偏并停下来()
        {
            var s = new CraftSession(Load("classic-latte"));
            CraftFeedback? got = null;
            s.Feedback += f => got = f;

            Assert.AreEqual(CraftUiMode.Place, s.Mode);
            Assert.AreEqual(2, s.Options.Length, "取杯有两个热点（萃取口 / 台面）");

            int before = s.StepIndex;
            Assert.AreEqual(MakeAction.Ok, s.PutAt(1));           // 放到台面（错）
            Assert.AreEqual(before, s.StepIndex, "判偏不进下一步");
            Assert.AreEqual(CraftUiMode.Decide, s.Mode);
            Assert.AreEqual(1, s.Deviations);
            Assert.AreEqual(CraftFeedback.Deviation, got, "判偏要发即时反馈（画面/音效）");

            Assert.IsTrue(s.Retry());
            Assert.AreEqual(CraftUiMode.Place, s.Mode, "重做回到这一步");
        }

        [Test]
        public void 按住没按够松手算偏差()
        {
            var s = new CraftSession(Load("classic-latte"));
            s.PutAt(0);
            Assert.AreEqual(CraftUiMode.Hold, s.Mode);

            s.BeginPress();
            s.Tick(0.6f);                     // 磨豆要 2.5 秒
            Assert.Less(s.Progress01, 0.5f);
            s.EndPress();

            Assert.AreEqual(1, s.Deviations);
            Assert.AreEqual(CraftUiMode.Decide, s.Mode, "没按够同样要停下来等决定");
        }

        [Test]
        public void 按住期间进度不越过满格()
        {
            var s = new CraftSession(Load("classic-latte"));
            s.PutAt(0);
            s.BeginPress();
            for (int i = 0; i < 40; i++) s.Tick(0.5f);   // 20 秒，远超 2.5 秒
            Assert.AreEqual(1f, s.Progress01, 0.001f, "按住进度封顶在 1，多按不罚");
            s.EndPress();
            Assert.AreEqual(0, s.Deviations, "按过头不算错");
        }

        [Test]
        public void 点击要按到位()
        {
            var s = new CraftSession(Load("classic-latte"));
            s.PutAt(0);
            s.HoldFor(2.6f);

            Assert.AreEqual("tamp", s.StepId);
            Assert.AreEqual(CraftUiMode.Press, s.Mode);
            s.Tap(0.05f);                     // 压粉要 0.4 秒
            Assert.AreEqual(CraftUiMode.Decide, s.Mode, "点一下不够");
            s.Retry();
            s.Tap(0.5f);
            Assert.AreEqual("extract", s.StepId);
        }

        [Test]
        public void 等待型自动推进并按秒计()
        {
            var s = new CraftSession(Load("classic-latte"));
            s.PutAt(0);
            s.HoldFor(2.6f);
            s.Tap(0.5f);
            Assert.AreEqual(CraftUiMode.Wait, s.Mode, "萃取不用操作");

            s.Tick(3f);
            Assert.AreEqual("extract", s.StepId, "没等够不走");
            Assert.AreEqual(0.5f, s.Progress01, 0.02f);
            s.Tick(3.1f);
            Assert.AreEqual("move", s.StepId, "等够 6 秒自动完成");
            Assert.AreEqual(0, s.Deviations, "等待型没有判定");
        }

        [Test]
        public void 门槛只挡操作不判错()
        {
            var s = new CraftSession(Load("classic-latte"));
            s.PutAt(0);
            s.HoldFor(2.6f);
            s.Skip();                          // 跳过压粉 → 萃取的门槛没开

            s.Tick(1f);
            Assert.AreEqual(MakeAction.Blocked, s.LastResult, "门槛没开时操作被挡");
            Assert.GreaterOrEqual(s.BlockedActions, 1);
            Assert.AreEqual(0, s.Deviations, "被挡住不算错（FR-EVAL-04）");
            Assert.AreEqual("extract", s.StepId, "状态没有变化");
        }

        [Test]
        public void 提前出杯判非常差且缺步不计错()
        {
            var s = new CraftSession(Load("classic-latte"));
            s.PutAt(0);
            var review = s.Serve();

            Assert.IsTrue(review.MissingRequired);
            Assert.AreEqual(DrinkQuality.Terrible, review.Quality);
            Assert.AreEqual(0, review.ErrorSteps, "缺步不是误差，不能重复计");
            Assert.AreEqual(0, s.Deviations);
        }

        [Test]
        public void 出杯之后所有操作都被拒()
        {
            var s = DriveToServe(Load("classic-latte"));
            s.Tap(0.1f);
            s.Serve();
            Assert.IsTrue(s.Maker.Served);

            Assert.AreEqual(MakeAction.Served, s.Tap(0.1f));
            Assert.AreEqual(MakeAction.Served, s.Choose(0));
            Assert.AreEqual(MakeAction.Served, s.PutAt(0));
            s.Tick(1f);
            Assert.AreEqual(CraftUiMode.Done, s.Mode, "出杯后停在这个状态");
        }

        [Test]
        public void 界面只能发请求发错原语不改变状态()
        {
            var s = new CraftSession(Load("classic-latte"));
            int before = s.StepIndex;

            Assert.AreEqual(MakeAction.NotCurrent, s.Choose(0), "当前是放置，选不接");
            Assert.AreEqual(MakeAction.NotCurrent, s.HoldFor(3f), "放置步骤不吃按住");
            Assert.AreEqual(MakeAction.NotCurrent, s.Choose(99), "越界的选项不崩也不生效");
            Assert.AreEqual(before, s.StepIndex, "状态一点没动");
            Assert.GreaterOrEqual(s.WrongActions, 2, "三次错原语都被记下来了");
            Assert.AreEqual(0, s.Deviations, "点错按钮不算做错");
        }

        [Test]
        public void 选择原语选错判偏选对通过()
        {
            var r = Mini(
                new RecipeStep { id = "milkType", name = "选奶", primitive = "select", judged = true,
                                 options = new[] { "椰奶", "牛奶" }, correct = "椰奶" },
                new RecipeStep { id = "done", name = "出杯", primitive = "click" });
            var s = new CraftSession(r);

            Assert.AreEqual(CraftUiMode.Choose, s.Mode);
            s.Choose(1);                       // 选牛奶（错）
            Assert.AreEqual(CraftUiMode.Decide, s.Mode);
            Assert.AreEqual(1, s.Deviations);

            s.Retry();
            s.Choose(0);
            Assert.AreEqual("done", s.StepId, "选对直接下一步");
            Assert.AreEqual(0, s.Maker.Review().ErrorSteps, "重做把偏差抹掉了");
        }

        [Test]
        public void 界面层拿不到评价档位()
        {
            // 界面只该知道"第几步、进度、候选、有没有偏差"，档位（好/一般/差/非常差）不给它，
            // 免得哪天顺手贴到屏幕上（FR-EVAL-03 无文字评级）。
            Assert.IsFalse(typeof(CraftSession).GetProperties().Any(p => p.PropertyType == typeof(DrinkQuality)),
                           "CraftSession 不该向界面暴露档位枚举");
            Assert.IsFalse(typeof(CraftSession).GetFields().Any(f => f.IsPublic && f.FieldType == typeof(DrinkQuality)));
        }
    }
}

using System.IO;
using System.Linq;
using CoffeeShop.Drinks;
using NUnit.Framework;
using UnityEngine;

namespace CoffeeShop.Tests
{
    /// <summary>
    /// 06 票：做饮品状态机（FR-MAKE-01/02/03、FR-EVAL-01/02/04）。
    /// 全部纯对象，一拍一拍推进，不依赖场景与时间。
    /// </summary>
    public class DrinkMakerTests
    {
        static RecipeFile file;

        static RecipeFile FileConfig()
        {
            if (file != null) return file;
            string path = Path.Combine(Application.dataPath, RecipeBook.AssetRelativePath);
            file = RecipeBook.Parse(File.ReadAllText(path));
            return file;
        }

        static Recipe R(string sku) => RecipeBook.Find(FileConfig(), sku);

        /// <summary>按当前步骤的原语做对。</summary>
        static MakeAction DoGood(DrinkMaker m)
        {
            var s = m.Current;
            switch (s.Primitive)
            {
                case MakePrimitive.Hold: m.Hold(s.param); return m.Release();
                case MakePrimitive.Click: return m.Click(s.param + 0.1f);
                case MakePrimitive.Select: return m.Choose(s.correct);
                case MakePrimitive.Place: return m.Put(s.correct);
                case MakePrimitive.Wait: return m.Await(s.param);
                default: return MakeAction.NotCurrent;
            }
        }

        /// <summary>取杯：配方里这一步是"放置"原语（把杯子放到萃取口下）。</summary>
        static void Cup(DrinkMaker m) => m.Put("萃取口");

        static void FinishAll(DrinkMaker m)
        {
            int guard = 0;
            while (!m.Finished && guard++ < 64)
            {
                if (m.PendingRetry) { m.Continue(); continue; }
                DoGood(m);
            }
            Assert.IsTrue(m.Finished, "没有走完整条配方");
        }

        [Test]
        public void 按配方做完全对就是好()
        {
            var m = new DrinkMaker(R("osmanthus-latte"));
            FinishAll(m);
            var rv = m.Serve();

            Assert.AreEqual(4, rv.JudgedSteps, "拿铁线 4 个判定步");
            Assert.AreEqual(0, rv.ErrorSteps);
            Assert.IsFalse(rv.MissingRequired);
            Assert.AreEqual(DrinkQuality.Good, rv.Quality);
        }

        [Test]
        public void 磨豆松早了算偏差_重做可以抹掉()
        {
            var m = new DrinkMaker(R("osmanthus-latte"));
            Cup(m);                       // 取杯

            Assert.AreEqual(MakeAction.InProgress, m.Hold(1.0f));
            Assert.AreEqual(MakeAction.Ok, m.Release(), "没按够也是完成这一步，只是带偏差");
            Assert.IsTrue(m.PendingRetry, "判偏了要停在待决定");
            Assert.AreEqual(StepOutcome.Off, m.OutcomeOf("grind"));

            // 待决定时别的操作一律不接受
            Assert.AreEqual(MakeAction.Invalid, m.Click(1f));

            Assert.IsTrue(m.RetryStep(), "原地重做");
            Assert.IsFalse(m.PendingRetry);
            Assert.AreEqual(StepOutcome.Pending, m.OutcomeOf("grind"), "重做要把偏差抹掉");

            m.Hold(2.5f);
            m.Release();
            Assert.AreEqual(StepOutcome.Good, m.OutcomeOf("grind"));
            Assert.AreEqual(0, m.Review().ErrorSteps);
        }

        [Test]
        public void 带着偏差继续会记一次错()
        {
            var m = new DrinkMaker(R("osmanthus-latte"));
            Cup(m);
            m.Hold(1.0f);
            m.Release();
            Assert.IsTrue(m.Continue(), "带着偏差继续");

            FinishAll(m);
            var rv = m.Serve();
            Assert.AreEqual(1, rv.ErrorSteps);
            Assert.AreEqual(0.25f, rv.ErrorRate, 1e-4f);
            Assert.AreEqual(DrinkQuality.Fair, rv.Quality);
        }

        [Test]
        public void 选错风味算偏差()
        {
            var m = new DrinkMaker(R("osmanthus-latte"));
            Cup(m); DoGood(m);            // 取杯、磨豆
            DoGood(m);                       // 压粉
            DoGood(m);                       // 萃取
            DoGood(m);                       // 拿杯
            DoGood(m);                       // 加奶
            Assert.AreEqual("flavor", m.Current.id);

            var wrong = m.Current.options.First(o => o != m.Current.correct);
            Assert.AreEqual(MakeAction.Ok, m.Choose(wrong));
            Assert.AreEqual(StepOutcome.Off, m.OutcomeOf("flavor"));
            Assert.IsTrue(m.PendingRetry);
        }

        [Test]
        public void 跳过必需的步骤就是缺步且直接非常差()
        {
            var m = new DrinkMaker(R("osmanthus-latte"));
            Cup(m); DoGood(m); DoGood(m); DoGood(m); DoGood(m);   // 取杯 → 拿杯
            Assert.AreEqual("milk", m.Current.id);
            Assert.IsTrue(m.SkipCurrent(), "可以跳过");

            FinishAll(m);
            var rv = m.Serve();
            Assert.IsTrue(rv.MissingRequired);
            CollectionAssert.Contains(rv.MissingStepIds, "milk");
            Assert.AreEqual(DrinkQuality.Terrible, rv.Quality, "缺步不看错误率，直接非常差");
            Assert.AreEqual(0, rv.ErrorSteps, "跳过不算错误步");
        }

        [Test]
        public void 门槛只禁操作不判错()
        {
            var m = new DrinkMaker(R("osmanthus-latte"));
            Cup(m); DoGood(m); DoGood(m);      // 取杯、磨豆、压粉 → 当前是萃取
            Assert.AreEqual("extract", m.Current.id);

            Assert.IsTrue(m.SkipCurrent(), "把萃取跳掉");
            Assert.AreEqual("move", m.Current.id);

            Assert.AreEqual(MakeAction.Blocked, m.Click(1f), "萃取没完成，取不了杯");
            Assert.AreEqual(StepOutcome.Pending, m.OutcomeOf("move"), "被门槛拦住不能留下任何记录");
            Assert.AreEqual(0, m.Review().ErrorSteps, "门槛不算错（FR-EVAL-04）");

            // 补上萃取之后门槛就开了
            var m2 = new DrinkMaker(R("osmanthus-latte"));
            Cup(m2); DoGood(m2); DoGood(m2);
            Assert.AreEqual(MakeAction.InProgress, m2.Await(1f));
            Assert.AreEqual(MakeAction.Ok, m2.Await(10f), "液流停下");
            Assert.AreEqual(MakeAction.Ok, m2.Click(1f), "这时候才拿得起杯子");
        }

        [Test]
        public void 可以提前出杯_没做的步骤算缺步()
        {
            var m = new DrinkMaker(R("osmanthus-latte"));
            Cup(m);
            DoGood(m);                       // 只做完磨豆
            var rv = m.Serve();

            Assert.IsTrue(rv.MissingRequired);
            Assert.Greater(rv.MissingStepIds.Length, 3);
            Assert.AreEqual(DrinkQuality.Terrible, rv.Quality);
            Assert.AreEqual(MakeAction.Served, m.Click(1f), "出杯后不再接受操作");
        }

        [Test]
        public void 原语不符会被拒绝()
        {
            var m = new DrinkMaker(R("osmanthus-latte"));
            Cup(m);                       // 取杯
            Assert.AreEqual("grind", m.Current.id);
            Assert.AreEqual(MakeAction.NotCurrent, m.Click(1f), "磨豆是按住，不给点");
            Assert.AreEqual(MakeAction.NotCurrent, m.Choose("桂花"));
        }

        [Test]
        public void 自动驱动一步不错且耗时等于配方()
        {
            var r = R("osmanthus-latte");
            var m = new DrinkMaker(r);

            float t = 0f;
            while (!m.Finished && t < 120f) { m.AutoTick(0.1f); t += 0.1f; }
            Assert.IsTrue(m.Finished);

            var rv = m.Serve();
            Assert.AreEqual(0, rv.ErrorSteps, "老板做的不产生偏差");
            Assert.AreEqual(DrinkQuality.Good, rv.Quality);
            Assert.That(t, Is.EqualTo(r.AutoSeconds).Within(1.0f), "自动耗时应当等于配方里各步 cost 之和");
        }

        [Test]
        public void 手动与自动读的是同一份配方()
        {
            var r = R("coconut-latte");
            var manual = new DrinkMaker(r);
            var auto = new DrinkMaker(r);

            FinishAll(manual);
            auto.AutoRun();

            Assert.AreSame(r, manual.Recipe);
            Assert.AreSame(r, auto.Recipe);
            Assert.AreEqual(manual.Recipe.steps.Length, auto.Recipe.steps.Length);
            Assert.AreEqual(DrinkQuality.Good, manual.Serve().Quality);
            Assert.AreEqual(DrinkQuality.Good, auto.Serve().Quality);
        }

        [TestCase(0, DrinkQuality.Good)]
        [TestCase(1, DrinkQuality.Fair)]
        [TestCase(2, DrinkQuality.Fair)]
        [TestCase(3, DrinkQuality.Poor)]
        [TestCase(4, DrinkQuality.Terrible)]
        public void 四档阈值_四判定步(int errors, DrinkQuality expect)
        {
            var rv = MakeOsmanthusWithErrors(errors);
            Assert.AreEqual(errors, rv.ErrorSteps);
            Assert.AreEqual(errors / 4f, rv.ErrorRate, 1e-4f);
            Assert.AreEqual(expect, rv.Quality, $"{errors}/4 应当判 {expect}");
        }

        [TestCase(0, DrinkQuality.Good)]
        [TestCase(1, DrinkQuality.Fair)]
        [TestCase(2, DrinkQuality.Poor)]
        [TestCase(3, DrinkQuality.Terrible)]
        public void 四档阈值_三判定步(int errors, DrinkQuality expect)
        {
            // 经典拿铁没有风味步，判定 3 步（磨豆、压粉、加奶）
            var m = new DrinkMaker(R("classic-latte"));
            Cup(m);
            if (errors >= 1) { m.Hold(0.4f); m.Release(); m.Continue(); } else DoGood(m);
            if (errors >= 2) { m.Click(0.05f); m.Continue(); } else DoGood(m);
            DoGood(m);   // 萃取
            DoGood(m);   // 拿杯
            if (errors >= 3) { m.Hold(0.4f); m.Release(); m.Continue(); } else DoGood(m);
            FinishAll(m);

            var rv = m.Serve();
            Assert.AreEqual(3, rv.JudgedSteps);
            Assert.AreEqual(errors, rv.ErrorSteps);
            Assert.AreEqual(expect, rv.Quality, $"{errors}/3 应当判 {expect}");
        }

        [Test]
        public void 放置类步骤放错位置算偏差()
        {
            // 拿铁线用不到"放置"，但五种原语都得在领域层走得通（FR-MAKE-03）
            var r = new Recipe
            {
                sku = "tray-test", name = "上托盘", line = "latte",
                steps = new[]
                {
                    new RecipeStep { id = "cup", name = "取杯", primitive = "click" },
                    new RecipeStep { id = "tray", name = "放上托盘", primitive = "place", judged = true, correct = "托盘A" },
                    new RecipeStep { id = "serve", name = "出杯", primitive = "click" },
                },
            };
            var m = new DrinkMaker(r);
            m.Click();
            Assert.AreEqual("tray", m.Current.id);
            Assert.AreEqual(MakeAction.NotCurrent, m.Click(1f), "放置步不接受点击");
            Assert.AreEqual(MakeAction.Ok, m.Put("托盘B"));
            Assert.AreEqual(StepOutcome.Off, m.OutcomeOf("tray"));
            m.Continue();
            m.Click(1f);
            Assert.AreEqual(DrinkQuality.Terrible, m.Serve().Quality);
        }

        /// <summary>桂花拿铁里制造 N 个偏差（磨豆 → 压粉 → 加奶 → 风味，按顺序）。</summary>
        static DrinkReview MakeOsmanthusWithErrors(int errors)
        {
            var m = new DrinkMaker(R("osmanthus-latte"));
            Cup(m);                                                   // 取杯
            if (errors >= 1) { m.Hold(0.4f); m.Release(); m.Continue(); } else DoGood(m);   // 磨豆
            if (errors >= 2) { m.Click(0.05f); m.Continue(); } else DoGood(m);              // 压粉
            DoGood(m);                                                   // 萃取
            DoGood(m);                                                   // 拿杯
            if (errors >= 3) { m.Hold(0.4f); m.Release(); m.Continue(); } else DoGood(m);   // 加奶
            if (errors >= 4)
            {
                var wrong = m.Current.options.First(o => o != m.Current.correct);
                m.Choose(wrong);
                m.Continue();                                            // 风味
            }
            else DoGood(m);
            FinishAll(m);
            return m.Serve();
        }
    }
}

using System.IO;
using System.Linq;
using CoffeeShop.Drinks;
using NUnit.Framework;
using UnityEngine;

namespace CoffeeShop.Tests
{
    /// <summary>
    /// 06 票 / FR-MAKE-02/04/05：配方配置化与校验。纯对象，不加载场景。
    /// </summary>
    public class RecipeConfigTests
    {
        static RecipeFile Load()
        {
            string path = Path.Combine(Application.dataPath, RecipeBook.AssetRelativePath);
            Assert.IsTrue(File.Exists(path), "找不到配方配置: " + path);
            return RecipeBook.Parse(File.ReadAllText(path));
        }

        [Test]
        public void 配置文件合法()
        {
            var errors = RecipeBook.Validate(Load());
            Assert.IsEmpty(errors, "配置有问题: " + string.Join(" | ", errors));
        }

        [Test]
        public void 拿铁线四款SKU都在()
        {
            var file = Load();
            foreach (var sku in new[] { "classic-latte", "coconut-latte", "osmanthus-latte", "caramel-macchiato" })
            {
                var r = RecipeBook.Find(file, sku);
                Assert.IsNotNull(r, "缺少 SKU: " + sku);
                Assert.AreEqual("latte", r.line);
                Assert.That(r.steps.Length, Is.InRange(6, 9), $"{sku} 步骤数应在 6–9（FR-MAKE-05）");
            }
        }

        [Test]
        public void 判定步数按基底线约定()
        {
            var file = Load();
            // 7.9.5：拿铁线判定 4 步（磨豆、压粉、加奶、加风味）；经典拿铁没有风味步 → 3 步
            Assert.AreEqual(3, RecipeBook.Find(file, "classic-latte").JudgedCount);
            Assert.AreEqual(4, RecipeBook.Find(file, "coconut-latte").JudgedCount);
            Assert.AreEqual(4, RecipeBook.Find(file, "osmanthus-latte").JudgedCount);
            Assert.AreEqual(4, RecipeBook.Find(file, "caramel-macchiato").JudgedCount);
        }

        [Test]
        public void 等待型步骤一律不判()
        {
            foreach (var r in Load().recipes)
            foreach (var s in r.steps.Where(s => s.Primitive == MakePrimitive.Wait))
                Assert.IsFalse(s.judged, $"{r.sku}.{s.id} 是等待步，不能有判定（FR-EVAL-01）");
        }

        [Test]
        public void 门槛指向前面的步骤()
        {
            foreach (var r in Load().recipes)
            {
                for (int i = 0; i < r.steps.Length; i++)
                {
                    var s = r.steps[i];
                    if (string.IsNullOrWhiteSpace(s.gate)) continue;
                    int at = System.Array.FindIndex(r.steps, x => x.id == s.gate);
                    Assert.GreaterOrEqual(at, 0, $"{r.sku}.{s.id} 的 gate 指向不存在的步骤");
                    Assert.Less(at, i, $"{r.sku}.{s.id} 的 gate 必须在它之前");
                }
            }
        }

        [Test]
        public void 选择步的正确项必须在候选里()
        {
            foreach (var r in Load().recipes)
            foreach (var s in r.steps.Where(s => s.Primitive == MakePrimitive.Select))
                Assert.Contains(s.correct, s.options, $"{r.sku}.{s.id} 的 correct 不在 options 里");
        }

        [Test]
        public void 坏配置会被校验拦住()
        {
            var file = new RecipeFile
            {
                recipes = new[]
                {
                    new Recipe
                    {
                        sku = "broken", name = "坏配方", line = "latte",
                        steps = new[]
                        {
                            new RecipeStep { id = "a", name = "选", primitive = "select", options = new[] { "x" }, correct = "y" },
                            new RecipeStep { id = "b", name = "等", primitive = "wait", judged = true, param = 1f },
                            new RecipeStep { id = "c", name = "门", primitive = "click", gate = "不存在" },
                        },
                    },
                },
            };
            var errors = RecipeBook.Validate(file);
            Assert.IsNotEmpty(errors);
            Assert.IsTrue(errors.Any(e => e.Contains("步骤数")), "步骤数不够要报");
            Assert.IsTrue(errors.Any(e => e.Contains("correct 不在")), "选择项错要报");
            Assert.IsTrue(errors.Any(e => e.Contains("等待型步骤不能有判定")), "等待步判错要报");
            Assert.IsTrue(errors.Any(e => e.Contains("gate 指向了不存在")), "门槛指向错要报");
        }

        [Test]
        public void 自动耗时按步骤计的出来()
        {
            var r = RecipeBook.Find(Load(), "osmanthus-latte");
            Assert.Greater(r.AutoSeconds, 10f, "整杯自动耗时应当在十几秒量级（FR-MAKE-05 15–30 秒）");
            Assert.Less(r.AutoSeconds, 40f);
        }
    }
}

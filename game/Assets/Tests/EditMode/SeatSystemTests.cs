using System.IO;
using System.Linq;
using CoffeeShop.Seating;
using NUnit.Framework;
using UnityEngine;

namespace CoffeeShop.Tests
{
    /// <summary>
    /// 04 票 / FR-SEAT-02：座位占用状态机。
    /// 这些用例全部是纯对象，不加载场景，跑得很快。
    /// </summary>
    public class SeatStateTests
    {
        static SeatState NewSeat() => new SeatState("bar-1", "bar", SeatKind.BarStool);

        [Test]
        public void 空座位可以被占用()
        {
            var s = NewSeat();
            Assert.IsTrue(s.IsFree);
            Assert.IsTrue(s.TryOccupy("npc-1"));
            Assert.IsFalse(s.IsFree);
            Assert.AreEqual("npc-1", s.Occupant);
        }

        [Test]
        public void 已占用座位拒绝第二个占用者()
        {
            var s = NewSeat();
            Assert.IsTrue(s.TryOccupy("npc-1"));
            Assert.IsFalse(s.TryOccupy("npc-2"), "已占用的座位不该被抢");
            Assert.AreEqual("npc-1", s.Occupant, "占用者不能被覆盖");
        }

        [Test]
        public void 空占用者标识被拒绝()
        {
            var s = NewSeat();
            Assert.IsFalse(s.TryOccupy(null));
            Assert.IsFalse(s.TryOccupy(""));
            Assert.IsFalse(s.TryOccupy("   "));
            Assert.IsTrue(s.IsFree, "非法占用不能把座位标成已占");
        }

        [Test]
        public void 本人可以释放座位()
        {
            var s = NewSeat();
            s.TryOccupy("npc-1");
            Assert.IsTrue(s.Release("npc-1"));
            Assert.IsTrue(s.IsFree);
            Assert.IsNull(s.Occupant);
        }

        [Test]
        public void 他人不能释放别人的座位()
        {
            var s = NewSeat();
            s.TryOccupy("npc-1");
            Assert.IsFalse(s.Release("npc-2"), "A 离开不该顺手放掉 B 的座");
            Assert.AreEqual("npc-1", s.Occupant);
        }

        [Test]
        public void 释放空座位返回失败()
        {
            var s = NewSeat();
            Assert.IsFalse(s.Release("npc-1"), "本来就是空的，不该假装释放成功");
        }

        [Test]
        public void 座位id不能为空()
        {
            Assert.Throws<System.ArgumentException>(() => new SeatState("", "bar", SeatKind.BarStool));
            Assert.Throws<System.ArgumentException>(() => new SeatState(null, "bar", SeatKind.BarStool));
        }
    }

    /// <summary>座位集合：计数、查重、批量释放。</summary>
    public class SeatRegistryTests
    {
        static SeatRegistry Build()
        {
            var r = new SeatRegistry();
            r.Add(new SeatState("bar-1", "bar", SeatKind.BarStool));
            r.Add(new SeatState("bar-2", "bar", SeatKind.BarStool));
            r.Add(new SeatState("round-1", "round", SeatKind.Armchair));
            return r;
        }

        [Test]
        public void 计数随占用变化()
        {
            var r = Build();
            Assert.AreEqual(3, r.Count);
            Assert.AreEqual(3, r.FreeCount);
            Assert.IsTrue(r.TryOccupy("bar-1", "npc-1"));
            Assert.AreEqual(2, r.FreeCount);
            Assert.AreEqual(1, r.OccupiedCount);
        }

        [Test]
        public void 重复占用同一座位被拒绝()
        {
            var r = Build();
            Assert.IsTrue(r.TryOccupy("bar-1", "npc-1"));
            Assert.IsFalse(r.TryOccupy("bar-1", "npc-2"));
            Assert.AreEqual(1, r.OccupiedCount);
        }

        [Test]
        public void 未知座位id返回失败()
        {
            var r = Build();
            Assert.IsFalse(r.TryOccupy("nope", "npc-1"));
            Assert.IsFalse(r.Release("nope", "npc-1"));
            Assert.IsNull(r.Find("nope"));
        }

        [Test]
        public void 重复id登记会抛异常()
        {
            var r = Build();
            Assert.Throws<System.InvalidOperationException>(
                () => r.Add(new SeatState("bar-1", "bar", SeatKind.BarStool)),
                "配置写重了应当当场炸掉，而不是悄悄覆盖");
        }

        [Test]
        public void 全量释放后所有座位可用()
        {
            var r = Build();
            r.TryOccupy("bar-1", "npc-1");
            r.TryOccupy("round-1", "npc-2");
            r.ReleaseAll();
            Assert.AreEqual(3, r.FreeCount);
        }
    }

    /// <summary>配置本身是数据，必须能脱离场景单独校验。</summary>
    public class SeatConfigTests
    {
        static SeatConfigFile Load()
        {
            string path = Path.Combine(Application.dataPath, SeatConfig.AssetRelativePath);
            Assert.IsTrue(File.Exists(path), "找不到座位配置文件: " + path);
            return SeatConfig.Parse(File.ReadAllText(path));
        }

        [Test]
        public void 配置文件合法()
        {
            var file = Load();
            var errors = SeatConfig.Validate(file);
            Assert.IsEmpty(errors, "配置有问题: " + string.Join(" | ", errors));
        }

        [Test]
        public void 座位id唯一()
        {
            var file = Load();
            var ids = file.seats.Select(s => s.id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "id 有重复");
        }

        [Test]
        public void 首切覆盖吧台凳与客人桌位()
        {
            var file = Load();
            Assert.GreaterOrEqual(file.seats.Count(s => s.zone == "bar"), 3, "吧台凳至少 3 个");
            Assert.GreaterOrEqual(file.seats.Count(s => s.zone == "round"), 3, "圆桌座位要有");
            Assert.GreaterOrEqual(file.seats.Count(s => s.zone == "meeting"), 1, "会议桌座位要有");
            Assert.GreaterOrEqual(file.seats.Length, 20, "首切座位总数");
        }

        [Test]
        public void 双人沙发是一椅两座()
        {
            var file = Load();
            var sofas = file.seats.Where(s => s.zone == "sofa").ToList();
            Assert.GreaterOrEqual(sofas.Count, 20, "10 张双人沙发各出 2 个座位");

            // 同一个 anchor 必须正好出两个座位，且左右对称（否则两个人会坐同一点）
            foreach (var g in sofas.GroupBy(s => s.anchor))
            {
                Assert.AreEqual(2, g.Count(), g.Key + " 应该有两个座位");
                var offs = g.Select(s => s.lateralOffset).OrderBy(v => v).ToList();
                Assert.Less(offs[0], 0f, g.Key + " 左边座位要往左偏");
                Assert.Greater(offs[1], 0f, g.Key + " 右边座位要往右偏");
                Assert.AreEqual(offs[0], -offs[1], 1e-4f, g.Key + " 两个座位要左右对称");
                Assert.Greater(Mathf.Abs(offs[1]), 0.2f, g.Key + " 两个座位要真正分开");
            }
        }

        [Test]
        public void 横向偏移有合理上限()
        {
            var ok = new SeatConfigFile
            {
                seats = new[] { new SeatEntry { id = "a", zone = "sofa", anchor = "x", faceDir = "N", lateralOffset = 0.30f } },
            };
            Assert.IsEmpty(SeatConfig.Validate(ok));

            var bad = new SeatConfigFile
            {
                seats = new[] { new SeatEntry { id = "a", zone = "sofa", anchor = "x", faceDir = "N", lateralOffset = 0.9f } },
            };
            Assert.IsNotEmpty(SeatConfig.Validate(bad), "偏到沙发外面去了应当报错");
        }

        [Test]
        public void 每个座位都指定了锚点与朝向依据()
        {
            var file = Load();
            foreach (var s in file.seats)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(s.anchor), s.id + " 缺少 anchor");
                bool hasFacing = !string.IsNullOrWhiteSpace(s.faceTarget) || !string.IsNullOrWhiteSpace(s.faceDir);
                Assert.IsTrue(hasFacing, s.id + " 既没有 faceTarget 也没有 faceDir");
            }
        }

        [Test]
        public void 座椅类型解析()
        {
            Assert.AreEqual(SeatKind.BarStool, SeatConfig.ParseKind("barStool"));
            Assert.AreEqual(SeatKind.BarStool, SeatConfig.ParseKind("BAR_STOOL"));
            Assert.AreEqual(SeatKind.Armchair, SeatConfig.ParseKind("armchair"));
            Assert.AreEqual(SeatKind.Sofa, SeatConfig.ParseKind("sofa"));
            Assert.AreEqual(SeatKind.Armchair, SeatConfig.ParseKind(null), "未知类型退回普通椅子");
        }

        [Test]
        public void 座面高度按类型兜底()
        {
            var e = new SeatEntry { kind = "barStool", seatHeight = 0f };
            Assert.AreEqual(0.66f, SeatConfig.ResolveSeatHeight(e), 1e-4f);
            e.seatHeight = 0.72f;
            Assert.AreEqual(0.72f, SeatConfig.ResolveSeatHeight(e), 1e-4f, "显式配置优先");
        }

        [Test]
        public void 方向盘方向名解析()
        {
            Assert.AreEqual(0f, SeatConfig.ParseFaceDir("N").x, 1e-4f);
            Assert.AreEqual(1f, SeatConfig.ParseFaceDir("E").x, 1e-4f);
            Assert.AreEqual(-1f, SeatConfig.ParseFaceDir("W").x, 1e-4f, "西 = -X");
            Assert.AreEqual(-1f, SeatConfig.ParseFaceDir("S").z, 1e-4f, "南 = -Z");
            var ne = SeatConfig.ParseFaceDir("ne");
            Assert.AreEqual(1f, new Vector2(ne.x, ne.z).magnitude, 1e-3f, "东北方向应当是单位向量");
            Assert.AreEqual(Vector3.zero, SeatConfig.ParseFaceDir("乱七八糟"));
        }

        [Test]
        public void 空配置被识别为错误()
        {
            Assert.IsNotEmpty(SeatConfig.Validate(new SeatConfigFile { seats = null }));
            Assert.Throws<System.ArgumentException>(() => SeatConfig.Parse(""));
        }
    }
}

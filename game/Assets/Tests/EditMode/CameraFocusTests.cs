using CoffeeShop.Cameras;
using NUnit.Framework;

namespace CoffeeShop.Tests
{
    /// <summary>
    /// 07 票 / FR-MOV-05、NFR-03：原地近景的过渡。
    /// 纯计算（不加载场景）：切入必须 ≤3 秒、退出要回到跟随构图、参数确实收紧。
    /// </summary>
    public class CameraFocusTests
    {
        /// <summary>按固定帧长推进，返回用掉的秒数。</summary>
        static float DriveTo(CameraFocusState s, float target, float dt, float timeout)
        {
            float t = 0f;
            while (!s.Settled && t < timeout)
            {
                s.Tick(dt);
                t += dt;
            }
            Assert.IsTrue(s.Settled, $"{timeout:F1}s 内没过渡到位");
            Assert.AreEqual(target, s.Progress, 1e-4f);
            return t;
        }

        [Test]
        public void 切入不超过三秒()
        {
            var s = new CameraFocusState();
            Assert.AreEqual(0f, s.Progress);
            s.Begin();
            float spent = DriveTo(s, 1f, 1f / 60f, 5f);
            Assert.LessOrEqual(spent, CameraFocusState.MaxSeconds, $"切入用了 {spent:F2}s");
            Assert.AreEqual(CameraFocusState.DefaultInSeconds, spent, 0.02f);
        }

        [Test]
        public void 低帧率也不超过三秒()
        {
            var s = new CameraFocusState();
            s.Begin();
            float spent = DriveTo(s, 1f, 0.25f, 5f);   // 4fps
            Assert.LessOrEqual(spent, CameraFocusState.MaxSeconds + 0.25f);
        }

        [Test]
        public void 配置写得再长也被截到三秒()
        {
            var s = new CameraFocusState(10f, 10f);
            Assert.AreEqual(CameraFocusState.MaxSeconds, s.InSeconds);
            Assert.AreEqual(CameraFocusState.MaxSeconds, s.OutSeconds);
        }

        [Test]
        public void 退出回到跟随构图()
        {
            var s = new CameraFocusState();
            s.Snap(true);
            Assert.AreEqual(1f, s.Progress);

            s.End();
            float spent = DriveTo(s, 0f, 1f / 60f, 3f);
            Assert.LessOrEqual(spent, CameraFocusState.MaxSeconds);
            Assert.AreEqual(CameraFocusState.DefaultOutSeconds, spent, 0.02f);
        }

        [Test]
        public void 过渡是单调的且两端速度为零()
        {
            var s = new CameraFocusState();
            s.Begin();
            float last = 0f;
            for (int i = 0; i < 200; i++)
            {
                s.Tick(1f / 60f);
                Assert.GreaterOrEqual(s.Progress, last - 1e-6f, "进度不能倒退");
                last = s.Progress;
            }
            Assert.AreEqual(1f, s.Progress, 1e-4f);

            Assert.AreEqual(0f, CameraFocusState.Smooth(0f), 1e-5f);
            Assert.AreEqual(1f, CameraFocusState.Smooth(1f), 1e-5f);
            Assert.AreEqual(0.5f, CameraFocusState.Smooth(0.5f), 1e-5f);
            // 缓动比线性更"软"：前 10% 时间走得比线性少
            Assert.Less(CameraFocusState.Smooth(0.1f), 0.1f);
            Assert.Greater(CameraFocusState.Smooth(0.9f), 0.9f);
        }

        [Test]
        public void 近景确实收紧了视野()
        {
            var p = CameraFocusProfile.Default;
            const float followDistance = 3.6f, followPitch = 35f, followFov = 60f;

            Assert.Less(p.distance, followDistance * 0.6f, "近景距离要明显近于跟随距离");
            Assert.Less(p.fov, followFov - 10f, "近景视野要明显收窄");
            Assert.Less(p.pitch, followPitch, "近景俯角更低，视线贴向台面");

            var s = new CameraFocusState();
            s.Begin();
            while (!s.Settled) s.Tick(1f / 60f);

            Assert.AreEqual(p.distance, s.Distance(followDistance, p.distance, s.Ease), 1e-3f);
            Assert.AreEqual(p.fov, s.Fov(followFov, p.fov, s.Ease), 1e-3f);
            // 半途插值落在两个端点之间
            float mid = CameraFocusState.Smooth(0.5f);
            Assert.That(s.Fov(followFov, p.fov, mid), Is.InRange(p.fov, followFov));
        }
    }
}

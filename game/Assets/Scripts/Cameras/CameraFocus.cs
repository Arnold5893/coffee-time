using System;

namespace CoffeeShop.Cameras
{
    /// <summary>近景构图参数。收紧视野 = 拉近距离 + 收窄 FOV + 压低俯角（贴到吧台上）。</summary>
    [Serializable]
    public struct CameraFocusProfile
    {
        public float distance;
        public float pitch;
        public float fov;

        public static CameraFocusProfile Default => new CameraFocusProfile
        {
            distance = 1.65f,
            pitch = 28f,
            fov = 42f,
        };
    }

    /// <summary>
    /// 跟随机位 ⇄ 吧台近景的过渡状态（07 票 / FR-MOV-05）。
    ///
    /// 纯计算，不碰场景：只回答"现在过渡到几成了，各参数该是多少"。
    /// 做法上是**同一台相机**换一套构图参数并平滑插值——不是切到另一个固定机位，
    /// 所以镜头仍然跟着角色、仍然能被玩家转动（非固定机位）。
    ///
    /// 时长：切入 <see cref="DefaultInSeconds"/>、退出 <see cref="DefaultOutSeconds"/>，
    /// 都远低于 NFR-03 / FR-MOV-05 要求的 3 秒上限；<see cref="MaxSeconds"/> 就是这个上限，
    /// 构造函数会把超时的时长截住，改配置也改不坏。
    /// </summary>
    public sealed class CameraFocusState
    {
        public const float DefaultInSeconds = 0.90f;
        public const float DefaultOutSeconds = 0.60f;
        public const float MaxSeconds = 3.0f;

        public float InSeconds { get; }
        public float OutSeconds { get; }

        public CameraFocusState(float inSeconds = DefaultInSeconds, float outSeconds = DefaultOutSeconds)
        {
            InSeconds = Clamp(inSeconds);
            OutSeconds = Clamp(outSeconds);
        }

        /// <summary>目标：true = 切近景，false = 回跟随机位。</summary>
        public bool Aimed { get; private set; }

        /// <summary>0 = 跟随机位，1 = 近景。</summary>
        public float Progress { get; private set; }

        /// <summary>缓动后的 0–1，插值系数用它（两端速度为 0，不会有硬切感）。</summary>
        public float Ease => Smooth(Progress);

        public bool Settled => Math.Abs(Progress - (Aimed ? 1f : 0f)) < 1e-4f;

        public void Begin() => Aimed = true;
        public void End() => Aimed = false;

        public void Snap(bool focused)
        {
            Aimed = focused;
            Progress = focused ? 1f : 0f;
        }

        /// <summary>推进过渡，返回当前进度。</summary>
        public float Tick(float dt)
        {
            float target = Aimed ? 1f : 0f;
            if (Math.Abs(Progress - target) < 1e-5f) return Progress;

            float dur = Aimed ? InSeconds : OutSeconds;
            Progress = MoveTowards(Progress, target, Math.Max(0f, dt) / dur);
            return Progress;
        }

        /// <summary>把跟随构图参数插值到近景构图。t 用 <see cref="Ease"/>。</summary>
        public float Distance(float baseValue, float focusValue, float t) => Lerp(baseValue, focusValue, t);
        public float Pitch(float baseValue, float focusValue, float t) => Lerp(baseValue, focusValue, t);
        public float Fov(float baseValue, float focusValue, float t) => Lerp(baseValue, focusValue, t);

        public static float Smooth(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        static float Clamp(float v) => v <= 0f ? DefaultInSeconds : (v > MaxSeconds ? MaxSeconds : v);
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        static float MoveTowards(float a, float b, float d) =>
            Math.Abs(b - a) <= d ? b : a + Math.Sign(b - a) * d;
    }
}

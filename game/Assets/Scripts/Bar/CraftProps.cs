using UnityEngine;

namespace CoffeeShop.Bar
{
    /// <summary>
    /// 台面道具的**形状**（07 票返工三）。
    ///
    /// 一件道具 = 一条旋转体轮廓（少数再挂一根圆管把手）。尺寸按真实器具放大 1.6 倍 ——
    /// 近景相机离工作区只有 0.7m，真实尺寸的碗壁（2mm）在屏幕上不到一个像素，
    /// 但比例关系保持真实（杯 > 奶缸 > 压粉器 ≈ 滤碗），一眼能看出这是在做咖啡。
    ///
    /// 所有道具的 **pivot 都在底面中心**，摆放时直接用台面高度，不用再算半高偏移。
    /// </summary>
    public static class CraftProps
    {
        // 关键尺寸（米）。改这里就够了，位置由 CraftView 负责。
        public const float BasketR = 0.0465f;   // 滤碗外半径（真实 58mm 粉碗 × 1.6）
        public const float BasketH = 0.046f;
        public const float PowderR = 0.033f;    // 粉饼半径（留出一圈碗壁，粉多粉少才有得看）
        public const float PowderFloor = 0.0095f;
        public const float CupR = 0.0725f;
        public const float CupH = 0.085f;
        public const float PitcherR = 0.056f;
        public const float PitcherH = 0.105f;

        // ---------------------------------------------------------------- 滤碗

        public static GameObject Basket(Material mat, Transform parent)
        {
            // 底 → 锥形外壁 → 卷边 → 折回来做直筒内腔 → 内底。
            // 内腔和外壁用同一个钢材质：**粉面涨到哪儿，全靠"碗壁还剩多高一圈"读出来**，
            // 内壁一旦做成深色，深棕粉饼跟它就糊成一片，粉多粉少完全看不出。
            return PropMesh.Spawn("Basket", PropMesh.Revolve(new[]
            {
                new Vector2(0.0010f, 0.0000f),
                new Vector2(0.0300f, 0.0000f),
                new Vector2(0.0300f, 0.0000f),
                new Vector2(0.0340f, 0.0050f),
                new Vector2(0.0420f, 0.0220f),   // 锥形外壁：底窄口宽，才像碗不像直筒杯
                new Vector2(0.0460f, 0.0400f),
                new Vector2(0.0465f, 0.0445f),   // 卷边（一条 3mm 的圆润带，不是厚平的杯口）
                new Vector2(0.0455f, 0.0460f),
                new Vector2(0.0430f, 0.0460f),
                new Vector2(0.0415f, 0.0445f),   // 卷边内沿
                new Vector2(0.0415f, 0.0445f),
                new Vector2(0.0405f, 0.0400f),   // 内壁（缓锥）
                new Vector2(0.0385f, 0.0220f),
                new Vector2(0.0360f, 0.0100f),
                new Vector2(0.0290f, 0.0090f),   // 内底倒角
                new Vector2(0.0010f, 0.0090f),
            }), mat, parent);
        }

        /// <summary>粉饼：一个带倒角的圆柱，顶面加一点颗粒起伏 —— 平得像塑料片就不像咖啡粉了。</summary>
        public static GameObject Powder(Material mat, Transform parent)
        {
            var mesh = PropMesh.Revolve(new[]
            {
                new Vector2(0.0010f, 0.00f),
                new Vector2(1.0000f, 0.00f),
                new Vector2(1.0000f, 0.00f),
                new Vector2(1.0000f, 0.84f),   // 侧壁
                new Vector2(0.9800f, 0.90f),   // 边缘圆角
                new Vector2(0.8600f, 0.93f),
                new Vector2(0.5500f, 0.98f),   // 顶面缓丘：中心比边缘高一截
                new Vector2(0.0010f, 1.00f),
            });
            Grain(mesh, 0.07f, 3.2f);
            var go = PropMesh.Spawn("Powder", mesh, mat, parent);
            go.transform.localScale = new Vector3(PowderR, 0.02f, PowderR);
            return go;
        }

        // ---------------------------------------------------------------- 压粉器

        public static GameObject Tamper(Material steel, Material dark, Transform parent)
        {
            var go = PropMesh.Spawn("Tamper", PropMesh.Revolve(new[]
            {
                new Vector2(0.0010f, 0.0000f),
                new Vector2(0.0370f, 0.0000f),
                new Vector2(0.0370f, 0.0000f),
                new Vector2(0.0410f, 0.0040f),   // 底盘倒角（直径正好塞进粉碗内腔）
                new Vector2(0.0410f, 0.0120f),
                new Vector2(0.0410f, 0.0120f),
                new Vector2(0.0370f, 0.0170f),   // 底盘顶面收进去
                new Vector2(0.0100f, 0.017f),
                new Vector2(0.0100f, 0.017f),
                new Vector2(0.0100f, 0.036f),   // 杆
                new Vector2(0.0080f, 0.048f),   // 收腰
                new Vector2(0.0090f, 0.060f),
                new Vector2(0.0010f, 0.062f),   // 杆到此为止，把手单独一件
            }), steel, parent);

            // 把手顶部一段压暗，跟金属底盘分出层次（实体分明暗才认得出形状）
            PropMesh.Spawn("TamperKnob",
                PropMesh.Revolve(new[]
                {
                    new Vector2(0.0010f, 0.0575f),
                    new Vector2(0.0090f, 0.0585f),
                    new Vector2(0.0090f, 0.0585f),
                    new Vector2(0.0125f, 0.0640f),   // 底部圆角
                    new Vector2(0.0155f, 0.0740f),   // 柱形把手（比球更像真实压粉器）
                    new Vector2(0.0165f, 0.0950f),
                    new Vector2(0.0155f, 0.1120f),   // 顶部圆角
                    new Vector2(0.0110f, 0.1250f),
                    new Vector2(0.0055f, 0.1290f),
                    new Vector2(0.0010f, 0.1300f),
                }), dark, go.transform);
            return go;
        }

        // ---------------------------------------------------------------- 马克杯

        public static GameObject Cup(Material ceramic, Transform parent)
        {
            var body = PropMesh.Spawn("Cup", PropMesh.Revolve(new[]
            {
                new Vector2(0.0010f, 0.000f),
                new Vector2(0.0600f, 0.000f),
                new Vector2(0.0600f, 0.000f),
                new Vector2(0.0660f, 0.005f),   // 底倒角
                new Vector2(0.0700f, 0.040f),   // 杯身微锥
                new Vector2(0.0725f, 0.080f),
                new Vector2(0.0725f, 0.085f),   // 口沿
                new Vector2(0.0725f, 0.085f),
                new Vector2(0.0690f, 0.085f),   // 杯口瓷厚 3.5mm —— 马克杯最强的识别特征
                new Vector2(0.0690f, 0.085f),
                new Vector2(0.0665f, 0.080f),
                new Vector2(0.0645f, 0.040f),
                new Vector2(0.0560f, 0.012f),   // 内底
                new Vector2(0.0010f, 0.012f),
            }), ceramic, parent);

            Handle(body.transform, new[]
            {
                new Vector3(0.065f, 0.030f, 0f),
                new Vector3(0.086f, 0.024f, 0f),
                new Vector3(0.100f, 0.044f, 0f),
                new Vector3(0.100f, 0.066f, 0f),
                new Vector3(0.084f, 0.080f, 0f),
                new Vector3(0.066f, 0.076f, 0f),
            }, 0.0075f, ceramic);
            return body;
        }

        /// <summary>杯里的咖啡液面：贴在杯内壁上，比杯口低一截。</summary>
        public static GameObject CupLiquid(Material mat, Transform parent)
        {
            return PropMesh.Spawn("CupCoffee", PropMesh.Revolve(new[]
            {
                new Vector2(0.0010f, 0.000f),
                new Vector2(0.0640f, 0.000f),
                new Vector2(0.0640f, 0.000f),
                new Vector2(0.0640f, 0.0008f),
                new Vector2(0.0010f, 0.0008f),
            }, 40), mat, parent);
        }

        // ---------------------------------------------------------------- 奶缸

        public static GameObject Pitcher(Material steel, Transform parent)
        {
            var body = PropMesh.Spawn("Pitcher", PropMesh.Revolve(new[]
            {
                new Vector2(0.0010f, 0.000f),
                new Vector2(0.0400f, 0.000f),
                new Vector2(0.0400f, 0.000f),
                new Vector2(0.0440f, 0.005f),
                new Vector2(0.0520f, 0.024f),   // 鼓形壶身
                new Vector2(0.0560f, 0.048f),
                new Vector2(0.0550f, 0.070f),
                new Vector2(0.0480f, 0.092f),   // 收口
                new Vector2(0.0450f, 0.105f),
                new Vector2(0.0450f, 0.105f),
                new Vector2(0.0420f, 0.105f),   // 口内沿
                new Vector2(0.0420f, 0.105f),
                new Vector2(0.0450f, 0.092f),
                new Vector2(0.0520f, 0.070f),
                new Vector2(0.0520f, 0.048f),
                new Vector2(0.0480f, 0.024f),
                new Vector2(0.0420f, 0.006f),
                new Vector2(0.0010f, 0.006f),
            }), steel, parent);

            // 壶嘴：旋转体做不出单边尖口，把 +x 侧上半段拉长并压低 —— 拉花壶的尖嘴就这么来
            var mesh = body.GetComponent<MeshFilter>().sharedMesh;
            Spout(mesh, 0.072f, 0.105f, 0.32f, 0.016f);

            Handle(body.transform, new[]
            {
                new Vector3(-0.050f, 0.030f, 0f),
                new Vector3(-0.070f, 0.026f, 0f),
                new Vector3(-0.080f, 0.048f, 0f),
                new Vector3(-0.076f, 0.075f, 0f),
                new Vector3(-0.058f, 0.086f, 0f),
                new Vector3(-0.046f, 0.080f, 0f),
            }, 0.0060f, steel);
            return body;
        }

        public static GameObject MilkSurface(Material mat, Transform parent)
        {
            return PropMesh.Spawn("Milk", PropMesh.Revolve(new[]
            {
                new Vector2(0.0010f, 0.000f),
                new Vector2(0.0430f, 0.000f),
                new Vector2(0.0430f, 0.000f),
                new Vector2(0.0430f, 0.0008f),
                new Vector2(0.0010f, 0.0008f),
            }, 40), mat, parent);
        }

        // ---------------------------------------------------------------- 糖浆瓶

        public static GameObject Bottle(Material glass, Transform parent)
        {
            var bottle = PropMesh.Spawn("Bottle", PropMesh.Revolve(new[]
            {
                new Vector2(0.0010f, 0.0000f),
                new Vector2(0.0180f, 0.0000f),
                new Vector2(0.0180f, 0.0000f),
                new Vector2(0.0210f, 0.0030f),
                new Vector2(0.0210f, 0.0580f),
                new Vector2(0.0200f, 0.0660f),   // 肩
                new Vector2(0.0160f, 0.0750f),
                new Vector2(0.0100f, 0.0790f),
                new Vector2(0.0100f, 0.0790f),
                new Vector2(0.0100f, 0.0920f),   // 颈
            }), glass, parent);

            PropMesh.Spawn("BottleCap", PropMesh.Revolve(new[]
            {
                new Vector2(0.0010f, 0.0920f),
                new Vector2(0.0140f, 0.0920f),
                new Vector2(0.0140f, 0.0920f),
                new Vector2(0.0140f, 0.1080f),
                new Vector2(0.0120f, 0.1100f),
                new Vector2(0.0010f, 0.1100f),
            }), PropMesh.Mat(new Color(0.30f, 0.29f, 0.27f), 0.6f, 0.55f), bottle.transform);
            return bottle;
        }

        // ---------------------------------------------------------------- 工作垫

        public static GameObject WorkPad(Material face, Material edge, Transform parent)
        {
            // 圆角 + 上下倒角。硬直角方块在近景里一眼就是"没加工过的 primitive"。
            // 两层：深色包边 + 内嵌的面，垫子才有厚度感，不是一块贴纸。
            var go = PropMesh.Spawn("WorkPad",
                PropMesh.Slab(0.66f, 0.24f, 0.012f, 0.042f, 0.003f, 6), edge, parent);
            var top = PropMesh.Spawn("WorkPadFace",
                PropMesh.Slab(0.618f, 0.198f, 0.010f, 0.038f, 0.002f, 6), face, go.transform);
            top.transform.localPosition = new Vector3(0f, 0.0028f, 0f);
            return go;
        }

        public static GameObject SpotRing(Material mat, Transform parent)
        {
            return PropMesh.Spawn("Ring", PropMesh.Ring(0.078f, 0.005f, 0.0022f), mat, parent);
        }

        // ---------------------------------------------------------------- 细节变形

        /// <summary>把手：在物体的本地 xy 平面里挂一根圆管。</summary>
        static void Handle(Transform owner, Vector3[] path, float radius, Material mat)
        {
            PropMesh.Spawn("Handle", PropMesh.Tube(path, radius), mat, owner);
        }

        /// <summary>把 +x 侧的上半段拉长并压低，做出一根壶嘴。</summary>
        static void Spout(Mesh m, float fromY, float toY, float reach, float drop)
        {
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i].y < fromY) continue;
                float ang = Mathf.Atan2(v[i].z, v[i].x);
                float w = Mathf.Exp(-(ang * ang) / 0.30f);           // 朝 +x 的高斯窗
                float k = Mathf.InverseLerp(fromY, toY, v[i].y);
                float s = 1f + reach * w * k;
                v[i].x *= s;
                v[i].z *= s;
                v[i].y -= drop * w * k * k;
            }
            m.vertices = v;
            m.RecalculateNormals();
        }

        /// <summary>只在顶面加一点起伏（比例空间 y≈1），像一捧松散的粉而不是一块塑料。</summary>
        static void Grain(Mesh m, float amp, float freq)
        {
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i].y < 0.90f) continue;
                float n = Mathf.PerlinNoise(v[i].x * freq + 11.3f, v[i].z * freq + 7.7f);
                v[i].y += (n - 0.5f) * amp;
            }
            m.vertices = v;
            m.RecalculateNormals();
        }
    }
}

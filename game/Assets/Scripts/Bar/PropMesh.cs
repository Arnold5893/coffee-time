using System.Collections.Generic;
using UnityEngine;

namespace CoffeeShop.Bar
{
    /// <summary>
    /// 台面道具的程序化网格（07 票返工三：把"一堆圆柱"换成有轮廓的器物）。
    ///
    /// 全部走同一条路 —— **一条二维轮廓绕 Y 轴转一圈**（车床 / lathe）。
    /// 一个轮廓数组就能画出：滤碗的锥形外壁 + 卷边 + 内腔、压粉器的倒角底盘 + 收腰杆 + 椭球把手、
    /// 马克杯的锥形杯身 + 杯口瓷厚 + 内壁、奶缸的鼓形壶身 + 收口、糖浆瓶的肩 + 颈 + 金属盖。
    ///
    /// 轮廓约定（很重要，UV 和法线方向都靠它）：
    ///   · 点的 x = 半径，y = 高度
    ///   · 顺序必须"底心 → 底面往外 → 外壁往上 → 顶面往内 → 顶心"（逆时针绕截面），法线才朝外
    ///   · 要在某处做硬边，就把同一个点连写两遍（这段不生成面，两侧各用一条切线算法线）
    ///
    /// 法线按轮廓切线**手算**，不是 RecalculateNormals：接缝处才不会有硬边。
    /// </summary>
    public static class PropMesh
    {
        public const int Seg = 32;   // 一圈多少段。近景只有 0.7m，32 段在屏幕上已经看不出棱。

        // ---------------------------------------------------------------- 旋转体

        public static Mesh Revolve(Vector2[] p, int segments = Seg, float startAngle = Mathf.PI)
        {
            int n = p.Length;
            var nrm = new Vector2[n];
            for (int i = 0; i < n; i++) nrm[i] = OutwardNormal(p, i);

            var verts = new Vector3[n * (segments + 1)];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];

            for (int s = 0; s <= segments; s++)
            {
                float a = startAngle + Mathf.PI * 2f * s / segments;
                float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                for (int i = 0; i < n; i++)
                {
                    int k = s * n + i;
                    verts[k] = new Vector3(p[i].x * cs, p[i].y, p[i].x * sn);
                    Vector3 nv = new Vector3(nrm[i].x * cs, nrm[i].y, nrm[i].x * sn);
                    norms[k] = nv.sqrMagnitude > 1e-8f ? nv.normalized : Vector3.up;
                    uvs[k] = new Vector2(s / (float)segments, i / (float)(n - 1));
                }
            }

            var tris = new List<int>((n - 1) * segments * 6);
            for (int s = 0; s < segments; s++)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    if ((p[i + 1] - p[i]).sqrMagnitude < 1e-13f) continue;   // 重复点 = 硬边接缝
                    int a = s * n + i, b = a + 1, c = (s + 1) * n + i, d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }

            var m = new Mesh { name = "revolve" };
            m.vertices = verts;
            m.normals = norms;
            m.uv = uvs;
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            EnsureOutward(m);
            return m;
        }

        /// <summary>轮廓点处朝外的二维法线（x 径向 / y 竖直）。相邻点重合即视为硬边。</summary>
        static Vector2 OutwardNormal(Vector2[] c, int i)
        {
            Vector2 a = i > 0 ? c[i] - c[i - 1] : Vector2.zero;
            Vector2 b = i < c.Length - 1 ? c[i + 1] - c[i] : Vector2.zero;
            bool ha = a.sqrMagnitude > 1e-13f;
            bool hb = b.sqrMagnitude > 1e-13f;
            Vector2 d = (ha && hb) ? (c[i + 1] - c[i - 1]) : (hb ? b : a);
            if (d.sqrMagnitude < 1e-13f) return Vector2.up;
            d.Normalize();
            return new Vector2(d.y, -d.x);
        }

        // ---------------------------------------------------------------- 圆管（把手 / 壶把）

        /// <summary>
        /// 沿一条三维路径扫一个圆截面。路径用 Catmull-Rom 先平滑，
        /// 所以给四五个控制点就是一条顺滑的把手，不用手工算切法。
        /// </summary>
        public static Mesh Tube(IList<Vector3> control, float radius, int ringSeg = 12, int smooth = 5)
        {
            var path = Smooth(control, smooth);
            int n = path.Count;

            var verts = new Vector3[n * ringSeg];
            var norms = new Vector3[verts.Length];

            for (int i = 0; i < n; i++)
            {
                Vector3 t = Tangent(path, i);
                Vector3 side = Vector3.Cross(t, Vector3.forward);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(t, Vector3.up);
                side.Normalize();
                Vector3 up = Vector3.Cross(side, t).normalized;

                for (int s = 0; s < ringSeg; s++)
                {
                    float a = Mathf.PI * 2f * s / ringSeg;
                    Vector3 dir = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    verts[i * ringSeg + s] = path[i] + dir * radius;
                    norms[i * ringSeg + s] = dir;
                }
            }

            var tris = new List<int>((n - 1) * ringSeg * 6);
            for (int i = 0; i < n - 1; i++)
            {
                for (int s = 0; s < ringSeg; s++)
                {
                    int s2 = (s + 1) % ringSeg;
                    int a = i * ringSeg + s, b = i * ringSeg + s2;
                    int c = (i + 1) * ringSeg + s, d = (i + 1) * ringSeg + s2;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }

            var m = new Mesh { name = "tube" };
            m.vertices = verts;
            m.normals = norms;
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            EnsureOutward(m);
            return m;
        }

        static Vector3 Tangent(IList<Vector3> path, int i)
        {
            int n = path.Count;
            Vector3 t = i == 0 ? path[1] - path[0]
                      : i == n - 1 ? path[n - 1] - path[n - 2]
                      : path[i + 1] - path[i - 1];
            return t.sqrMagnitude > 1e-10f ? t.normalized : Vector3.right;
        }

        /// <summary>Catmull-Rom 细分：控制点 → 平滑路径。</summary>
        static List<Vector3> Smooth(IList<Vector3> c, int per)
        {
            var outp = new List<Vector3>();
            int n = c.Count;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = c[Mathf.Max(0, i - 1)];
                Vector3 p1 = c[i];
                Vector3 p2 = c[i + 1];
                Vector3 p3 = c[Mathf.Min(n - 1, i + 2)];
                for (int k = 0; k < per; k++)
                {
                    float t = k / (float)per;
                    outp.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }
            outp.Add(c[n - 1]);
            return outp;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1
                         + (-p0 + p2) * t
                         + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                         + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        // ---------------------------------------------------------------- 圆角板（工作垫）

        /// <summary>
        /// 俯视圆角矩形、上下各带一圈倒角的扁板。桌面上的垫子、托盘都是它。
        /// 四角硬直角的方块在近景里一眼就是"未加工的 primitive"。
        /// </summary>
        public static Mesh Slab(float width, float depth, float height, float corner, float bevel, int arcSeg = 5)
        {
            corner = Mathf.Clamp(corner, 0.0005f, Mathf.Min(width, depth) * 0.48f);
            bevel = Mathf.Clamp(bevel, 0.0002f, height * 0.45f);

            var outer = RoundRect(width, depth, corner, arcSeg);
            var inner = RoundRect(width - bevel * 2f, depth - bevel * 2f,
                                  Mathf.Max(0.0005f, corner - bevel), arcSeg);

            int n = outer.Count;
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();

            // 四条高度环：底内缩 → 底倒角顶 → 上倒角底 → 顶内缩
            float[] ys = { 0f, bevel, height - bevel, height };
            for (int r = 0; r < 4; r++)
            {
                bool shrunk = r == 0 || r == 3;
                var ring = shrunk ? inner : outer;
                for (int i = 0; i < n; i++)
                {
                    verts.Add(new Vector3(ring[i].x, ys[r], ring[i].y));
                    norms.Add(Vector3.up);   // 侧面法线最后按实际朝向修正
                }
            }

            // 侧面
            var tris = new List<int>();
            for (int r = 0; r < 3; r++)
            {
                for (int i = 0; i < n; i++)
                {
                    int i2 = (i + 1) % n;
                    int a = r * n + i, b = r * n + i2;
                    int c = (r + 1) * n + i, d = (r + 1) * n + i2;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }

            // 上下封口（圆角矩形是凸的，从中心扇形三角化就够）
            int cTop = verts.Count; verts.Add(new Vector3(0f, height, 0f)); norms.Add(Vector3.up);
            int cBot = verts.Count; verts.Add(new Vector3(0f, 0f, 0f)); norms.Add(Vector3.down);
            for (int i = 0; i < n; i++)
            {
                int i2 = (i + 1) % n;
                tris.Add(cTop); tris.Add(3 * n + i); tris.Add(3 * n + i2);
                tris.Add(cBot); tris.Add(i2); tris.Add(i);
            }

            var m = new Mesh { name = "slab" };
            m.vertices = verts.ToArray();
            m.normals = norms.ToArray();
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();      // 侧面法线按实际几何算
            m.RecalculateBounds();
            return m;
        }

        static List<Vector2> RoundRect(float w, float d, float r, int arcSeg)
        {
            float hx = w * 0.5f, hz = d * 0.5f;
            float ix = hx - r, iz = hz - r;
            var pts = new List<Vector2>();
            pts.Add(new Vector2(hx, 0f));                                    // +x 边中点
            for (int k = 1; k <= arcSeg; k++)                                // 右上角 0°→90°
            {
                float a = Mathf.PI * 0.5f * k / arcSeg;
                pts.Add(new Vector2(ix + r * Mathf.Cos(a), iz + r * Mathf.Sin(a)));
            }
            for (int k = 1; k <= arcSeg; k++)                                // 左上角 90°→180°
            {
                float a = Mathf.PI * 0.5f + Mathf.PI * 0.5f * k / arcSeg;
                pts.Add(new Vector2(-ix + r * Mathf.Cos(a), iz + r * Mathf.Sin(a)));
            }
            for (int k = 1; k <= arcSeg; k++)                                // 左下角
            {
                float a = Mathf.PI + Mathf.PI * 0.5f * k / arcSeg;
                pts.Add(new Vector2(-ix + r * Mathf.Cos(a), -iz + r * Mathf.Sin(a)));
            }
            for (int k = 1; k <= arcSeg; k++)                                // 右下角
            {
                float a = Mathf.PI * 1.5f + Mathf.PI * 0.5f * k / arcSeg;
                pts.Add(new Vector2(ix + r * Mathf.Cos(a), -iz + r * Mathf.Sin(a)));
            }
            return pts;
        }

        /// <summary>扁平圆环（落点提示圈）：内孔是真孔，不是一个压在台面上的实心圆饼。</summary>
        public static Mesh Ring(float radius, float thickness, float height)
        {
            return Revolve(new[]
            {
                new Vector2(radius - thickness, 0f),
                new Vector2(radius, 0f),
                new Vector2(radius, 0f),
                new Vector2(radius, height),
                new Vector2(radius, height),
                new Vector2(radius - thickness, height),
                new Vector2(radius - thickness, height),
                new Vector2(radius - thickness, 0f),
            });
        }

        // ---------------------------------------------------------------- 通用

        /// <summary>
        /// 法线已经有了，但三角形绕序可能反 —— 逐面比较面法线与顶点法线，多数反向就整体翻。
        /// 生几何时不必每处都手推顺时针。
        /// </summary>
        public static void EnsureOutward(Mesh m)
        {
            var v = m.vertices;
            var n = m.normals;
            if (n == null || n.Length != v.Length) return;
            int[] t = m.triangles;

            int bad = 0, good = 0;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 fn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (fn.sqrMagnitude < 1e-18f) continue;
                Vector3 vn = n[t[i]] + n[t[i + 1]] + n[t[i + 2]];
                if (Vector3.Dot(fn, vn) < 0f) bad++; else good++;
            }
            if (bad <= good) return;
            for (int i = 0; i < t.Length; i += 3)
            {
                int tmp = t[i]; t[i] = t[i + 1]; t[i + 1] = tmp;
            }
            m.triangles = t;
        }

        /// <summary>建一个只用来画的 GameObject：没有 Collider、不投影、pivot 就地不动。</summary>
        public static GameObject Spawn(string name, Mesh mesh, Material mat, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
            return go;
        }

        public static Material Mat(Color c, float metallic, float smooth)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh);
            // URP Lit 的主色是 _BaseColor；Material.color 走的是 _Color，在 URP 下静默无效，
            // 设进去的道具会全是白模、只剩环境反射的颜色（钢碗发蓝、咖啡粉发白就是这么来的）。
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            else m.color = c;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            return m;
        }
    }
}

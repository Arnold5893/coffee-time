using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CoffeeShop.Seating
{
    /// <summary>
    /// 座位总装配（04 票 / FR-SEAT-01）：读配置 → 在场景里找到每把椅子 → 解析出
    /// 坐下点 / 站立点 / 朝向 / 桌面点位 → 建立 SeatRegistry。
    ///
    /// 关键点：配置里没有一处世界坐标。家具在 build_shop.py 里挪位后重跑 ProjectSetup，
    /// 座位全部自动跟随，不需要回来改 JSON。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class SeatingManager : MonoBehaviour
    {
        [Tooltip("场景根（FBX 实例）。留空则自动找名为 CoffeeShop 的对象")]
        public Transform shopRoot;
        [Tooltip("覆盖 Resources/Config/seats 的配置，仅调试用")]
        public TextAsset configOverride;

        public List<SeatAnchor> Seats { get; } = new List<SeatAnchor>();
        public List<string> Missing { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        public SeatRegistry Registry { get; } = new SeatRegistry();
        public bool Built { get; private set; }

        void Awake()
        {
            if (!Built) Build();
        }

        void Start()
        {
            if (!Built) Build();   // Awake 被别的脚本抢跑时兜底
        }

        public void Build()
        {
            Seats.Clear();
            Missing.Clear();
            Errors.Clear();

            SeatConfigFile file;
            try
            {
                file = configOverride != null
                    ? SeatConfig.Parse(configOverride.text)
                    : SeatConfig.LoadFromResources();
            }
            catch (System.Exception e)
            {
                Errors.Add("配置读取失败: " + e.Message);
                Built = true;
                return;
            }

            Errors.AddRange(SeatConfig.Validate(file));

            var root = shopRoot != null ? shopRoot : FindShopRoot();
            if (root == null)
            {
                Errors.Add("场景里找不到 CoffeeShop 根节点");
                Built = true;
                return;
            }

            // 一次性建立名字索引，避免每个座位都全场景遍历一遍
            var map = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!map.ContainsKey(t.name)) map[t.name] = t;

            foreach (var entry in file.seats)
            {
                if (entry == null) continue;
                var seat = Resolve(entry, map);
                if (seat == null) { Missing.Add(entry.id); continue; }
                Seats.Add(seat);
                Registry.Add(seat.State);
            }

            Built = true;
        }

        SeatAnchor Resolve(SeatEntry entry, Dictionary<string, Transform> map)
        {
            if (!map.TryGetValue(entry.anchor ?? string.Empty, out var anchor) || anchor == null)
                return null;
            var anchorR = anchor.GetComponentInChildren<Renderer>();
            if (anchorR == null) return null;

            Bounds ab = anchorR.bounds;
            float floorY = ab.min.y;
            var seatPoint = new Vector3(ab.center.x, floorY, ab.center.z);
            var kind = SeatConfig.ParseKind(entry.kind);

            Vector3 facing = ResolveFacing(entry, map, seatPoint, ab);
            if (facing.sqrMagnitude < 1e-6f)
            {
                // 最后兜底：用对象自身的前向水平投影（FBX 里可能正好是背面，仅作保险）
                facing = Vector3.ProjectOnPlane(anchor.forward, Vector3.up);
                if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
            }
            facing = new Vector3(facing.x, 0f, facing.z).normalized;

            float seatHeight = SeatConfig.ResolveSeatHeight(entry);
            float standOffset = entry.standOffset > 0.01f ? entry.standOffset : 0.55f;

            return new SeatAnchor
            {
                Entry = entry,
                State = new SeatState(entry.id, entry.zone, kind),
                Anchor = anchor,
                SeatPoint = seatPoint + facing * 0.02f,
                Facing = Quaternion.LookRotation(facing, Vector3.up),
                StandPoint = ResolveStandPoint(seatPoint, facing, standOffset, floorY),
                SurfacePoint = ResolveSurface(entry, map, seatPoint, facing),
                SeatHeight = seatHeight,
            };
        }

        /// <summary>
        /// 朝向优先用 faceTarget（朝它最近的一面），其次用 faceDir 方向名，最后用对象自身前向。
        /// 用 ClosestPoint 而不是"目标包围盒中心"：吧台、长桌是长条形，朝中心会算歪。
        /// </summary>
        Vector3 ResolveFacing(SeatEntry entry, Dictionary<string, Transform> map, Vector3 seatPoint, Bounds own)
        {
            if (!string.IsNullOrEmpty(entry.faceTarget) &&
                map.TryGetValue(entry.faceTarget, out var ft) && ft != null)
            {
                var r = ft.GetComponentInChildren<Renderer>();
                if (r != null)
                {
                    Bounds b = r.bounds;
                    Vector3 closest = b.ClosestPoint(seatPoint);
                    Vector3 d = closest - seatPoint; d.y = 0f;
                    if (d.sqrMagnitude > 1e-4f) return d;
                    // 座位已经落在目标包围盒里：退化成指向目标中心
                    Vector3 c = b.center - seatPoint; c.y = 0f;
                    if (c.sqrMagnitude > 1e-4f) return c;
                }
            }

            Vector3 dir = SeatConfig.ParseFaceDir(entry.faceDir);
            if (dir.sqrMagnitude > 1e-6f) return dir;

            return Vector3.zero;
        }

        /// <summary>
        /// 起身站位：优先座位正后方，被墙或邻座挡住就偏到斜后方，再不行就缩短距离。
        /// 店里不少椅子背后就是墙（会议桌靠墙那排、圆桌靠边那把），
        /// 只沿正后方硬退会一路退到人贴在椅子上。
        /// </summary>
        Vector3 ResolveStandPoint(Vector3 seatPoint, Vector3 facing, float offset, float floorY)
        {
            Physics.SyncTransforms();
            Vector3 baseP = new Vector3(seatPoint.x, floorY, seatPoint.z);
            var angles = new[] { 0f, 22f, -22f, 45f, -45f, 68f, -68f, 90f, -90f };
            var dists = new[] { offset, offset * 0.82f, offset * 0.66f, offset * 0.52f };

            foreach (float dist in dists)
            {
                foreach (float a in angles)
                {
                    Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * (-facing);
                    Vector3 p = baseP + dir * dist;
                    if (!StandBlocked(p, floorY)) return p;
                }
            }
            return baseP;   // 四面都被围住（靠墙角落）：原地起立，让控制器自己挤开
        }

        static bool StandBlocked(Vector3 p, float floorY)
        {
            Vector3 a = new Vector3(p.x, floorY + 0.55f, p.z);
            Vector3 b = new Vector3(p.x, floorY + 1.35f, p.z);
            return Physics.CheckCapsule(a, b, 0.28f, ~BoundaryMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 桌面点位（放杯子的地方）。
        ///
        /// 两种取法：
        ///   ① 配置显式给了 surfaceHeight —— 直接用（吧台台面被陈列柜挡着、阳光区长桌和窗户是
        ///      同一个网格，这两种情况射线探不准，只能写死台面高度）；
        ///   ② 没给高度 —— 朝桌子边缘往里挪 15cm 打一条向下的射线，命中什么就放什么上面
        ///      （圆桌、会议桌都适用，桌上摆着书也不会算错）。
        /// </summary>
        Vector3 ResolveSurface(SeatEntry entry, Dictionary<string, Transform> map, Vector3 seatPoint, Vector3 facing)
        {
            float forward = entry.surfaceForward > 0.01f ? entry.surfaceForward : 0.50f;
            Vector3 probe = new Vector3(seatPoint.x, 0f, seatPoint.z) + facing * forward;

            // 有明确的桌子对象时，取"桌沿最近点再往里挪 15cm"：太靠里会打到桌上的摆件，太靠边会掉到桌外
            if (!string.IsNullOrEmpty(entry.faceTarget) &&
                map.TryGetValue(entry.faceTarget, out var ft) && ft != null)
            {
                var r = ft.GetComponentInChildren<Renderer>();
                if (r != null)
                {
                    Bounds b = r.bounds;
                    Vector3 cp = b.ClosestPoint(new Vector3(seatPoint.x, b.center.y, seatPoint.z));
                    Vector3 inward = b.center - cp; inward.y = 0f;
                    if (inward.sqrMagnitude > 1e-4f) cp += inward.normalized * 0.15f;
                    probe = new Vector3(cp.x, 0f, cp.z);
                }
            }

            if (entry.surfaceHeight > 0.01f)
                return new Vector3(probe.x, seatPoint.y + entry.surfaceHeight, probe.z);

            var origin = new Vector3(probe.x, seatPoint.y + 2.2f, probe.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, 3.0f, ~BoundaryMask, QueryTriggerInteraction.Ignore))
                return new Vector3(probe.x, hit.point.y + 0.02f, probe.z);

            return new Vector3(probe.x, seatPoint.y + 0.75f, probe.z);   // 什么都没探到：按常见桌高兜底
        }

        static int BoundaryMask
        {
            get
            {
                if (boundaryMask < 0) boundaryMask = LayerMask.GetMask("Boundary");
                return boundaryMask;
            }
        }
        static int boundaryMask = -1;

        static Transform FindShopRoot()
        {
            var go = GameObject.Find("CoffeeShop");
            return go != null ? go.transform : null;
        }

        // ---------------- 查询 ----------------

        /// <summary>离某个位置最近的空座位（水平距离）。没有就返回 null。</summary>
        public SeatAnchor NearestFree(Vector3 worldPos, float maxDistance)
        {
            SeatAnchor best = null;
            float bestD = maxDistance * maxDistance;
            for (int i = 0; i < Seats.Count; i++)
            {
                var s = Seats[i];
                if (!s.IsFree) continue;
                Vector3 d = s.SeatPoint - worldPos; d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq < bestD) { bestD = sq; best = s; }
            }
            return best;
        }

        public SeatAnchor Find(string id) => Seats.FirstOrDefault(s => s.Id == id);

        public int CountInZone(string zone) => Seats.Count(s => s.Zone == zone);

        /// <summary>每个区域有几个座位，用于报告与验收。</summary>
        public string ZoneSummary()
        {
            var zones = Seats.Select(s => s.Zone).Distinct().OrderBy(z => z);
            return string.Join(" ", zones.Select(z => $"{z}={CountInZone(z)}"));
        }

        public string Describe() =>
            $"seats={Seats.Count} missing={Missing.Count} errors={Errors.Count} {ZoneSummary()}";
    }
}

using System.Collections;
using System.Linq;
using System.Text;
using CoffeeShop.Controls;
using CoffeeShop.Player;
using CoffeeShop.Seating;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CoffeeShop.Testing
{
    /// <summary>
    /// 04 票自动化验收：座位解析 → 靠近出提示 → 按交互坐下 → 重复占用被拒 → 起身释放。
    /// 交互走真实输入链路（虚拟摇杆的点按），不直接改 SeatInteraction 内部状态。
    /// </summary>
    public class SeatFlowTest : MonoBehaviour
    {
        public PlayerMotor player;
        public PlayerInputRouter router;
        public SeatingManager seating;
        public SeatInteraction seatAct;

        readonly StringBuilder log = new StringBuilder();
        int failures;

        void Check(bool ok, string what)
        {
            if (!ok) { failures++; log.AppendLine("  FAIL " + what); }
            else log.AppendLine("  ok   " + what);
        }

        IEnumerator Start()
        {
            if (player == null || router == null || seating == null || seatAct == null)
            {
                Finish("MISSING-REFS");
                yield break;
            }

            router.SetDevice(PlayerInputRouter.Device.VirtualJoystick);
            router.SetSuppressed(true);
            Time.captureDeltaTime = 1f / 60f;
            for (int i = 0; i < 20; i++) yield return null;

            // ---- 1. 配置驱动的座位全部落地 --------------------------------
            log.AppendLine($"registry: {seating.Describe()}");
            Check(seating.Errors.Count == 0, "配置无错误" + (seating.Errors.Count > 0 ? ": " + string.Join("; ", seating.Errors) : ""));
            Check(seating.Missing.Count == 0, "所有 anchor 都在场景里找到" +
                  (seating.Missing.Count > 0 ? ": 缺 " + string.Join(", ", seating.Missing) : ""));
            Check(seating.Seats.Count >= 20, $"座位总数 {seating.Seats.Count} >= 20");
            Check(seating.CountInZone("bar") == 3, "吧台凳 3 个");
            Check(seating.CountInZone("round") == 12, "圆桌座位 12 个");
            Check(seating.CountInZone("meeting") == 6, "会议桌座位 6 个");
            Check(seating.CountInZone("sun") == 12, "阳光区座位 12 个");

            // ---- 2. 每个座位的几何都解析出来了 ----------------------------
            int badFacing = 0, badSurface = 0, badStand = 0, badSeat = 0, crampedStand = 0;
            foreach (var s in seating.Seats)
            {
                Vector3 fwd = s.Facing * Vector3.forward;
                if (Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.05f || Mathf.Abs(new Vector2(fwd.x, fwd.z).magnitude - 1f) > 0.02f)
                    badFacing++;
                if (s.SurfacePoint.y < 0.35f || s.SurfacePoint.y > 1.45f)
                {
                    badSurface++;
                    log.AppendLine($"    badSurface {s.Id}: y={s.SurfacePoint.y:F2} at ({s.SurfacePoint.x:F2},{s.SurfacePoint.z:F2})");
                }
                float standGap = new Vector2(s.StandPoint.x - s.SeatPoint.x, s.StandPoint.z - s.SeatPoint.z).magnitude;
                if (standGap > 0.70f)
                {
                    badStand++;
                    log.AppendLine($"    badStand {s.Id}: gap={standGap:F2} 站位跑得比座位还远");
                }
                else if (standGap < 0.15f)
                {
                    // 四周全是家具（背后就是圆桌/墙）的座位只能原地起立，属于真实布局限制，单独计数
                    crampedStand++;
                    Vector3 standDir = s.Facing * Vector3.forward;
                    Vector3 ideal = new Vector3(s.SeatPoint.x, 0f, s.SeatPoint.z) - standDir * 0.55f;
                    var ca = new Vector3(ideal.x, s.SeatPoint.y + 0.55f, ideal.z);
                    var cb = new Vector3(ideal.x, s.SeatPoint.y + 1.35f, ideal.z);
                    var hits = Physics.OverlapCapsule(ca, cb, 0.28f,
                        ~LayerMask.GetMask("Boundary"), QueryTriggerInteraction.Ignore);
                    string blockers = hits.Length == 0
                        ? "none"
                        : string.Join(" | ", hits.Select(h => h.name));
                    log.AppendLine($"    cramped {s.Id}: 原地起立，理想站位被挡住: {blockers}");
                }
                if (s.SeatHeight < 0.30f || s.SeatHeight > 0.90f) badSeat++;
            }
            log.AppendLine($"geometry: badFacing={badFacing} badSurface={badSurface} badStand={badStand} badSeat={badSeat} cramped={crampedStand}");
            Check(badFacing == 0, "所有座位朝向都是水平单位向量");
            Check(badSurface == 0, "所有桌面点位高度合理");
            Check(badStand == 0, "所有站位都在座位后方 0.70m 以内");
            Check(crampedStand <= 3, $"原地起立的夹缝座 {crampedStand} 个（上限 3，背后就是家具）");
            Check(badSeat == 0, "所有座面高度合理");

            // ---- 3. 走到吧台凳旁：提示与最近空座 --------------------------
            var seat = seating.Find("bar-2");
            Check(seat != null, "找到 bar-2");
            if (seat == null) { Finish(null); yield break; }

            player.Teleport(seat.StandPoint, seat.Facing);
            for (int i = 0; i < 20; i++) yield return null;
            log.AppendLine($"near seat: {seat}  player={player.transform.position}");
            Check(seatAct.NearestFree == seat, "站位 0.5m 内识别到这张空座");
            Check(seat.IsFree, "坐下前是空的");

            // 太远就不该有提示
            var farPoint = seat.StandPoint - (seat.Facing * Vector3.forward) * 3f;
            Check(seating.NearestFree(farPoint, seatAct.reachRadius) == null, "离 3m 远时不提示");

            // ---- 4. 按交互坐下 --------------------------------------------
            yield return PressInteract();
            float wait = 0f;
            while (seatAct.Blend < 0.999f && wait < 2f) { wait += Time.deltaTime; yield return null; }
            log.AppendLine($"sit: seated={seatAct.IsSeated} blend={seatAct.Blend:F2} " +
                           $"occupant={seat.State.Occupant} motorEnabled={player.enabled} ctl={player.ControllerEnabled}");
            Check(seatAct.IsSeated, "按下交互后进入坐姿");
            Check(seatAct.Blend > 0.99f, "坐下过渡走完");
            Check(!seat.IsFree, "座位被标记为占用");
            Check(seat.State.Occupant == "player", "占用者是玩家");
            Check(!player.ControllerEnabled, "坐下期间角色控制器关闭（不会被重力拖走）");
            float seatDist = Vector3.Distance(
                new Vector3(player.transform.position.x, 0f, player.transform.position.z),
                new Vector3(seat.SeatPoint.x, 0f, seat.SeatPoint.z));
            Check(seatDist < 0.15f, $"人落在座位点上（偏差 {seatDist:F2}m）");

            // ---- 5. 已占用座位不能被重复使用 ------------------------------
            bool otherOccupied = seating.Registry.TryOccupy(seat.Id, "npc-1");
            bool playerReSit = seatAct.TrySit(seat);
            log.AppendLine($"reuse: npc={otherOccupied} playerTrySit={playerReSit} rejected={seatAct.RejectedSits}");
            Check(!otherOccupied, "别的角色抢不到已占用的座位");
            Check(!playerReSit, "玩家不能在同一张座位上再坐一次");
            Check(seatAct.IsSeated, "重复占用请求不影响当前坐姿");

            // ---- 6. 再按交互起身 ------------------------------------------
            yield return PressInteract();
            wait = 0f;
            while (seatAct.IsSeated && wait < 2f) { wait += Time.deltaTime; yield return null; }
            log.AppendLine($"stand: seated={seatAct.IsSeated} free={seat.IsFree} " +
                           $"ctl={player.ControllerEnabled} pos={player.transform.position}");
            Check(!seatAct.IsSeated, "起身后退出坐姿");
            Check(seat.IsFree, "起身后座位被释放");
            Check(player.ControllerEnabled, "角色控制器恢复");
            float standDist = Vector2.Distance(
                new Vector2(player.transform.position.x, player.transform.position.z),
                new Vector2(seat.StandPoint.x, seat.StandPoint.z));
            Check(standDist < 0.20f, $"人回到座位后方的站位（偏差 {standDist:F2}m）");

            // ---- 7. 释放后可以再坐一次（座位能循环使用） ------------------
            bool reuse = seatAct.TrySit(seat);
            Check(reuse && !seat.IsFree, "释放后的座位可以再次占用");
            seatAct.TryStand();
            wait = 0f;
            while (seatAct.IsSeated && wait < 2f) { wait += Time.deltaTime; yield return null; }
            Check(seat.IsFree, "二次起身后座位再次释放");

            // ---- 8. 换一张桌边的椅子，确认不同区域都可用 ------------------
            var roundSeat = seating.Seats.Find(s => s.Zone == "round");
            if (roundSeat != null)
            {
                player.Teleport(roundSeat.StandPoint, roundSeat.Facing);
                for (int i = 0; i < 15; i++) yield return null;
                bool sat = seatAct.TrySit(roundSeat);
                for (int i = 0; i < 45; i++) yield return null;
                log.AppendLine($"round seat: {roundSeat.Id} sat={sat} blend={seatAct.Blend:F2}");
                Check(sat, "圆桌椅子也能坐下（座面高度与吧台凳不同）");
                seatAct.TryStand();
                for (int i = 0; i < 30; i++) yield return null;
                Check(roundSeat.IsFree, "圆桌椅子起身后释放");
            }

            log.AppendLine($"counts: sit={seatAct.SitCount} stand={seatAct.StandCount} rejected={seatAct.RejectedSits}");
            Finish(null);
        }

        /// <summary>走真实输入链路点一次交互：写进虚拟摇杆 → 等两帧让 router 与交互各自读到。</summary>
        IEnumerator PressInteract()
        {
            router.Virtual.PressInteract();
            for (int i = 0; i < 3; i++) yield return null;
        }

        void Finish(string fatal)
        {
            Time.captureDeltaTime = 0f;
            if (fatal != null) { log.AppendLine("FATAL " + fatal); failures++; }
            log.AppendLine($"failures={failures}");
            log.AppendLine(failures == 0 ? "SEAT RESULT: PASS" : "SEAT RESULT: FAIL");
            Debug.Log("[SEAT]\n" + log);
            System.IO.File.WriteAllText("seat_flow.txt", log.ToString());
#if UNITY_EDITOR
            EditorApplication.Exit(failures == 0 ? 0 : 1);
#else
            Application.Quit(failures == 0 ? 0 : 1);
#endif
        }
    }
}

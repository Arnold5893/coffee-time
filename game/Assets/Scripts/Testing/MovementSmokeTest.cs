using System.Collections;
using System.Collections.Generic;
using System.Text;
using CoffeeShop.Cameras;
using CoffeeShop.Controls;
using CoffeeShop.Player;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CoffeeShop.Testing
{
    /// <summary>
    /// 02 票自动化验收：用虚拟输入驱动角色，量化"碰撞生效"与"走一圈不卡死"。
    /// 跑完打印 SMOKE 行并退出，由批处理日志留证。
    /// </summary>
    public class MovementSmokeTest : MonoBehaviour
    {
        public bool verbose;
        public PlayerMotor player;
        public PlayerInputRouter router;
        public ThirdPersonCameraRig rig;

        /// <summary>店面水平范围（用来判定角色有没有穿墙跑出去）</summary>
        public Bounds room;
        /// <summary>后操作台范围（顾客侧绝不该进入）</summary>
        public Bounds backCounter;
        /// <summary>前柜台范围，用来定位吧台内通道</summary>
        public Bounds frontCounter;
        /// <summary>起点集合：出生点 + 几个关键位</summary>
        public List<Vector3> anchors = new List<Vector3>();

        readonly StringBuilder log = new StringBuilder();
        int failures;

        IEnumerator Start()
        {
            if (player == null || router == null) { Finish("MISSING-REFS"); yield break; }

            router.SetDevice(PlayerInputRouter.Device.VirtualJoystick);
            router.SetSuppressed(true);

            // 批处理下渲染帧率可达上万，deltaTime 小到 1e-4 量级时 CharacterController 的扫掠会被
            // 接触偏移吃掉，位移恒为 0。这里固定步长，让测量等价于 60fps 的真实游玩。
            Time.captureDeltaTime = 1f / 60f;

            // 等物理落位
            for (int i = 0; i < 20; i++) yield return null;

            // --- 1. 八方向位移：从每个起点出发，8 个方向各推 1.2 秒 ---
            var dirs = new List<Vector3>();
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                dirs.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }

            int stuckCount = 0, outOfRoom = 0, probes = 0;
            foreach (var anchor in anchors)
            {
                var freeDirs = 0;
                foreach (var d in dirs)
                {
                    yield return DriveFrom(anchor, d, 1.2f, (moved, end) =>
                    {
                        probes++;
                        if (moved < 0.25f) { /* 这个方向被家具挡住，正常 */ }
                        else freeDirs++;
                        if (!InRoom(end)) outOfRoom++;
                    });
                }
                // 一个点至少要有 3 个方向能走 0.25m 以上，否则视为死角
                log.AppendLine($"  anchor=({anchor.x:F1},{anchor.z:F1}) freeDirs={freeDirs}/8");
                if (freeDirs < 3) { stuckCount++; log.AppendLine("    ^ DEAD-END"); }
            }
            log.AppendLine($"probes={probes} deadEnds={stuckCount} outOfRoom={outOfRoom}");
            if (stuckCount > 0 || outOfRoom > 0) failures++;

            // --- 2. 撞后操作台：站在吧台内通道正中，朝后操作台推进，不得穿进去 ---
            Vector3 aisleMid = new Vector3((backCounter.center.x + frontCounter.center.x) * 0.5f, 0.06f,
                                           (backCounter.center.z + frontCounter.center.z) * 0.5f);
            Vector3 towardBack = backCounter.center - aisleMid; towardBack.y = 0f; towardBack.Normalize();
            float nearest = float.MaxValue;
            float endMoved = 0f;
            yield return DriveFrom(aisleMid, towardBack, 2.5f, (moved, end) => { endMoved = moved; },
                                   eachFrame: p =>
                                   {
                                       float dx = Mathf.Max(backCounter.min.x - p.x, 0f, p.x - backCounter.max.x);
                                       float dz = Mathf.Max(backCounter.min.z - p.z, 0f, p.z - backCounter.max.z);
                                       nearest = Mathf.Min(nearest, Mathf.Sqrt(dx * dx + dz * dz));
                                   });
            log.AppendLine($"barBlock: start=({aisleMid.x:F2},{aisleMid.z:F2}) moved={endMoved:F2} " +
                           $"minPlanarDistToBackCounter={nearest:F2} (>=0.20 means blocked outside)");
            if (nearest < 0.20f || endMoved < 0.3f) { failures++; log.AppendLine("    ^ BAR NOT BLOCKING"); }

            // --- 3. 碰撞整体有效性：朝墙角推进不得越出店面 ---
            Vector3 center = new Vector3(room.center.x, 0.06f, room.center.z);
            bool escaped = false;
            yield return DriveFrom(center, new Vector3(0.7f, 0f, 0.7f).normalized, 4.0f,
                                   null, eachFrame: p => { if (!InRoom(p)) escaped = true; });
            log.AppendLine($"wallEscape={escaped}");
            if (escaped) failures++;

            router.Virtual.Clear();
            Finish(failures == 0 ? "PASS" : "FAIL");
        }

        IEnumerator DriveFrom(Vector3 start, Vector3 worldDir, float seconds,
                             System.Action<float, Vector3> onDone,
                             System.Action<Vector3> eachFrame = null)
        {
            player.Teleport(start, Quaternion.LookRotation(worldDir, Vector3.up));
            router.Virtual.Clear();
            yield return null;

            Vector3 begin = player.transform.position;
            bool firstFrame = true;
            float t = 0f;
            while (t < seconds)
            {
                // 世界方向 → 相机相对分量（角色移动是相机相对的）
                Vector3 f = Vector3.ProjectOnPlane(rig.transform.forward, Vector3.up).normalized;
                Vector3 r = Vector3.Cross(Vector3.up, f).normalized;
                router.Virtual.SetMove(new Vector2(Vector3.Dot(worldDir, r), Vector3.Dot(worldDir, f)));
                if (verbose && firstFrame)
                {
                    firstFrame = false;
                    Debug.Log($"[SMOKE-FRAME] ticks={player.UpdateTicks} ctrlEnabled={player.ControllerEnabled} " +
                              $"grounded={player.IsGrounded} intent=({router.Intent.Move.x:F2},{router.Intent.Move.y:F2}) " +
                              $"move=input({router.CurrentDevice}) pos={player.transform.position}");
                }
                eachFrame?.Invoke(player.transform.position);
                t += Time.deltaTime;
                yield return null;
            }
            router.Virtual.Clear();
            for (int i = 0; i < 3; i++) yield return null;

            Vector3 end = player.transform.position;
            float moved = Vector3.Distance(new Vector3(begin.x, 0, begin.z), new Vector3(end.x, 0, end.z));
            if (verbose)
                Debug.Log($"[SMOKE-DRIVE] from=({begin.x:F2},{begin.z:F2}) dir=({worldDir.x:F1},{worldDir.z:F1}) " +
                          $"intent=({router.Intent.Move.x:F2},{router.Intent.Move.y:F2}) to=({end.x:F2},{end.z:F2}) moved={moved:F2} " +
                          $"grounded={player.IsGrounded}");
            onDone?.Invoke(moved, end);
        }

        bool InRoom(Vector3 p) =>
            p.x > room.min.x + 0.1f && p.x < room.max.x - 0.1f &&
            p.z > room.min.z + 0.1f && p.z < room.max.z - 0.1f;

        void Finish(string verdict)
        {
            Time.captureDeltaTime = 0f;
            log.AppendLine($"SMOKE RESULT: {verdict}");
            Debug.Log("[SMOKE]\n" + log);
            System.IO.File.WriteAllText("movement_smoke.txt", log.ToString());
#if UNITY_EDITOR
            EditorApplication.Exit(verdict == "PASS" ? 0 : 1);
#else
            Application.Quit(verdict == "PASS" ? 0 : 1);
#endif
        }
    }
}

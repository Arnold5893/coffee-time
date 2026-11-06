using System.Collections;
using System.Text;
using CoffeeShop.Cameras;
using CoffeeShop.Controls;
using CoffeeShop.Interaction;
using CoffeeShop.Player;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CoffeeShop.Testing
{
    /// <summary>
    /// 03 票自动化验收：门外出生 → 点门 → 开门 → 自己走进去 → 门自动关。
    /// 全程用虚拟输入驱动真实控制器与真实射线交互，不直接改状态。
    /// </summary>
    public class EntryFlowTest : MonoBehaviour
    {
        public PlayerMotor player;
        public PlayerInputRouter router;
        public EntranceDoor door;
        public ThirdPersonCameraRig followRig;
        public Transform spawnPoint;

        readonly StringBuilder log = new StringBuilder();
        int failures;

        void Check(bool ok, string what)
        {
            if (!ok) { failures++; log.AppendLine("  FAIL " + what); }
            else log.AppendLine("  ok   " + what);
        }

        IEnumerator Start()
        {
            if (player == null || router == null || door == null || spawnPoint == null)
            {
                Finish("MISSING-REFS");
                yield break;
            }

            router.SetDevice(PlayerInputRouter.Device.VirtualJoystick);
            router.SetSuppressed(true);
            Time.captureDeltaTime = 1f / 60f;

            for (int i = 0; i < 20; i++) yield return null;

            // ---- 1. 出生点：在门外 ----------------------------------------
            player.Teleport(spawnPoint.position, spawnPoint.rotation);
            door.ForceClosed();
            for (int i = 0; i < 20; i++) yield return null;
            float spawnDepth = door.InsideDepth(player.transform.position);
            log.AppendLine($"spawnDepth={spawnDepth:F2} (负值=在门外)");
            Check(spawnDepth < -0.8f, "出生点在门外地台上");

            // ---- 2. 关门时走不进去 ----------------------------------------
            Vector3 before = player.transform.position;
            yield return Push(Vector2.up, 3.0f);          // 朝相机前方(店内)直推 3 秒
            float blockedDepth = door.InsideDepth(player.transform.position);
            float movedToDoor = Vector3.Distance(before, player.transform.position);
            log.AppendLine($"closedDoor: moved={movedToDoor:F2}m depth={blockedDepth:F2} state={door.Current}");
            Check(movedToDoor > 0.5f, "角色确实朝门走了");
            Check(blockedDepth < 0.05f, "门关着时被挡在门外，没有穿透");

            // ---- 3. 回到出生点，对着门点击 → 开门 + 门铃 -------------------
            player.Teleport(spawnPoint.position, spawnPoint.rotation);
            door.ForceClosed();
            // 用真实的跟随镜头取景，点门就是玩家实际会看到的那一帧
            if (followRig != null) { followRig.enabled = true; followRig.SnapToTarget(); }
            for (int i = 0; i < 24; i++) yield return null;

            var cam = Camera.main;
            Vector3 aim = door.doorCenter + Vector3.up * 1.2f;
            Physics.SyncTransforms();
            Vector3 sp = cam.WorldToScreenPoint(aim);
            bool clicked = door.TryInteractFromCamera(cam, sp);
            // 诊断：这条射线到底先撞上了什么
            var dbgRay = cam.ScreenPointToRay(sp);
            int boundaryMask = LayerMask.GetMask("Boundary");
            string dbg = "nothing";
            if (Physics.Raycast(dbgRay, out var dbgHit, 12f, ~boundaryMask, QueryTriggerInteraction.Ignore))
                dbg = $"{dbgHit.collider.name} (layer {dbgHit.collider.gameObject.layer}, d={dbgHit.distance:F2}m)";
            var targetNames = new System.Text.StringBuilder();
            foreach (var t in door.clickTargets) targetNames.Append(t == null ? "null" : t.name).Append(' ');
            log.AppendLine($"cam=({cam.transform.position.x:F2},{cam.transform.position.y:F2}) " +
                           $"aimScreen=({sp.x:F0},{sp.y:F0}) screen=({Screen.width},{Screen.height}) " +
                           $"hit={clicked} bell={door.BellCount}");
            log.AppendLine($"  dbgRay(mask={~boundaryMask}) hits {dbg}; clickTargets=[{targetNames}]");
            Check(clicked, "射线打到门并触发开门");
            Check(door.BellCount == 1, "门铃响了一次");

            // 重复点击同一扇正在开的门，不该重复触发
            bool again = door.Open();
            log.AppendLine($"open again while opening -> {again} bells={door.BellCount}");
            Check(!again && door.BellCount == 1, "开门过程中重复点击无副作用");

            // ---- 4. 等门开到位 --------------------------------------------
            float wait = 0f;
            while (door.OpenAmount < 0.999f && wait < 3f) { wait += Time.deltaTime; yield return null; }
            log.AppendLine($"door open: amount={door.OpenAmount:F2} state={door.Current} after={wait:F2}s");
            Check(door.OpenAmount > 0.99f, "门扇开到 92°");

            // ---- 5. 自己走进去 --------------------------------------------
            if (followRig != null) { followRig.enabled = true; followRig.SnapToTarget(); }
            for (int i = 0; i < 6; i++) yield return null;
            yield return Push(Vector2.up, 3.2f);
            float depthNow = door.InsideDepth(player.transform.position);
            log.AppendLine($"afterWalkIn: depth={depthNow:F2} hasEntered={door.HasEntered} state={door.Current}");
            Check(depthNow > 0.55f, "玩家已越过门槛进店");
            Check(door.HasEntered, "流程记录到已进店");

            // ---- 6. 门自动关闭 --------------------------------------------
            wait = 0f;
            while (door.Current != EntranceDoor.State.Closed && wait < 4f) { wait += Time.deltaTime; yield return null; }
            log.AppendLine($"door closed: state={door.Current} amount={door.OpenAmount:F2} after={wait:F2}s");
            Check(door.Current == EntranceDoor.State.Closed, "进店后门自动关闭");

            // ---- 7. 再次开关不出错 ----------------------------------------
            bool reopen = door.Open();
            wait = 0f;
            while (door.OpenAmount < 0.999f && wait < 3f) { wait += Time.deltaTime; yield return null; }
            bool openAgainOk = door.OpenAmount > 0.99f;
            door.ForceClosed();
            log.AppendLine($"reopen={reopen} amount={door.OpenAmount:F2} bells={door.BellCount}");
            Check(reopen && openAgainOk, "第二次开门正常");
            Check(door.BellCount == 2, "第二次开门门铃再响一次");

            Finish(null);
        }

        /// <summary>按住某个方向推 seconds 秒（固定 60fps 步长）。</summary>
        IEnumerator Push(Vector2 dir, float seconds)
        {
            int frames = Mathf.RoundToInt(seconds * 60f);
            for (int i = 0; i < frames; i++)
            {
                router.Virtual.SetMove(dir);
                yield return null;
            }
            router.Virtual.SetMove(Vector2.zero);
            for (int i = 0; i < 10; i++) yield return null;
        }

        void Finish(string fatal)
        {
            Time.captureDeltaTime = 0f;
            if (fatal != null) { log.AppendLine("FATAL " + fatal); failures++; }
            log.AppendLine($"failures={failures}");
            log.AppendLine(failures == 0 ? "ENTRY RESULT: PASS" : "ENTRY RESULT: FAIL");
            Debug.Log("[ENTRY]\n" + log);
            System.IO.File.WriteAllText("entry_flow.txt", log.ToString());
#if UNITY_EDITOR
            EditorApplication.Exit(failures == 0 ? 0 : 1);
#else
            Application.Quit(failures == 0 ? 0 : 1);
#endif
        }
    }
}

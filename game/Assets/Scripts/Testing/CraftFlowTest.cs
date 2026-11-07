using System.Collections;
using System.Text;
using CoffeeShop.Bar;
using CoffeeShop.Cameras;
using CoffeeShop.Controls;
using CoffeeShop.Drinks;
using CoffeeShop.Player;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CoffeeShop.Testing
{
    /// <summary>
    /// 07 票自动化验收：工位判定 → 进入近景 → 五原语做完整杯 → 偏差反馈 → 退出恢复 → 通道可走。
    /// 操作全部走 CraftPanel 的公开入口（指针回调与自动化用的是同一条路），
    /// 界面背后只发请求给 <see cref="CraftSession"/>，不直接改任何业务状态。
    /// </summary>
    public class CraftFlowTest : MonoBehaviour
    {
        public PlayerMotor player;
        public PlayerInputRouter router;
        public ThirdPersonCameraRig followRig;
        public BarStation station;

        readonly StringBuilder log = new StringBuilder();
        int failures;

        void Check(bool ok, string what)
        {
            if (!ok) { failures++; log.AppendLine("  FAIL " + what); }
            else log.AppendLine("  ok   " + what);
        }

        IEnumerator Start()
        {
            if (player == null || router == null || followRig == null || station == null)
            {
                Finish("MISSING-REFS");
                yield break;
            }
            var panel = station.panel;
            if (panel == null) { Finish("NO-PANEL"); yield break; }

            router.SetDevice(PlayerInputRouter.Device.VirtualJoystick);
            router.SetSuppressed(true);
            Time.captureDeltaTime = 1f / 60f;
            for (int i = 0; i < 20; i++) yield return null;

            float fovFollow = followRig.CurrentFov;
            float distFollow = followRig.CurrentDistance;
            log.AppendLine($"follow: fov={fovFollow:F1} dist={distFollow:F2} pitch={followRig.Pitch:F0}");

            // ---- 1. 工位范围 ------------------------------------------------
            Vector3 look = station.workPoint - station.center; look.y = 0f;
            player.Teleport(station.GroundPoint, Quaternion.LookRotation(look.sqrMagnitude > 1e-4f ? look.normalized : Vector3.forward));
            for (int i = 0; i < 5; i++) yield return null;

            Check(station.InZone(player.transform.position), "站在工位中心算在范围内");
            Check(!station.InZone(station.center + new Vector3(station.size.x * 0.5f + 0.7f, 0f, 0f)),
                  "沿吧台走开 0.7m 就不在工位范围内");
            Check(!station.InZone(station.center + new Vector3(0f, 0f, station.size.z * 0.5f + 0.40f)),
                  "越过通道边线（柜台里）不算在工位");
            Check(panel.HintVisible, "站到工位就出「开始制作」提示");
            Check(!station.Active, "还没按交互时界面不打开");

            // ---- 2. 按交互进入（走真实输入链路） ----------------------------
            router.Virtual.PressInteract();
            for (int i = 0; i < 3; i++) yield return null;

            Check(station.Active && panel.IsOpen, "按交互进入制作界面");
            Check(router.UiMode, "输入切到界面模式（光标可点、不再发世界交互）");
            Check(!player.enabled, "制作期间走路锁住（原地近景）");

            var craft = panel.Session;
            Check(craft != null, "界面拿到了 CraftSession");

            // ---- 3. 近景切入 ≤3s，且确实收紧了 -----------------------------
            float t = 0f;
            while (followRig.FocusProgress < 0.999f && t < 5f) { t += Time.deltaTime; yield return null; }
            Check(t <= CameraFocusState.MaxSeconds, $"近景切入 {t:F2}s（≤{CameraFocusState.MaxSeconds:F0}s，FR-MOV-05）");
            Check(followRig.CurrentFov < fovFollow - 5f, $"视野收窄 {fovFollow:F0}° → {followRig.CurrentFov:F0}°");
            Check(followRig.CurrentDistance < distFollow - 0.8f,
                  $"距离收紧 {distFollow:F2}m → {followRig.CurrentDistance:F2}m");
            Vector3 camPos = followRig.transform.position;
            Check(Mathf.Abs(camPos.z - station.center.z) < 0.9f,
                  $"近景机位仍在吧台内侧通道里（z 偏差 {Mathf.Abs(camPos.z - station.center.z):F2}m）");
            log.AppendLine($"  camera at ({camPos.x:F2},{camPos.y:F2},{camPos.z:F2}) work={station.workPoint}");

            // ---- 4. 五原语 -------------------------------------------------
            // 4.1 place：放杯（热点）
            Check(craft.Mode == CraftUiMode.Place, "第一步是放置原语 → 出热点按钮");
            Check(craft.Options.Length == 2, $"放置热点 {craft.Options.Length} 个");
            int before = craft.StepIndex;
            craft.Choose(0);        // 原语不符的请求：领域层拒绝，界面改不了状态
            Check(craft.StepIndex == before, "发错原语的请求不改变步骤（NFR-05：UI 只发请求）");

            panel.PickOption(1);    // 放到台面 = 放错
            Check(craft.Mode == CraftUiMode.Decide, "放错位置会停下来等玩家决定");
            Check(panel.LastFeedback == CraftFeedback.Deviation, "判偏立刻发出反馈事件");
            Check(panel.FlashAmount > 0.5f, "判偏有即时红闪（FR-EVAL-03，界面上没有任何文字评级）");
            panel.RetryStep();
            Check(craft.Mode == CraftUiMode.Place, "重做后回到这一步重新放");
            panel.PickOption(0);
            Check(craft.StepId == "grind", "放到萃取口下才对");

            // 4.2 hold：磨豆（按住，进度条）
            Check(craft.Mode == CraftUiMode.Hold, "磨豆是按住原语 → 出进度条");
            panel.PressDown();
            float holdT = 0f;
            // 进度要顶到 1 才松手：0.99 就松手 = 差 0.025 秒，同样算偏差（这正是判定要的东西）
            while (craft.Progress01 < 1f && holdT < 4f) { holdT += Time.deltaTime; yield return null; }
            Check(craft.Progress01 >= 1f, $"按住期间进度涨满（{holdT:F2}s）");
            panel.PressUp();
            Check(craft.StepId == "tamp", "按够了松手就过");

            // 4.3 click：压粉（要按到位，点一下算偏）
            Check(craft.Mode == CraftUiMode.Press, "压粉是点击原语");
            panel.PressDown();
            for (int i = 0; i < 2; i++) yield return null;
            panel.PressUp();
            Check(craft.Mode == CraftUiMode.Decide, "点一下没压到位 → 判偏");
            panel.RetryStep();
            panel.PressDown();
            for (int i = 0; i < 30; i++) yield return null;   // 0.5s > 0.4s 门槛
            panel.PressUp();
            Check(craft.StepId == "extract", "按够 0.4 秒才过");

            // 4.4 wait：萃取（不用操作，进度自己走）
            Check(craft.Mode == CraftUiMode.Wait, "萃取是等待原语，不需要按");
            float waitT = 0f;
            while (craft.StepId == "extract" && waitT < 9f) { waitT += Time.deltaTime; yield return null; }
            Check(craft.StepId == "move", $"等待 {waitT:F1}s 自动完成萃取");

            // 4.5 后段：取杯（点击）→ 加奶（按住）→ 出杯（点击）
            panel.TapAction(0.06f);
            Check(craft.StepId == "milk", "取杯点一下就走");

            panel.PressDown();
            float milkT = 0f;
            while (craft.Progress01 < 1f && milkT < 4f) { milkT += Time.deltaTime; yield return null; }
            panel.PressUp();
            Check(craft.StepId == "serve", "加奶按够 2 秒");

            panel.TapAction(0.06f);
            for (int i = 0; i < 4; i++) yield return null;

            Check(craft.Maker.Served, "最后一步做完自动出杯");
            var review = craft.LastReview;
            Check(review != null && review.Quality == DrinkQuality.Good,
                  $"两处偏差都重做了 → 整杯评价 {(review != null ? review.ToString() : "null")}");
            log.AppendLine($"  session: {craft.Describe()}");

            // ---- 5. 出杯后自动收起，镜头恢复 -------------------------------
            float closeT = 0f;
            while (station.Active && closeT < 3f) { closeT += Time.deltaTime; yield return null; }
            Check(!station.Active, $"出杯 {closeT:F1}s 后自动收起界面");
            yield return WaitFocus(0f, 2f);
            Check(followRig.FocusProgress < 0.001f, "镜头回到跟随构图");
            Check(Mathf.Abs(followRig.CurrentFov - fovFollow) < 1.5f,
                  $"视野恢复 {followRig.CurrentFov:F1}°（跟随 {fovFollow:F1}°）");
            Check(Mathf.Abs(followRig.CurrentDistance - distFollow) < 0.1f,
                  $"距离恢复 {followRig.CurrentDistance:F2}m");
            Check(player.enabled, "退出后走路恢复");

            // ---- 6. 再进一次，用"取消"退出 ---------------------------------
            router.Virtual.PressInteract();
            for (int i = 0; i < 3; i++) yield return null;
            Check(station.Active && panel.IsOpen, "再次按交互可以重开（配方是新的，从第一步开始）");
            Check(panel.Session != null && panel.Session.StepIndex == 0, "新一次制作从第一步开始");

            router.Virtual.PressCancel();
            for (int i = 0; i < 3; i++) yield return null;
            Check(!station.Active && !panel.IsOpen, "按取消直接退出");
            Check(!router.UiMode, "输入模式还原");
            yield return WaitFocus(0f, 1.5f);

            // ---- 7. 吧台内侧通道可以自由移动 -------------------------------
            Vector3 aisleStart = new Vector3(station.center.x + 0.6f, 0.06f, station.center.z);
            player.Teleport(aisleStart, Quaternion.LookRotation(Vector3.left));
            for (int i = 0; i < 5; i++) yield return null;

            Vector3 dirWorld = Vector3.left;
            Vector3 fwd = Vector3.ProjectOnPlane(followRig.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            router.Virtual.SetMove(new Vector2(Vector3.Dot(dirWorld, right), Vector3.Dot(dirWorld, fwd)));
            float walkT = 0f;
            Vector3 from = player.transform.position;
            while (walkT < 1.2f) { walkT += Time.deltaTime; yield return null; }
            router.Virtual.Clear();
            for (int i = 0; i < 3; i++) yield return null;

            Vector3 to = player.transform.position;
            float moved = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
            float endZ = to.z;
            log.AppendLine($"  aisle walk: moved={moved:F2}m from=({from.x:F2},{from.z:F2}) to=({to.x:F2},{to.z:F2})");
            Check(moved > 0.6f, $"在吧台内侧通道里走了 {moved:F2}m（不被柜台卡住）");
            Check(Mathf.Abs(endZ - station.center.z) < 0.75f, $"走完仍在通道里（z 偏差 {Mathf.Abs(endZ - station.center.z):F2}m）");

            Finish(null);
        }

        IEnumerator WaitFocus(float target, float timeout)
        {
            float t = 0f;
            while (Mathf.Abs(followRig.FocusProgress - target) > 0.001f && t < timeout)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        void Finish(string fatal)
        {
            Time.captureDeltaTime = 0f;
            if (fatal != null) { log.AppendLine("FATAL " + fatal); failures++; }
            log.AppendLine($"failures={failures}");
            log.AppendLine(failures == 0 ? "CRAFT RESULT: PASS" : "CRAFT RESULT: FAIL");
            Debug.Log("[CRAFT]\n" + log);
            System.IO.File.WriteAllText("craft_flow.txt", log.ToString());
#if UNITY_EDITOR
            EditorApplication.Exit(failures == 0 ? 0 : 1);
#else
            Application.Quit(failures == 0 ? 0 : 1);
#endif
        }
    }
}

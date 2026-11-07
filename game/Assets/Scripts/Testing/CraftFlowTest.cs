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
    /// 07 票自动化验收（返工版）：工位判定 → 进入近景 → **手柄直接操作**做完整杯 →
    /// 缺陷看得见 → 退出恢复 → 通道可走。
    ///
    /// 手势走的是 CraftView 的公开入口（和真实指针事件同一条路），
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
        CraftView view;

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
            view = panel.view;
            Check(craft != null, "界面拿到了 CraftSession");
            Check(view != null && view.Bound, "直接操作层已绑定（台面道具就位）");

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

            // ---- 4. 直接操作做完整杯 ---------------------------------------
            // 4.1 place：把杯子拖到台面（错）
            Check(craft.Mode == CraftUiMode.Place, "第一步是放置原语 → 拖杯子");
            Check(view.PowderLevel < 0.05f, $"开局粉碗是空的（粉面 {view.PowderLevel:P0}）");
            yield return CarryTo(1);
            Check(craft.Mode == CraftUiMode.Decide, "杯子拖到台面 = 放错，停下来等玩家决定");
            Check(panel.LastFeedback == CraftFeedback.Deviation, "判偏立刻发出反馈事件");

            panel.RetryStep();
            yield return null;
            Check(craft.Mode == CraftUiMode.Place, "重做后回到这一步重新放");

            // 4.2 place：拖到萃取口下（对）
            yield return CarryTo(0);
            Check(craft.StepId == "grind", "杯子拖到萃取口下才对");

            // 4.3 hold：磨豆 = 绕圈，不转就不磨
            Check(craft.Mode == CraftUiMode.Hold, "磨豆是按住原语 → 手势是绕圈");
            Vector2 center = view.ToScreen(view.BasketWorld);
            view.PointerDown(center + new Vector2(70f, 0f));
            for (int i = 0; i < 30; i++) yield return null;   // 按住不动
            Check(craft.Progress01 < 0.01f, $"按住不转，粉一点都不出（进度 {craft.Progress01:P0}）");
            Check(craft.StepId == "grind", "不转就一直停在这一步");

            // 转到粉面涨满就松手（这一步不会自己结束，松手才结算——这就是"够了没"的判断）
            float turnT = 0f;
            for (int i = 0; i < 400 && craft.Progress01 < 1f; i++)
            {
                float a = i * 0.22f;
                view.PointerMove(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 70f);
                turnT += Time.deltaTime;
                yield return null;
            }
            view.PointerUp();
            yield return null;
            Check(craft.StepId == "tamp", $"绕圈磨了 {turnT:F1}s 才过（配方要 2.5s）");
            Check(view.PowderLevel > 0.95f, $"粉面涨满（{view.PowderLevel:P0}，这就是「粉够了」）");

            // 4.4 click：压粉 = 下拖，压到底要停住
            Check(craft.Mode == CraftUiMode.Press, "压粉是点击原语 → 手势是往下拖");
            yield return Push(200f, 0f, 1);                  // 拖到底立刻松手 = 没停住
            Check(craft.Mode == CraftUiMode.Decide, "压到底不停住 → 判偏");

            panel.RetryStep();
            yield return null;
            yield return Push(200f, 6f, 32);                 // 手歪着压，但停够了
            Check(craft.StepId == "extract", "拖到底停住 0.5s 才过");
            Check(Mathf.Abs(view.TiltDeg) > 2f, $"压歪了粉饼就是斜的（{view.TiltDeg:F1}°，界面上没有任何提示）");

            // 4.5 wait：萃取
            Check(craft.Mode == CraftUiMode.Wait, "萃取是等待原语，不需要操作");
            float waitT = 0f;
            while (craft.StepId == "extract" && waitT < 9f) { waitT += Time.deltaTime; yield return null; }
            Check(craft.StepId == "move", $"等 {waitT:F1}s 自动流完");

            // 4.6 move：把杯子拖到托盘
            yield return CarryTo(2);
            Check(craft.StepId == "milk", "杯子拖到托盘就走");

            // 4.7 hold：打奶 = 奶缸拖到蒸汽位并保持
            Check(craft.Mode == CraftUiMode.Hold, "加奶是按住原语 → 奶缸保持");
            view.PointerDown(view.ToScreen(view.PitcherWorld));
            view.PointerMove(view.ToScreen(view.SpotWorld(4)));
            float milkT = 0f;
            while (craft.StepId == "milk" && milkT < 4f)
            {
                view.PointerMove(view.ToScreen(view.SpotWorld(4)));
                milkT += Time.deltaTime;
                yield return null;
            }
            view.PointerUp();
            yield return null;
            Check(craft.StepId == "serve", $"奶缸在蒸汽位保持 {milkT:F1}s 就走（配方 2.0s）");

            // 4.8 serve：拖到出杯位
            yield return CarryTo(3);
            for (int i = 0; i < 4; i++) yield return null;

            Check(craft.Maker.Served, "最后一步做完自动出杯");
            var review = craft.LastReview;
            Check(review != null && review.Quality == DrinkQuality.Good,
                  $"两次放错/压偏都重做了 → 整杯评价 {(review != null ? review.ToString() : "null")}");
            log.AppendLine($"  session: {craft.Describe()}");
            log.AppendLine($"  props: powder={view.PowderLevel:P0} tilt={view.TiltDeg:F1}° turnFrames={view.TurnFrames} pushFrames={view.PushFrames}");

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
            log.AppendLine($"  aisle walk: moved={moved:F2}m from=({from.x:F2},{from.z:F2}) to=({to.x:F2},{to.z:F2})");
            Check(moved > 0.6f, $"在吧台内侧通道里走了 {moved:F2}m（不被柜台卡住）");
            Check(Mathf.Abs(to.z - station.center.z) < 0.75f, $"走完仍在通道里（z 偏差 {Mathf.Abs(to.z - station.center.z):F2}m）");

            Finish(null);
        }

        // ------------------------------------------------------------ 手势

        IEnumerator CarryTo(int spot)
        {
            view.PointerDown(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            yield return null;
            view.PointerMove(view.ToScreen(view.SpotWorld(spot)));
            yield return null;
            view.PointerMove(view.ToScreen(view.SpotWorld(spot)));
            yield return null;
            view.PointerUp();
            yield return null;
        }

        IEnumerator Push(float pixels, float drift, int holdFrames)
        {
            Vector2 p = view.ToScreen(view.BasketWorld) + new Vector2(0f, 120f);
            view.PointerDown(p);
            for (int i = 0; i < 20; i++)
            {
                p += new Vector2(drift, -pixels / 20f);
                view.PointerMove(p);
                yield return null;
            }
            for (int i = 0; i < holdFrames; i++) yield return null;
            view.PointerUp();
            yield return null;
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

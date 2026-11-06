using CoffeeShop.Controls;
using CoffeeShop.Player;
using UnityEngine;
using UnityEngine.UI;

namespace CoffeeShop.Seating
{
    /// <summary>
    /// 玩家坐下 / 起身（04 票 / FR-SEAT-02）。
    ///
    /// 交互：走到空座 1.3m 内 → 座位上方浮出"坐下"提示 → 左键坐下 →
    ///       再按左键起身，人回到座位后方的站位，座位当场释放。
    ///
    /// 动画是占位实现（12 票换骨骼模型后由 Mecanim 接管）：
    /// 位置沿座位⇄站位插值、视觉体下沉到座面高度、上身略前倾。
    /// 状态机：Standing → Sitting → Seated → Standing。
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class SeatInteraction : MonoBehaviour
    {
        [Header("接线")]
        public SeatingManager seating;
        public PlayerMotor motor;
        public PlayerInputRouter router;
        [Tooltip("角色可见体（Player/Visual），坐下时整体下沉")]
        public Transform visual;
        [Tooltip("腿部视觉体，坐下时收起来；留空则只做整体下沉")]
        public Transform legs;

        [Header("交互")]
        [Tooltip("离座位多近才出提示、才能坐（米）")]
        public float reachRadius = 1.30f;
        [Tooltip("坐下动作耗时")]
        public float sitDuration = 0.50f;
        [Tooltip("起身动作耗时")]
        public float standDuration = 0.35f;
        [Tooltip("占用者标识，写进座位占用记录")]
        public string occupantId = "player";
        [Tooltip("坐下后允许起身的最小过渡量，防止一次点击既坐又起")]
        public float standGate = 0.75f;

        CharacterController controller;
        Transform prompt;
        Text promptText;

        SeatAnchor seated;
        SeatAnchor nearest;
        float blend;            // 0 = 站着，1 = 坐好
        float targetBlend;
        Vector3 fromPos, toPos;
        Quaternion fromRot, toRot;
        Vector3 visualBase;
        Vector3 legsPosBase;
        Vector3 legsScaleBase;
        float poseSeatHeight = 0.45f;
        bool poseActive;

        public bool IsSeated => seated != null;
        public SeatAnchor CurrentSeat => seated;
        public SeatAnchor NearestFree => nearest;
        /// <summary>0 = 站着，1 = 坐好。测试用。</summary>
        public float Blend => blend;
        public int SitCount { get; private set; }
        public int StandCount { get; private set; }
        public int RejectedSits { get; private set; }

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (visual != null) visualBase = visual.localPosition;
            CreatePrompt();
        }

        void Update()
        {
            var intent = router != null ? router.Intent : PlayerIntent.None;

            if (seated == null)
            {
                nearest = seating != null ? seating.NearestFree(transform.position, reachRadius) : null;
                ShowPrompt(nearest);
                if (nearest != null && intent.Interact) TrySit(nearest);
            }
            else
            {
                ShowPrompt(null);
                if (intent.Interact && blend >= standGate) TryStand();
            }

            Step(Time.deltaTime);
        }

        /// <summary>坐下。座位已被人占用时返回 false，并且不产生任何位移。</summary>
        public bool TrySit(SeatAnchor seat)
        {
            if (seat == null || seated != null) return false;
            if (seating == null || !seating.Registry.TryOccupy(seat.Id, occupantId))
            {
                RejectedSits++;
                return false;
            }

            seated = seat;
            poseSeatHeight = seat.SeatHeight;
            poseActive = true;
            fromPos = transform.position;
            fromRot = transform.rotation;
            toPos = new Vector3(seat.SeatPoint.x, fromPos.y, seat.SeatPoint.z);
            toRot = seat.Facing;
            targetBlend = 1f;

            if (controller != null) controller.enabled = false;
            if (motor != null) motor.enabled = false;
            SitCount++;
            return true;
        }

        /// <summary>起身。没坐着的时候返回 false。</summary>
        public bool TryStand()
        {
            if (seated == null) return false;

            fromPos = transform.position;
            fromRot = transform.rotation;
            toPos = new Vector3(seated.StandPoint.x, fromPos.y, seated.StandPoint.z);
            toRot = seated.Facing;
            targetBlend = 0f;
            StandCount++;
            return true;
        }

        /// <summary>推进过渡动画。抽成公开方法，自动化测试可以用固定步长驱动。</summary>
        public void Step(float dt)
        {
            if (Mathf.Abs(blend - targetBlend) < 1e-4f)
            {
                if (targetBlend <= 0f && seated != null) FinishStand();
                return;
            }

            float dur = targetBlend > 0f ? sitDuration : standDuration;
            blend = Mathf.MoveTowards(blend, targetBlend, dt / Mathf.Max(0.01f, dur));
            float t = blend * blend * (3f - 2f * blend);   // smoothstep

            transform.SetPositionAndRotation(Vector3.Lerp(fromPos, toPos, t),
                                             Quaternion.Slerp(fromRot, toRot, t));
            ApplyPose(blend, poseSeatHeight);

            if (blend <= 0f && targetBlend <= 0f && seated != null) FinishStand();
        }

        void FinishStand()
        {
            var seat = seated;
            transform.SetPositionAndRotation(new Vector3(toPos.x, transform.position.y, toPos.z), toRot);
            seated = null;
            poseActive = false;
            ApplyPose(0f, poseSeatHeight);

            if (seat != null && seating != null) seating.Registry.Release(seat.Id, occupantId);
            if (controller != null) controller.enabled = true;
            if (motor != null) motor.enabled = true;
        }

        /// <summary>
        /// 坐下姿态占位：整体下沉到"头顶 ≈ 座面 + 0.80m"，同时把腿收起来、上身略前倾。
        /// 骨骼模型接进来后整段替换成 Animator 播放。
        /// </summary>
        void ApplyPose(float t, float seatHeight)
        {
            if (visual == null) return;
            float drop = Mathf.Max(0.10f, 0.80f - seatHeight) * t;
            visual.localPosition = visualBase + new Vector3(0f, -drop, 0f);
            visual.localRotation = Quaternion.Euler(-7f * t, 0f, 0f);

            if (legs != null)
            {
                var ls = legsScaleBase;
                ls.y = Mathf.Lerp(legsScaleBase.y, legsScaleBase.y * 0.42f, t);   // 腿收短
                legs.localScale = ls;
                var lp = legsPosBase;
                lp.y = Mathf.Lerp(legsPosBase.y, legsPosBase.y * 0.52f, t);      // 腿往上收进身体
                legs.localPosition = lp;
            }
        }

        // ---------------- 提示 UI（世界空间，能进 RenderTexture 截图） ----------------

        void CreatePrompt()
        {
            try
            {
                var go = new GameObject("SeatPrompt");
                go.transform.SetParent(transform, false);
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(300f, 76f);
                rt.localScale = Vector3.one * 0.004f;

                var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
                labelGO.transform.SetParent(go.transform, false);
                var lrt = (RectTransform)labelGO.transform;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero;
                lrt.offsetMax = Vector2.zero;

                promptText = labelGO.GetComponent<Text>();
                promptText.font = LoadPromptFont();
                promptText.text = "坐下";
                promptText.fontSize = 44;
                promptText.alignment = TextAnchor.MiddleCenter;
                promptText.horizontalOverflow = HorizontalWrapMode.Overflow;
                promptText.verticalOverflow = VerticalWrapMode.Overflow;
                promptText.color = new Color(1f, 1f, 1f, 1f);

                var outline = labelGO.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
                outline.effectDistance = new Vector2(2f, -2f);

                prompt = go.transform;
                go.SetActive(false);
                Debug.Log($"[seat] prompt UI ready font={(promptText.font != null ? promptText.font.name : "NULL")} " +
                          $"scale={rt.localScale.x} size={rt.sizeDelta} canvasRenderer={labelGO.GetComponent<CanvasRenderer>() != null}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[seat] 提示 UI 创建失败（不影响坐下逻辑）: " + e.Message);
                prompt = null;
            }
        }

        /// <summary>
        /// 取一个能显示中文的字体。Unity 内置的 LegacyRuntime.ttf 只有拉丁字形，
        /// 中文会整段空白；这里优先用系统里的中文字体，取不到再退回内置字体。
        /// </summary>
        static Font LoadPromptFont()
        {
            string[] prefer =
            {
                "Microsoft YaHei", "微软雅黑", "PingFang SC", "Hiragino Sans GB",
                "Noto Sans CJK SC", "Source Han Sans SC", "SimHei", "Arial Unicode MS"
            };
            var f = Font.CreateDynamicFontFromOSFont(prefer, 44);
            if (f != null) return f;
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        void ShowPrompt(SeatAnchor seat)
        {
            if (prompt == null) return;
            if (seat == null)
            {
                if (prompt.gameObject.activeSelf) prompt.gameObject.SetActive(false);
                return;
            }

            if (!prompt.gameObject.activeSelf) prompt.gameObject.SetActive(true);
            // 摆在座位上方、高过角色头顶：站在座位后方时不会被自己的脑袋挡住
            prompt.position = seat.SeatPoint + Vector3.up * (seat.SeatHeight + 1.15f);

            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 away = prompt.position - cam.transform.position;
                if (away.sqrMagnitude > 1e-4f)
                    prompt.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            }
        }

        public void Bind(SeatingManager manager, PlayerMotor playerMotor, PlayerInputRouter inputRouter, Transform visualRoot)
        {
            seating = manager;
            motor = playerMotor;
            router = inputRouter;
            visual = visualRoot;
            if (visual != null)
            {
                visualBase = visual.localPosition;
                if (legs == null) legs = visual.Find("PlayerVisual_Legs");
            }
            if (legs != null)
            {
                legsPosBase = legs.localPosition;
                legsScaleBase = legs.localScale;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, reachRadius);
        }
    }
}

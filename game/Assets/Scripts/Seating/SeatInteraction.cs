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
        [Tooltip("角色可见体（Player/Visual）。坐下时按部件重摆成坐姿，不做整体下沉")]
        public Transform visual;

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
        float poseSeatHeight = 0.45f;

        /// <summary>一个视觉部件的"站姿"快照；坐姿由站位插值过去。</summary>
        struct PartPose
        {
            public Transform t;
            public Vector3 pos, euler, scale;
        }

        // 0 大腿 / 1 小腿 / 2 躯干 / 3 头 / 4 朝向标
        static readonly string[] PartNames =
        {
            "PlayerVisual_Legs", "PlayerVisual_Shin", "PlayerVisual_Body",
            "PlayerVisual_Head", "PlayerVisual_Facing"
        };

        PartPose[] parts;

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
            if (parts == null) CacheParts();
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
            ApplyPose(0f, poseSeatHeight);

            if (seat != null && seating != null) seating.Registry.Release(seat.Id, occupantId);
            if (controller != null) controller.enabled = true;
            if (motor != null) motor.enabled = true;
        }

        /// <summary>
        /// 坐下姿态占位：**按部件重摆**，不是整体往下沉。
        ///
        /// 旧版只是把整个视觉体下沉 14cm、腿压短，结果身体比座面还低、比靠背还宽，
        /// 整把凳子被吞进身体里，看起来就是穿模。现在：
        ///   大腿绕 X 转 90° 躺平向前伸、小腿从膝盖垂到地面、躯干底面正落在座面上、
        ///   躯干进深从 0.38 收到 0.30 —— 比靠背（本地 z ≈ -0.20）窄，靠背就能露出来。
        /// 骨骼模型接进来后整段替换成 Animator 播放。
        /// </summary>
        void ApplyPose(float t, float seatHeight)
        {
            if (parts == null || parts.Length == 0) return;

            if (t <= 1e-4f)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].t == null) continue;
                    parts[i].t.localPosition = parts[i].pos;
                    parts[i].t.localEulerAngles = parts[i].euler;
                    parts[i].t.localScale = parts[i].scale;
                }
                return;
            }

            float hs = Mathf.Clamp(seatHeight, 0.30f, 0.80f);
            float kneeY = hs + 0.17f;                       // 大腿中线：略高于座面，正好搭在坐板上
            float shinLen = Mathf.Max(0.28f, kneeY);        // 小腿长度 = 膝盖到地面的高度

            // 大腿：躺平，从臀下向前伸 0.28m（膝盖停在吧台前沿之外，不钻进柜台）
            SetPart(0, t, new Vector3(0f, kneeY, 0.14f), new Vector3(90f, 0f, 0f),
                    new Vector3(0.30f, 0.14f, 0.30f));
            // 小腿：垂直悬在膝盖下方
            SetPart(1, t, new Vector3(0f, shinLen * 0.5f, 0.22f), Vector3.zero,
                    new Vector3(0.20f, shinLen * 0.5f, 0.20f));
            // 躯干：底面 = 座面顶，进深收窄给靠背让位
            SetPart(2, t, new Vector3(0f, hs + 0.30f, 0.03f), Vector3.zero,
                    new Vector3(0.36f, 0.28f, 0.28f));
            // 头：坐在躯干顶端
            SetPart(3, t, new Vector3(0f, hs + 0.72f, 0.03f), Vector3.zero,
                    new Vector3(0.26f, 0.28f, 0.26f));
            // 朝向标：落到胸口前方
            SetPart(4, t, new Vector3(0f, hs + 0.42f, 0.175f), Vector3.zero,
                    new Vector3(0.13f, 0.15f, 0.03f));
        }

        void SetPart(int i, float t, Vector3 posePos, Vector3 poseEuler, Vector3 poseScale)
        {
            if (i < 0 || i >= parts.Length) return;
            var p = parts[i];
            if (p.t == null) return;
            p.t.localPosition = Vector3.Lerp(p.pos, posePos, t);
            p.t.localEulerAngles = Vector3.Lerp(p.euler, poseEuler, t);
            p.t.localScale = Vector3.Lerp(p.scale, poseScale, t);
        }

        /// <summary>抓一次站姿快照。只在第一次绑定时抓，避免坐着重绑把坐姿记成站姿。</summary>
        void CacheParts()
        {
            if (visual == null) return;
            parts = new PartPose[PartNames.Length];
            for (int i = 0; i < PartNames.Length; i++)
            {
                var t = visual.Find(PartNames[i]);
                parts[i] = new PartPose
                {
                    t = t,
                    pos = t != null ? t.localPosition : Vector3.zero,
                    euler = t != null ? t.localEulerAngles : Vector3.zero,
                    scale = t != null ? t.localScale : Vector3.one
                };
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
            if (parts == null) CacheParts();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, reachRadius);
        }
    }
}

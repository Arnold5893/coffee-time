using System;
using CoffeeShop.Audio;
using CoffeeShop.Drinks;
using CoffeeShop.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoffeeShop.Bar
{
    /// <summary>
    /// 吧台制作界面（07 票 / FR-MAKE-03、NFR-05）。
    ///
    /// 五种原语对应五种控件，全部只是**发请求**给 <see cref="CraftSession"/>：
    ///   click  → 圆形主按钮，按一下（有按住时长要求的步骤要按到位）
    ///   hold   → 同一个主按钮，按住不放，上方进度条跟着涨
    ///   select → 一排选项按钮
    ///   place  → 一排热点按钮（放到哪）
    ///   wait   → 不需要操作，进度条自己走
    ///
    /// 界面**不显示任何文字评级**（FR-EVAL-03）：做偏了一步只有红闪 + 一声闷响，
    /// 然后出现「重做 / 继续」两个按钮——是操作选择，不是打分。
    /// 评分（DrinkReview）由领域层自己算，这层连读都不读它的档位。
    /// </summary>
    [DefaultExecutionOrder(75)]
    public class CraftPanel : MonoBehaviour
    {
        [Header("接线")]
        public Camera uiCamera;
        public AudioSource sfx;

        [Header("观感")]
        [Tooltip("偏差红闪的衰减时长（秒）")]
        public float flashSeconds = 0.45f;
        [Tooltip("出杯后自动收起界面的延时（秒）")]
        public float closeDelay = 1.10f;

        const float progressBarWidth = 620f;

        Canvas canvas;
        RectTransform craftRoot, hintRoot, optionsRow, decideRow, actionRoot;
        Text stepText, actHintText, hintText, actionLabel, doneText;
        Image progressBg, progressFill, actionImg, flash;
        Text skipLabel, serveLabel;

        CraftSession session;
        string optionsForStep = "";
        float flashAmount;
        float doneTimer;
        AudioClip thud, ding;

        public CraftSession Session => session;
        public bool IsOpen => session != null;
        /// <summary>界面要求关掉（出杯后延时到 / 点了退出）。由 BarStation 轮询。</summary>
        public bool CloseRequested { get; private set; }
        public float FlashAmount => flashAmount;
        public CraftFeedback? LastFeedback { get; private set; }
        public int PressDownCount { get; private set; }
        public int PressUpCount { get; private set; }
        public int OptionClicks { get; private set; }
        public int ExitClicks { get; private set; }
        public bool HintVisible => hintRoot != null && hintRoot.gameObject.activeSelf;

        /// <summary>验收/排查用：进度条当前的尺寸与颜色。</summary>
        public string ProgressDebug
        {
            get
            {
                if (progressFill == null) return "fill=null";
                var rt = progressFill.rectTransform;
                return $"fillAmount={progressFill.fillAmount:F2} fillRect={rt.rect} color={progressFill.color} " +
                       $"active={progressFill.gameObject.activeInHierarchy} p={session?.Progress01:F2}";
            }
        }

        void Awake()
        {
            if (uiCamera == null) uiCamera = Camera.main;
            if (sfx == null)
            {
                sfx = gameObject.AddComponent<AudioSource>();
                sfx.playOnAwake = false;
                sfx.spatialBlend = 0f;
            }
            thud = ProceduralSfx.CraftThud();
            ding = ProceduralSfx.ServeDing();
            Build();
            ShowHint(false, "");
        }

        // ------------------------------------------------------------ 对外

        public void Open(CraftSession craft)
        {
            if (craft == null) return;
            if (session != null) session.Feedback -= OnFeedback;
            session = craft;
            session.Feedback += OnFeedback;

            optionsForStep = "";
            doneTimer = 0f;
            flashAmount = 0f;
            ApplyFlash();
            CloseRequested = false;
            craftRoot.gameObject.SetActive(true);
            hintRoot.gameObject.SetActive(false);
            Refresh();
        }

        public void Close()
        {
            if (session != null) session.Feedback -= OnFeedback;
            session = null;
            CloseRequested = false;
            flashAmount = 0f;
            ApplyFlash();
            if (craftRoot != null) craftRoot.gameObject.SetActive(false);
            ShowHint(false, "");
        }

        /// <summary>没进界面时，站在工位上给一句"按左键开始制作"。</summary>
        public void ShowHint(bool show, string text)
        {
            if (hintRoot == null) return;
            if (hintRoot.gameObject.activeSelf != show) hintRoot.gameObject.SetActive(show);
            if (show) UiKit.SetText(hintText, text);
        }

        /// <summary>指针按下主按钮（鼠标左键 / 手指按住）。</summary>
        public void PressDown()
        {
            if (session == null) return;
            PressDownCount++;
            session.BeginPress();
        }

        /// <summary>指针抬起：hold 结算按够没有，click 结算按下时长。</summary>
        public void PressUp()
        {
            if (session == null) return;
            PressUpCount++;
            session.EndPress();
        }

        /// <summary>自动化用：按下 → 按住若干秒 → 抬起。</summary>
        public void TapAction(float seconds = 0.06f)
        {
            if (session == null) return;
            PressDown();
            float step = 0.02f;
            for (float t = 0f; t < seconds - 1e-4f; t += step) session.Tick(Mathf.Min(step, seconds - t));
            PressUp();
        }

        public void PickOption(int index)
        {
            if (session == null) return;
            OptionClicks++;
            if (session.Mode == CraftUiMode.Place) session.PutAt(index);
            else session.Choose(index);
        }

        public void RetryStep()
        {
            if (session == null) return;
            session.Retry();
        }

        public void ContinueStep()
        {
            if (session == null) return;
            session.Continue();
        }

        public void SkipStep()
        {
            if (session == null) return;
            session.Skip();
        }

        public void ServeNow()
        {
            if (session == null) return;
            session.Serve();
        }

        public void RequestExit()
        {
            ExitClicks++;
            CloseRequested = true;
        }

        // ------------------------------------------------------------ 帧驱动

        void Update()
        {
            if (flashAmount > 0f)
            {
                flashAmount = Mathf.Max(0f, flashAmount - Time.deltaTime / Mathf.Max(0.01f, flashSeconds));
                ApplyFlash();
            }

            if (session == null) return;

            session.Tick(Time.deltaTime);

            // 配方最后一步就是"出杯"，做完自动落杯；质量由领域层算，界面不显示
            if (session.Mode == CraftUiMode.Done && !session.Maker.Served) session.Serve();

            if (session.Mode == CraftUiMode.Done)
            {
                doneTimer += Time.deltaTime;
                if (doneTimer >= closeDelay) CloseRequested = true;
            }

            Refresh();
        }

        void OnFeedback(CraftFeedback f)
        {
            LastFeedback = f;
            switch (f)
            {
                case CraftFeedback.Deviation:
                    flashAmount = 1f;
                    ApplyFlash();
                    Play(thud);
                    break;
                case CraftFeedback.Served:
                    Play(ding);
                    break;
            }
        }

        void Play(AudioClip clip)
        {
            if (sfx != null && clip != null) sfx.PlayOneShot(clip);
        }

        // ------------------------------------------------------------ 刷新

        void Refresh()
        {
            if (session == null || craftRoot == null) return;

            var mode = session.Mode;
            UiKit.SetText(stepText, session.StepName);
            UiKit.SetText(actHintText, HintFor(mode));

            bool actionMode = mode == CraftUiMode.Press || mode == CraftUiMode.Hold;
            SetActive(actionRoot, actionMode);
            SetActive(optionsRow, mode == CraftUiMode.Choose || mode == CraftUiMode.Place);
            SetActive(decideRow, mode == CraftUiMode.Decide);
            SetActive(doneText, mode == CraftUiMode.Done);
            SetActive(progressBg.gameObject, mode == CraftUiMode.Hold || mode == CraftUiMode.Wait);

            if (actionMode)
            {
                UiKit.SetText(actionLabel, mode == CraftUiMode.Hold ? "按住" : "点击");
                // 按住时按钮压深一点，给"正在磨"的手感
                float k = session.IsPressing ? 0.86f : 1f;
                actionImg.color = new Color(0.96f * k, 0.90f * k, 0.78f * k, 0.95f);
            }

            if (session.Mode == CraftUiMode.Choose || session.Mode == CraftUiMode.Place)
                BuildOptions(session.StepId + "|" + mode);

            if (progressBg != null && progressFill != null)
            {
                // 宽度用 sizeDelta 给死（高度由锚点撑满），不走 anchorMax——
                // 后者在运行时改容易和 offset 打架，进度条会缩成一条线。
                progressFill.fillAmount = Mathf.Clamp01(session.Progress01);
            }

            // 没做完就能提前出杯（FR-MAKE-01）；已经走到最后一步就别重复点
            SetActive(skipLabel.transform.parent.gameObject, mode != CraftUiMode.Done);
            SetActive(serveLabel.transform.parent.gameObject, mode != CraftUiMode.Done);
        }

        static string HintFor(CraftUiMode mode)
        {
            switch (mode)
            {
                case CraftUiMode.Hold: return "按住不放，按到进度走满";
                case CraftUiMode.Press: return "点一下";
                case CraftUiMode.Choose: return "选一个";
                case CraftUiMode.Place: return "放到哪里";
                case CraftUiMode.Wait: return "等它自己完成";
                case CraftUiMode.Decide: return "重做这一步，还是带着继续？";
                case CraftUiMode.Done: return "这杯好了";
                default: return "";
            }
        }

        static void SetActive(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        static void SetActive(Component c, bool on)
        {
            if (c != null) SetActive(c.gameObject, on);
        }

        void BuildOptions(string key)
        {
            if (optionsForStep == key) return;
            optionsForStep = key;

            for (int i = optionsRow.childCount - 1; i >= 0; i--)
                Destroy(optionsRow.GetChild(i).gameObject);

            var options = session.Options;
            const float width = 176f, height = 68f, gap = 16f;
            float total = options.Length * width + Mathf.Max(0, options.Length - 1) * gap;
            float x = -total * 0.5f + width * 0.5f;

            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var btn = UiKit.TextButton("Option" + i, optionsRow, options[i], 26,
                                           new Vector2(width, height), new Color(0.22f, 0.18f, 0.15f, 0.92f), Color.white);
                UiKit.Place(btn.image.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                            new Vector2(x, 0f), new Vector2(width, height));
                btn.onClick.AddListener(() => PickOption(index));
                x += width + gap;
            }
        }

        // ------------------------------------------------------------ 搭建

        void Build()
        {
            var canvasGO = new GameObject("CraftCanvas", typeof(RectTransform), typeof(Canvas),
                                          typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = uiCamera;
            canvas.planeDistance = 0.35f;
            canvas.sortingOrder = 20;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 800f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var root = (RectTransform)canvasGO.transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            // ---- 未进入时的提示 ----
            hintRoot = UiKit.Node("EnterHint", root);
            UiKit.Place(hintRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(560f, 58f));
            var hintBg = hintRoot.gameObject.AddComponent<Image>();
            hintBg.color = new Color(0.08f, 0.07f, 0.06f, 0.72f);
            hintBg.raycastTarget = false;
            hintText = UiKit.Label("EnterHintText", hintRoot, 26, TextAnchor.MiddleCenter, Color.white);
            var hrt = hintText.rectTransform;
            hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
            hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;
            hintRoot.gameObject.SetActive(false);

            // ---- 制作界面 ----
            craftRoot = UiKit.Node("CraftRoot", root);
            craftRoot.anchorMin = Vector2.zero;
            craftRoot.anchorMax = Vector2.one;
            craftRoot.offsetMin = Vector2.zero;
            craftRoot.offsetMax = Vector2.zero;

            // 步骤名放屏幕上方：底部中间留给"人 + 台面"，别让字压在角色身上
            stepText = UiKit.Label("StepName", craftRoot, 34, TextAnchor.MiddleCenter, Color.white);
            UiKit.Place(stepText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -62f), new Vector2(960f, 50f));

            actHintText = UiKit.Label("StepHint", craftRoot, 22, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.78f, 0.92f));
            UiKit.Place(actHintText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -104f), new Vector2(960f, 34f));

            // 进度条
            progressBg = UiKit.Box("ProgressBg", craftRoot, new Color(0.06f, 0.05f, 0.04f, 0.80f));
            UiKit.Place(progressBg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 226f), new Vector2(progressBarWidth, 22f));

            progressFill = UiKit.Box("ProgressFill", progressBg.transform, new Color(1f, 0.78f, 0.28f, 1f));
            progressFill.sprite = UiKit.Solid();
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillOrigin = 0;
            progressFill.fillAmount = 0f;
            progressFill.raycastTarget = false;
            var pf = progressFill.rectTransform;
            pf.anchorMin = Vector2.zero;
            pf.anchorMax = Vector2.one;
            pf.offsetMin = Vector2.zero;
            pf.offsetMax = Vector2.zero;

            // 主按钮（圆形）
            var circle = UiKit.Box("ActionButton", craftRoot, new Color(0.96f, 0.90f, 0.78f, 0.95f));
            actionImg = circle;
            circle.sprite = UiKit.Circle();
            actionRoot = circle.rectTransform;
            UiKit.Place(actionRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 116f), new Vector2(168f, 168f));
            var press = circle.gameObject.AddComponent<PressTarget>();
            press.onDown = PressDown;
            press.onUp = PressUp;

            actionLabel = UiKit.Label("ActionLabel", actionRoot, 30, TextAnchor.MiddleCenter, new Color(0.24f, 0.16f, 0.10f));
            var alrt = actionLabel.rectTransform;
            alrt.anchorMin = Vector2.zero; alrt.anchorMax = Vector2.one;
            alrt.offsetMin = Vector2.zero; alrt.offsetMax = Vector2.zero;

            // 选项 / 热点
            optionsRow = UiKit.Node("Options", craftRoot);
            UiKit.Place(optionsRow, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(960f, 80f));
            optionsRow.gameObject.SetActive(false);

            // 判偏后的两个选择
            decideRow = UiKit.Node("Decide", craftRoot);
            UiKit.Place(decideRow, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(520f, 76f));
            var retry = UiKit.TextButton("Retry", decideRow, "重做", 28, new Vector2(210f, 68f),
                                         new Color(0.42f, 0.24f, 0.14f, 0.92f), Color.white);
            UiKit.Place(retry.image.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-120f, 0f), new Vector2(210f, 68f));
            retry.onClick.AddListener(RetryStep);

            var cont = UiKit.TextButton("Continue", decideRow, "继续", 28, new Vector2(210f, 68f),
                                        new Color(0.20f, 0.30f, 0.26f, 0.92f), Color.white);
            UiKit.Place(cont.image.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(120f, 0f), new Vector2(210f, 68f));
            cont.onClick.AddListener(ContinueStep);
            decideRow.gameObject.SetActive(false);

            doneText = UiKit.Label("Done", craftRoot, 40, TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.85f));
            UiKit.Place(doneText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(600f, 60f));
            doneText.gameObject.SetActive(false);

            // 跳过 / 出杯（FR-MAKE-01）
            var skip = UiKit.TextButton("Skip", craftRoot, "跳过", 22, new Vector2(128f, 44f),
                                        new Color(0.20f, 0.18f, 0.16f, 0.90f), new Color(1f, 1f, 1f, 0.92f));
            UiKit.Place(skip.image.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-310f, 42f), new Vector2(128f, 44f));
            skip.onClick.AddListener(SkipStep);
            skipLabel = skip.GetComponentInChildren<Text>();

            var serve = UiKit.TextButton("Serve", craftRoot, "出杯", 22, new Vector2(128f, 44f),
                                         new Color(0.20f, 0.18f, 0.16f, 0.90f), new Color(1f, 1f, 1f, 0.92f));
            UiKit.Place(serve.image.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(310f, 42f), new Vector2(128f, 44f));
            serve.onClick.AddListener(ServeNow);
            serveLabel = serve.GetComponentInChildren<Text>();

            var exit = UiKit.TextButton("Exit", craftRoot, "退出", 22, new Vector2(108f, 48f),
                                        new Color(0.20f, 0.18f, 0.16f, 0.90f), Color.white);
            UiKit.Place(exit.image.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -24f), new Vector2(108f, 48f));
            exit.onClick.AddListener(RequestExit);

            // ---- 偏差红闪（不吃点击） ----
            flash = UiKit.Box("Flash", root, new Color(0.85f, 0.10f, 0.08f, 0f));
            var frt = flash.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            flash.raycastTarget = false;

            craftRoot.gameObject.SetActive(false);
        }

        void ApplyFlash()
        {
            if (flash == null) return;
            flash.color = new Color(0.85f, 0.10f, 0.08f, flashAmount * 0.42f);
            flash.gameObject.SetActive(flashAmount > 0.001f);
        }

        /// <summary>主按钮的按下/抬起：用指针事件而不是全局输入，鼠标与触屏走同一条路。</summary>
        class PressTarget : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            public Action onDown;
            public Action onUp;
            public void OnPointerDown(PointerEventData eventData) => onDown?.Invoke();
            public void OnPointerUp(PointerEventData eventData) => onUp?.Invoke();
        }
    }
}

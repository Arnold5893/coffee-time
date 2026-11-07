using CoffeeShop.Drinks;
using UnityEngine;

namespace CoffeeShop.Bar
{
    /// <summary>
    /// 吧台直接操作层（07 票返工 / 方案 A「手上有东西」）。
    ///
    /// 原则：**手势在屏幕空间识别，后果落在台面上看得见的道具上**。
    ///   · 磨豆   = 按住绕圈 → 转得动才磨，粉面跟着涨（不转就不磨）
    ///   · 压粉   = 按住往下拖 → 压头跟着沉；拖歪了粉饼就是斜的；压到底要停住
    ///   · 取杯   = 把杯子拖到萃取口（拖到台面 = 错）
    ///   · 移动/出杯 = 把杯子拖到托盘 / 出杯位
    ///   · 打奶   = 把奶缸拖到蒸汽位并保持
    ///   · 选料   = 抓对应的小瓶
    ///
    /// 这一层只发请求给 <see cref="CraftSession"/>，**不改任何业务状态**（NFR-05）。
    /// 偏差不靠 UI 提示：粉面低一截、粉饼斜了，就是道具本身的样子（FR-EVAL-03）。
    /// </summary>
    public class CraftView : MonoBehaviour
    {
        public enum Hand
        {
            None = 0,
            Turn = 1,      // 绕圈（磨豆）
            Push = 2,      // 下拖（压粉）
            Carry = 3,     // 拖着走（杯子 / 奶缸）
            Hold = 4,      // 保持（打奶）
            Pick = 5,      // 抓小瓶
            Stroke = 6,    // 横扫（淋糖浆）
        }

        [Header("接线")]
        public Camera cam;
        [Tooltip("台面工作区中心（世界坐标），由构建脚本按后柜包围盒推出来")]
        public Vector3 anchor;

        [Header("手感")]
        [Tooltip("手柄转一圈磨多少秒")]
        public float secondsPerTurn = 0.62f;
        [Tooltip("下压到底需要的屏幕像素")]
        public float pushPixels = 170f;
        [Tooltip("拖到多近算放进热点（米）")]
        public float hotspotRadius = 0.12f;
        [Tooltip("打奶时奶缸离蒸汽位多近算在位（米）")]
        public float steamRadius = 0.13f;

        // ---------------------------------------------------------------- 状态

        CraftSession s;
        Transform row, pad, basket, powder, tamperHead, cup, cupCoffee, pitcher, milkSurface;
        Transform[] rings = new Transform[5];
        readonly Transform[] bottles = new Transform[3];

        Vector3 rowDir;
        Vector3 rowFwd;                 // 从主排指向柜台里侧（Build 里算一次，释放落点也要用）
        Vector3 cupHome, pitcherHome, tamperHome;
        Vector3[] spots = new Vector3[5];   // 0 萃取口 / 1 台面 / 2 托盘 / 3 出杯位 / 4 蒸汽位

        Hand hand;
        Vector2 downPos, lastPos;
        Vector2 turnPrev;
        float turnRadians;
        float turnSinceTick;
        float pushDepth;
        float tiltPx;
        float powderLevel;
        float powderTamped;              // 粉饼被压实到多少（压过之后不会弹回来）
        bool heldInPlace;
        bool carrying;
        Vector3 carryTarget;

        public int TurnFrames { get; private set; }
        public int PushFrames { get; private set; }
        public int CarryCount { get; private set; }

        public Hand CurrentHand => hand;
        public float PushDepth => pushDepth;
        public float TiltDeg => TiltFromPixels(tiltPx);
        public float TurnRadians => turnRadians;
        public float PowderLevel => powderLevel;
        public bool Bound => s != null;

        // ---------------------------------------------------------------- 生命周期

        public void Bind(CraftSession session, Camera camera)
        {
            s = session;
            if (camera != null) cam = camera;
            Build();
            ResetProps();
            row.gameObject.SetActive(true);
        }

        public void Clear()
        {
            s = null;
            hand = Hand.None;
            pushDepth = 0f;
            tiltPx = 0f;
            turnRadians = 0f;
            powderLevel = 0f;
            powderTamped = 0f;
            carrying = false;
            heldInPlace = false;
            if (row != null) row.gameObject.SetActive(false);
        }

        void OnDisable() => Clear();

        // ---------------------------------------------------------------- 指针

        public void PointerDown(Vector2 screen)
        {
            if (s == null) return;
            downPos = lastPos = turnPrev = screen;

            switch (s.Mode)
            {
                case CraftUiMode.Hold:
                    if (s.StepId == "milk") { hand = Hand.Hold; heldInPlace = false; s.BeginPress(); }
                    else { hand = Hand.Turn; turnRadians = 0f; turnSinceTick = 0f; s.BeginPress(); }
                    break;

                case CraftUiMode.Press:
                    if (s.StepId == "tamp") { hand = Hand.Push; pushDepth = 0f; tiltPx = 0f; s.BeginPress(); }
                    else if (s.StepId == "drizzle") { hand = Hand.Stroke; tiltPx = 0f; }
                    else { hand = Hand.Carry; BeginCarry(); }
                    break;

                case CraftUiMode.Place:
                    hand = Hand.Carry;
                    BeginCarry();
                    break;

                case CraftUiMode.Choose:
                    hand = Hand.Pick;
                    PickBottle(screen);
                    break;
            }
        }

        public void PointerMove(Vector2 screen)
        {
            if (s == null || hand == Hand.None) return;
            Vector2 delta = screen - lastPos;
            lastPos = screen;

            switch (hand)
            {
                case Hand.Turn: StepTurn(screen, delta); break;
                case Hand.Push: StepPush(delta); break;
                case Hand.Carry: StepCarry(screen); break;
                case Hand.Hold: StepHold(screen); break;
                case Hand.Stroke: tiltPx += delta.x; break;
            }
        }

        public void PointerUp()
        {
            if (s == null) { hand = Hand.None; return; }
            var was = hand;
            hand = Hand.None;

            switch (was)
            {
                case Hand.Turn:
                    s.EndPress();          // 转不够就别想过
                    break;
                case Hand.Push:
                    s.EndPress();          // 压到底停够没
                    pushDepth = 0f;
                    break;
                case Hand.Carry:
                    ReleaseCarry();
                    break;
                case Hand.Hold:
                    s.EndPress();
                    heldInPlace = false;
                    break;
                case Hand.Stroke:
                    if (Mathf.Abs(tiltPx) > 40f) s.Tap(0.06f);
                    tiltPx = 0f;
                    break;
            }
        }

        // ---------------------------------------------------------------- 转圈（磨豆）

        void StepTurn(Vector2 screen, Vector2 delta)
        {
            Vector2 c = ToScreen(BasketWorld);
            Vector2 a = turnPrev - c, b = screen - c;
            if (a.sqrMagnitude > 400f && b.sqrMagnitude > 400f)
            {
                float da = Mathf.Abs(Vector2.SignedAngle(a, b)) * Mathf.Deg2Rad;
                turnRadians += da;
                turnSinceTick += da;
            }
            turnPrev = screen;
            _ = delta;
        }

        // ---------------------------------------------------------------- 下拖（压粉）

        void StepPush(Vector2 delta)
        {
            pushDepth = Mathf.Clamp01(pushDepth - delta.y / Mathf.Max(1f, pushPixels));
            if (pushDepth > 0.02f) tiltPx += delta.x;   // 只往正下方压不会歪；手一歪就累加
        }

        // ---------------------------------------------------------------- 拖物件

        void BeginCarry()
        {
            carrying = true;
            CarryCount++;
            carryTarget = s.StepId == "milk" ? PitcherWorld : CupWorld;
        }

        void StepCarry(Vector2 screen)
        {
            var target = s.StepId == "milk" ? PitcherWorld : CupWorld;
            if (TryScreenToCounter(screen, target.y, out Vector3 world)) carryTarget = world;
        }

        void ReleaseCarry()
        {
            carrying = false;

            if (s.Mode == CraftUiMode.Place)
            {
                int best = -1;
                float bestD = hotspotRadius;
                for (int i = 0; i < 2; i++)
                {
                    float d = Vector3.Distance(carryTarget, spots[i]);
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best >= 0)
                {
                    // 萃取口那儿摆着粉碗：杯子往柜沿那一侧让开 0.12m，否则两个叠在一起，
                    // 画面里只剩"碗里插着个杯子"。粉碗在里侧，杯子在外侧，一眼分得清。
                    cup.position = spots[best] + Vector3.up * 0.046f;
                    s.PutAt(best);
                }
                else cup.position = cupHome;
                return;
            }

            // move / serve：拖到目标点就够了
            int need = s.StepId == "serve" ? 3 : 2;
            if (Vector3.Distance(carryTarget, spots[need]) < hotspotRadius)
            {
                cup.position = spots[need] + Vector3.up * 0.046f;
                s.Tap(0.06f);
            }
        }

        // ---------------------------------------------------------------- 打奶 / 选瓶

        void StepHold(Vector2 screen)
        {
            if (TryScreenToCounter(screen, PitcherWorld.y, out Vector3 world))
                carryTarget = world;

            if (pitcher != null)
                pitcher.position = Vector3.Lerp(pitcher.position, carryTarget, Time.deltaTime * 14f);

            heldInPlace = Vector3.Distance(PitcherWorld, spots[4]) < steamRadius;

            // 蒸汽打起来的奶面在抖：奶缸没到位就没有这层动静
            if (milkSurface != null && heldInPlace)
            {
                float wob = Mathf.Sin(Time.time * 11f) * 0.0015f;
                milkSurface.localPosition = new Vector3(0f, 0.082f + wob, 0f);
            }
        }

        void PickBottle(Vector2 screen)
        {
            var options = s.Options;
            int best = -1;
            float bestD = 76f;
            for (int i = 0; i < bottles.Length && i < options.Length; i++)
            {
                if (bottles[i] == null) continue;
                float d = Vector2.Distance(ToScreen(bottles[i].position), screen);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best >= 0) s.Choose(best);
        }

        // ---------------------------------------------------------------- 帧驱动

        void Update()
        {
            if (s == null) return;

            switch (hand)
            {
                case Hand.Turn:
                    if (turnSinceTick > 0.004f) { s.Tick(Time.deltaTime); TurnFrames++; }
                    turnSinceTick = 0f;
                    break;
                case Hand.Push:
                    // 手指压下去多少，粉饼就被压实多少 —— 抬手后不回弹（压过的粉就是压过了）
                    powderTamped = Mathf.Max(powderTamped, pushDepth * 0.85f);
                    if (pushDepth > 0.985f) { s.Tick(Time.deltaTime); PushFrames++; }
                    break;
                case Hand.Hold:
                    if (heldInPlace) s.Tick(Time.deltaTime);
                    break;
            }

            Animate();
        }

        // ---------------------------------------------------------------- 视觉

        void Animate()
        {
            if (row == null) return;

            int gi = StepIndex("grind");
            if (s.StepIndex == gi) powderLevel = Mathf.Clamp01(s.Progress01);
            else if (s.StepIndex < gi) powderLevel = 0f;

            if (powder != null && basket != null)
            {
                // 粉堆**一边长高一边铺开**。只让顶面升高是不够的 —— 54° 俯视下 1cm 高度差
                // 在屏幕上只有七八个像素，"磨了一半"和"磨满了"看起来一样。半径跟着涨，
                // 眼睛一量就知道还剩多少：一小撮 → 盖住半个碗底 → 铺满。
                float spread = Mathf.Lerp(0.18f, 1f, Mathf.Clamp01(powderLevel));
                float r = CraftProps.PowderR * spread;
                float top = Mathf.Lerp(CraftProps.PowderFloor + 0.003f, 0.0380f, powderLevel);
                float h = Mathf.Max(0.002f, (top - CraftProps.PowderFloor) * Mathf.Lerp(1f, 0.62f, powderTamped));
                powder.position = basket.position + Vector3.up * CraftProps.PowderFloor;
                powder.localScale = new Vector3(r, h, r);

                float deg = TiltFromPixels(tiltPx);
                powder.localEulerAngles = new Vector3(deg, 0f, deg * 0.6f);
                if (Mathf.Abs(deg) > 1f)
                {
                    // 歪得越多，粉被挤得越偏 —— 粉饼明显不在碗心，另一侧露出一弯亮钢碗底。
                    // 只转个角度是不够的：俯视下一个倾斜的圆柱看上去还是个正圆，什么都读不出来。
                    float k = Mathf.Clamp01(Mathf.Abs(deg) / 15f) * 0.0075f;
                    powder.position += new Vector3(-Mathf.Sign(deg) * k, 0f, Mathf.Sign(deg) * k * 0.6f);
                    powder.position += Vector3.up * (Mathf.Abs(deg) * 0.0003f);
                }
            }

            if (tamperHead != null && basket != null)
            {
                // 压粉器：底座在垫子西端，压下时先横移到碗口正上方，再沉进碗里。
                // 停住时它一定是"立在台面上"的姿态，不是浮在半空。
                float slide = Mathf.Clamp01(pushDepth * 4f);
                Vector3 at = Vector3.Lerp(tamperHome, basket.position, slide);
                float lift = Mathf.Lerp(0f, 0.006f, slide);
                lift = Mathf.Lerp(lift, 0.026f, pushDepth);
                tamperHead.position = at + Vector3.up * lift;
            }

            if (cup != null && carrying) cup.position = Vector3.Lerp(cup.position, carryTarget, Time.deltaTime * 22f);
            if (pitcher != null && carrying) pitcher.position = Vector3.Lerp(pitcher.position, carryTarget, Time.deltaTime * 22f);

            UpdateRings();
            RefreshProps();
        }

        static float TiltFromPixels(float px) => Mathf.Clamp(px * 0.09f, -15f, 15f);

        /// <summary>只摆出这一步用得上的道具，其余收起来：台面永远只留"手上这套"。</summary>
        void RefreshProps()
        {
            string id = s.StepId;
            var mode = s.Mode;

            bool needBasket = id == "grind" || id == "tamp" || id == "extract" || id == "move";
            bool needTamper = id == "grind" || id == "tamp" || id == "extract" || id == "move";
            bool needCup = true;      // 杯子全程在台面上（那杯东西从头到尾看得见）
            bool needPitcher = id == "milk";
            bool needBottles = mode == CraftUiMode.Choose;
            // 杯里的咖啡液要等萃取开始才有：空杯里凭空有一摊咖啡就穿帮了
            int extractAt = StepIndex("extract");
            bool needLiquid = extractAt >= 0 && s.StepIndex >= extractAt;

            if (basket != null) basket.gameObject.SetActive(needBasket);
            if (powder != null) powder.gameObject.SetActive(needBasket);
            if (tamperHead != null) tamperHead.gameObject.SetActive(needTamper);
            if (cup != null) cup.gameObject.SetActive(needCup);
            if (cupCoffee != null) cupCoffee.gameObject.SetActive(needCup && needLiquid);
            if (pitcher != null) pitcher.gameObject.SetActive(needPitcher || id == "milk");
            if (milkSurface != null) milkSurface.gameObject.SetActive(needPitcher);
            for (int i = 0; i < bottles.Length; i++)
                if (bottles[i] != null) bottles[i].gameObject.SetActive(needBottles);
        }

        void UpdateRings()
        {
            for (int i = 0; i < rings.Length; i++)
                if (rings[i] != null) rings[i].gameObject.SetActive(false);

            int show = -1;
            switch (s.Mode)
            {
                case CraftUiMode.Place: show = 0; break;
                case CraftUiMode.Press:
                    // 压粉也是 Press 模式，但那儿没有"落点" —— 圈只标杯子该去的两个地方
                    if (s.StepId == "serve") show = 3;
                    else if (s.StepId == "move") show = 2;
                    break;
                case CraftUiMode.Hold:
                    if (s.StepId == "milk") show = 4;
                    break;
            }
            if (show >= 0 && rings[show] != null) rings[show].gameObject.SetActive(true);
        }

        int StepIndex(string id)
        {
            var steps = s.Recipe.steps;
            if (steps == null) return -1;
            for (int i = 0; i < steps.Length; i++) if (steps[i].id == id) return i;
            return -1;
        }

        // ---------------------------------------------------------------- 搭建

        void Build()
        {
            if (row != null) return;

            // 道具**沿吧台长边横排**（世界 +x）。这条不跟着相机走：
            //     柜台只有 0.65m 深，要是按"与视线垂直"算，一旦相机在侧面就变成沿柜深排 ——
            //     整排 1.2m 会有一半扎进墙里、另一半悬在柜沿外。固定沿吧台铺才是唯一站得住的排法。
            //     符号取 +x：工作区在咖啡机东侧，往东铺才离机身越来越远。
            rowDir = Vector3.right;
            rowFwd = Vector3.Cross(Vector3.up, rowDir).normalized;   // 朝着柜台里

            row = new GameObject("CraftProps").transform;
            row.SetParent(transform, false);

            // 材质按真实器具的质感给：不锈钢高金属度、陶瓷低金属高光、咖啡粉几乎全哑。
            // 之前全是 metallic 0.35 的"灰塑料"，明暗层次起不来，再好的形状也像积木。
            var steel = PropMesh.Mat(new Color(0.80f, 0.81f, 0.82f), 0.72f, 0.60f);
            var wood = PropMesh.Mat(new Color(0.27f, 0.15f, 0.085f), 0f, 0.32f);   // 压粉器把手是木头的
            var ceramic = PropMesh.Mat(new Color(0.96f, 0.95f, 0.92f), 0.02f, 0.62f);
            var coffee = PropMesh.Mat(new Color(0.33f, 0.19f, 0.10f), 0f, 0.16f);
            var milkMat = PropMesh.Mat(new Color(0.97f, 0.96f, 0.93f), 0f, 0.45f);
            var ringMat = PropMesh.Mat(new Color(1f, 0.85f, 0.55f), 0f, 0.5f);
            // 垫子压暗：道具全是亮钢/白瓷，浅灰垫子会把它们一起洗白；深暖灰才衬得出来
            var padMat = PropMesh.Mat(new Color(0.31f, 0.28f, 0.24f), 0f, 0.30f);
            var padEdge = PropMesh.Mat(new Color(0.17f, 0.15f, 0.13f), 0f, 0.26f);

            // 一横排，西（木桌侧）→ 东（咖啡机侧）：
            //   出杯位 → 压粉器座 → 粉碗 → 台面(放错) → 托盘 → 萃取口 → 蒸汽位
            // 间距 0.12~0.16 按道具直径定（杯子 ⌀0.145 带把手 0.175，是最大的一件）。
            Vector3 A(float d) => anchor + rowDir * d;
            spots[0] = A(0.24f);                  // 萃取口（紧贴咖啡机）
            spots[1] = A(0.06f);                  // 台面（放错的那个）
            spots[2] = A(0.14f);                  // 托盘
            spots[3] = A(-0.26f);                 // 出杯位（靠木桌那侧）
            spots[4] = A(0.28f);                  // 蒸汽位

            // 工作垫：圆角 + 上下倒角，比道具群略大一圈。
            // 垫子不只是底衬 —— 它把 0.80m 的"操作区"从整片木纹台面里划出来，近景才有构图。
            pad = CraftProps.WorkPad(padMat, padEdge, row).transform;
            pad.position = new Vector3(anchor.x + 0.01f, anchor.y - 0.012f, anchor.z);

            for (int i = 0; i < rings.Length; i++)
            {
                rings[i] = CraftProps.SpotRing(ringMat, row).transform;
                rings[i].position = spots[i] + Vector3.up * 0.0006f;
                rings[i].gameObject.SetActive(false);
            }

            // 粉碗：固定站在工作台中间偏西（不占任何热点）。磨豆、压粉都在它上面。
            basket = CraftProps.Basket(steel, row).transform;
            basket.position = A(-0.06f);

            powder = CraftProps.Powder(coffee, row).transform;

            // 压粉器有它自己的座位（垫子最西端），压粉时才横移到碗上
            tamperHome = A(-0.20f);
            tamperHead = CraftProps.Tamper(steel, wood, row).transform;
            tamperHead.position = tamperHome;

            cupHome = A(-0.30f);
            cup = CraftProps.Cup(ceramic, row).transform;
            cup.position = cupHome;
            cupCoffee = CraftProps.CupLiquid(coffee, cup).transform;
            cupCoffee.localPosition = new Vector3(0f, 0.052f, 0f);

            // 奶缸只在"打奶"那步出现：平时收在粉碗那个位置（那时碗已经收起来了）
            pitcherHome = A(-0.06f);
            pitcher = CraftProps.Pitcher(steel, row).transform;
            pitcher.position = pitcherHome;
            milkSurface = CraftProps.MilkSurface(milkMat, pitcher).transform;
            milkSurface.localPosition = new Vector3(0f, 0.082f, 0f);

            // 小瓶只在"选料"那步出现（其余道具这时都收起来了），可以占用主排的位置
            for (int i = 0; i < bottles.Length; i++)
            {
                Vector3 p = A(-0.15f + i * 0.075f);
                bottles[i] = CraftProps.Bottle(i == 0 ? milkMat : coffee, row).transform;
                bottles[i].position = p;
            }

            row.gameObject.SetActive(false);
            ResetProps();
        }

        void ResetProps()
        {
            if (row == null) return;
            if (cup != null) cup.position = cupHome;
            if (pitcher != null) pitcher.position = pitcherHome;
            if (tamperHead != null) tamperHead.position = tamperHome;
            if (powder != null)
            {
                powder.localScale = new Vector3(CraftProps.PowderR * 0.3f, 0.003f, CraftProps.PowderR * 0.3f);
                powder.localEulerAngles = Vector3.zero;
            }
            pushDepth = 0f;
            tiltPx = 0f;
            turnRadians = 0f;
            powderLevel = 0f;
            powderTamped = 0f;
        }

        // ---------------------------------------------------------------- 查询（验收 / 截图脚本用）

        public Vector3 BasketWorld => basket != null ? basket.position : anchor;
        public Vector3 CupWorld => cup != null ? cup.position : anchor;
        public Vector3 PitcherWorld => pitcher != null ? pitcher.position : anchor;
        public Vector3 SpotWorld(int i) => spots[Mathf.Clamp(i, 0, spots.Length - 1)];
        public Vector3 BottleWorld(int i) => bottles[Mathf.Clamp(i, 0, bottles.Length - 1)] != null
            ? bottles[Mathf.Clamp(i, 0, bottles.Length - 1)].position : anchor;

        public Vector2 ToScreen(Vector3 world)
        {
            if (cam == null) return Vector2.zero;
            Vector3 p = cam.WorldToScreenPoint(world);
            return new Vector2(p.x, p.y);
        }

        bool TryScreenToCounter(Vector2 screen, float y, out Vector3 world)
        {
            world = anchor;
            if (cam == null) return false;
            var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
            var ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            if (!plane.Raycast(ray, out float enter)) return false;
            world = ray.GetPoint(enter);
            return true;
        }
    }
}

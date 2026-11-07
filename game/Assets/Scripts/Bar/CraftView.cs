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
        Transform row, pad, basket, basketInner, powder, tamperHead, tamperBase, cup, cupCoffee, pitcher, milkSurface;
        Transform[] rings = new Transform[5];
        readonly Transform[] bottles = new Transform[3];

        Vector3 rowDir;
        Vector3 rowFwd;                 // 从主排指向柜台里侧（Build 里算一次，释放落点也要用）
        Vector3 cupHome, pitcherHome;
        Vector3[] spots = new Vector3[5];   // 0 萃取口 / 1 台面 / 2 托盘 / 3 出杯位 / 4 蒸汽位

        Hand hand;
        Vector2 downPos, lastPos;
        Vector2 turnPrev;
        float turnRadians;
        float turnSinceTick;
        float pushDepth;
        float tiltPx;
        float powderLevel;
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
                var mp = milkSurface.localPosition;
                float wob = Mathf.Sin(Time.time * 11f) * 0.0015f;
                milkSurface.localPosition = new Vector3(mp.x, mp.y + wob, mp.z);
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

            if (powder != null)
            {
                // 粉面在碗壁里从底往上涨：粉少的时候碗口那圈深色内衬露在外面，涨满了才快齐口。
                // 高度就是"磨了多少粉"——低一截就是不够，不需要任何文字提示。
                float top = Mathf.Lerp(0.008f, 0.042f, powderLevel);
                float bottom = 0.006f;
                float h = Mathf.Max(0.002f, (top - bottom) * 0.5f);
                var sc = powder.localScale;
                powder.localScale = new Vector3(sc.x, h, sc.z);
                powder.position = basket.position + Vector3.up * ((top + bottom) * 0.5f);

                float deg = TiltFromPixels(tiltPx);
                powder.localEulerAngles = new Vector3(deg, 0f, deg * 0.6f);
                if (Mathf.Abs(deg) > 1f)
                {
                    // 歪得越多，高的一侧越顶出碗口。只靠倾角，俯视里高低差只有十几像素，
                    // 看不出"压歪了"；粉饼翘出碗沿，一眼就知道这一下没压平。
                    powder.position += Vector3.up * (Mathf.Abs(deg) * 0.0013f);
                }
            }

            if (tamperHead != null)
            {
                // 压头跟着手下拖走：先横移到粉碗正上方，再压进碗里。
                // 静止时它立在碗旁边的压粉器座上（真吧台也是这么摆的）。
                Vector3 over = basket != null ? basket.position : tamperBase.position;
                float slide = Mathf.Clamp01(pushDepth * 3.5f);
                Vector3 at = Vector3.Lerp(tamperBase.position, over, slide);
                float lift = Mathf.Lerp(0.100f, 0.020f, pushDepth);
                tamperHead.position = at + Vector3.up * lift;
            }

            if (cup != null && carrying) cup.position = Vector3.Lerp(cup.position, carryTarget, Time.deltaTime * 22f);
            // 杯里的咖啡液是独立物件（Cup 是空杯、液面另放），杯子一动它必须跟着，
            // 否则杯子被拖走后原处会留一摊咖啡。
            if (cup != null && cupCoffee != null) cupCoffee.position = cup.position + Vector3.up * 0.034f;
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
            bool needTamper = id == "tamp";
            bool needCup = true;      // 杯子全程在台面上（那杯东西从头到尾看得见）
            bool needPitcher = id == "milk";
            bool needBottles = mode == CraftUiMode.Choose;
            // 杯里的咖啡液要等萃取开始才有：空杯里凭空有一摊咖啡就穿帮了
            int extractAt = StepIndex("extract");
            bool needLiquid = extractAt >= 0 && s.StepIndex >= extractAt;

            if (basket != null) basket.gameObject.SetActive(needBasket);
            if (basketInner != null) basketInner.gameObject.SetActive(needBasket);
            if (powder != null) powder.gameObject.SetActive(needBasket);
            if (tamperBase != null) tamperBase.gameObject.SetActive(needTamper);
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
                    show = s.StepId == "serve" ? 3 : 2;
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

            var steel = Mat(new Color(0.72f, 0.73f, 0.75f), 0.35f, 0.45f);
            var dark = Mat(new Color(0.16f, 0.16f, 0.17f), 0.2f, 0.6f);
            var darkInner = Mat(new Color(0.17f, 0.14f, 0.12f), 0f, 0.35f);   // 碗内壁：暖深棕，比纯黑像"装过粉的碗"
            var ceramic = Mat(new Color(0.96f, 0.95f, 0.92f), 0.05f, 0.35f);
            var coffee = Mat(new Color(0.42f, 0.27f, 0.16f), 0f, 0.75f);
            var milkMat = Mat(new Color(0.97f, 0.96f, 0.93f), 0f, 0.4f);
            var ringMat = Mat(new Color(1f, 0.88f, 0.66f), 0f, 0.5f);
            var padMat = Mat(new Color(0.62f, 0.60f, 0.56f), 0f, 0.3f);

            // 一横排，从西（萃取口，靠旧木桌）到东（出杯位，靠咖啡机）：
            //   粉碗 → 奶缸 → 蒸汽位 → 压粉器 → 台面(放错) → 杯子 → 托盘 → 出杯位
            // 间距 0.175、整排 0.88m，正好铺进咖啡机与木桌之间那 1.10m 的空台面。
            // 道具尺寸比实物稍大（粉碗 ⌀0.14 对真实 0.058）：1m 外的俯视近景里，
            // 真实比例的碗壁只有 2~3mm，屏幕上根本分不出碗沿和粉面。
            Vector3 A(float d) => anchor + rowDir * d;
            // 顺序按真实动线排：萃取口在最东（贴着咖啡机），一路往西是放错的台面、
            // 托盘、出杯位。萃取口不能跟粉碗同位置 —— 杯子放进去就是"碗里插了个杯子"。
            spots[0] = A(0.40f);                  // 萃取口（紧贴咖啡机那侧）
            spots[4] = A(0.22f);                  // 蒸汽位
            spots[1] = A(-0.02f);                 // 台面（放错的那个）
            spots[2] = A(-0.18f);                 // 托盘
            spots[3] = A(-0.32f);                 // 出杯位（靠木桌那侧）

            // 工作垫：整片木纹台面上道具没有落点感。浅色防滑垫把"操作区"从台面里分出来，
            // 也当作深色道具（钢碗、咖啡粉）的底衬 —— 深色垫子会把这些全吞掉。
            // 宽度贴着整排走（±0.35）：垫子比道具群大太多，近景里就只是"一大片灰"。
            pad = Box("WorkPad", new Vector3(0.70f, 0.012f, 0.34f),
                      new Vector3(anchor.x, anchor.y - 0.006f, anchor.z), padMat, row);

            for (int i = 0; i < rings.Length; i++)
            {
                rings[i] = Cyl("Ring" + i, 0.130f, 0.003f, spots[i] + Vector3.up * 0.002f, ringMat, row);
                rings[i].gameObject.SetActive(false);
            }

            // 粉碗固定站在工作台中间偏西（不占任何热点）：磨豆、压粉都在它上面，
            // 杯子的萃取口在另一头贴着咖啡机，两件东西永远不会叠。
            Vector3 basketSpot = A(-0.20f);
            basket = Cyl("Basket", 0.140f, 0.055f, basketSpot + Vector3.up * 0.0275f, steel, row);
            // 碗壁内衬：比碗口低 7mm，粉面在它里面涨 —— 粉少的时候看得见一圈深色碗壁，满了才快齐口
            basketInner = Cyl("BasketInner", 0.112f, 0.048f, basketSpot + Vector3.up * 0.024f, darkInner, row);
            powder = Cyl("Powder", 0.096f, 0.034f, basketSpot + Vector3.up * 0.020f, coffee, row);

            tamperBase = Cyl("TamperBase", 0.130f, 0.014f, A(-0.06f) + Vector3.up * 0.006f, steel, row);
            tamperHead = new GameObject("TamperHead").transform;
            tamperHead.SetParent(row, false);
            tamperHead.position = tamperBase.position;
            Cyl("TamperDisc", 0.130f, 0.034f, tamperBase.position + Vector3.up * 0.020f, steel, tamperHead);
            Cyl("TamperGrip", 0.040f, 0.120f, tamperBase.position + Vector3.up * 0.085f, dark, tamperHead);

            cupHome = A(0.12f) + Vector3.up * 0.046f;
            cup = Cyl("Cup", 0.100f, 0.105f, cupHome, ceramic, row);
            cupCoffee = Cyl("CupCoffee", 0.082f, 0.004f, cupHome + Vector3.up * 0.034f, coffee, row);

            // 奶缸和粉碗不会同时出现（打奶那步粉碗已经收起来了），摆得近一点没关系
            pitcherHome = A(0.06f) + Vector3.up * 0.056f;   // 在蒸汽位旁边但不算"在位"
            pitcher = Cyl("Pitcher", 0.110f, 0.115f, pitcherHome, steel, row);
            milkSurface = Cyl("Milk", 0.088f, 0.004f, pitcherHome + Vector3.up * 0.050f, milkMat, row);

            // 小瓶只在"选料"那步出现（其余道具这时都收起来了），可以借用主排的位置
            for (int i = 0; i < bottles.Length; i++)
            {
                Vector3 p = A(-0.24f + i * 0.075f) + rowFwd * 0.05f + Vector3.up * 0.050f;
                bottles[i] = Cyl("Bottle" + i, 0.048f, 0.110f, p, i == 0 ? milkMat : coffee, row);
            }

            row.gameObject.SetActive(false);
        }

        void ResetProps()
        {
            if (row == null) return;
            if (cup != null) cup.position = cupHome;
            if (pitcher != null) pitcher.position = pitcherHome;
            if (tamperHead != null) tamperHead.localPosition = new Vector3(0f, 0f, 0f);
            if (powder != null)
            {
                var sc = powder.localScale;
                powder.localScale = new Vector3(sc.x, 0.001f, sc.z);
                powder.localEulerAngles = Vector3.zero;
            }
            pushDepth = 0f;
            tiltPx = 0f;
            turnRadians = 0f;
            powderLevel = 0f;
        }

        static Transform Cyl(string name, float diameter, float height, Vector3 world, Material m, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.position = world;
            go.transform.localScale = new Vector3(diameter, Mathf.Max(0.001f, height * 0.5f), diameter);
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return go.transform;
        }

        static Transform Box(string name, Vector3 size, Vector3 world, Material m, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.position = world;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return go.transform;
        }

        static Material Mat(Color c, float metallic, float smooth)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh);
            // URP Lit 的主色是 _BaseColor；Material.color 走的是 _Color，在 URP 下静默无效，
            // 设进去的道具会全是白模、只剩环境反射的颜色（钢碗发蓝、咖啡粉发白就是这么来的）。
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            else m.color = c;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            return m;
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

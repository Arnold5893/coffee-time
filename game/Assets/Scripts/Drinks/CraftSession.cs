using System;

namespace CoffeeShop.Drinks
{
    /// <summary>制作界面当前该摆什么控件。由当前步骤的原语决定，UI 只读这个、不自己判断。</summary>
    public enum CraftUiMode
    {
        Press = 0,    // click：点一下（有 param 的步骤要按到位）
        Hold = 1,     // hold：按住不放，有进度
        Choose = 2,   // select：从候选里挑一个
        Place = 3,    // place：把杯子放到某个位置（热点）
        Wait = 4,     // wait：等它自己完成，有进度，不需要操作
        Decide = 5,   // 判偏了：重做这一步，还是带着偏差继续（7.9.4）
        Done = 6,     // 出杯了
    }

    /// <summary>给表现层的即时反馈。只有这几种，**没有档位**——文字评级不许出现在玩家界面上（FR-EVAL-03）。</summary>
    public enum CraftFeedback
    {
        StepDone = 0,     // 一步做完
        Deviation = 1,    // 判偏：画面闪一下 + 一声闷响
        Blocked = 2,      // 系统门槛挡住（不判错，FR-EVAL-04）
        WrongAction = 3,  // 点了不该点的原语
        Served = 4,       // 出杯
    }

    /// <summary>
    /// 制作界面的**唯一业务入口**（07 票 / NFR-05）。
    ///
    /// UI 层只允许调这里的方法，一个都不许直接改订单、配料或 <see cref="DrinkMaker"/> 的字段；
    /// 每一次操作都只是"发一个请求"，由 <see cref="DrinkMaker"/> 自己的状态机决定接受还是拒绝。
    /// 这样"UI 不能改写业务数据"是结构性的——CraftPanel 手里根本没有 DrinkMaker 之外的写入口。
    ///
    /// 按压语义（PC 鼠标 / 移动端点触完全一致）：
    ///   <see cref="BeginPress"/> 指针按下 → 每帧 <see cref="Tick"/> → <see cref="EndPress"/> 抬起。
    ///   Hold 在按住期间攒进度、抬起才结算（按够=好，没够=偏差）；
    ///   Press 用按下时长结算（压粉要按 0.4 秒，快点和慢点的结果不一样）。
    /// </summary>
    public sealed class CraftSession
    {
        readonly DrinkMaker maker;
        bool pressing;
        float pressSeconds;

        /// <summary>给表现层的即时反馈（闪光/音效/抖动），不含任何档位信息。</summary>
        public event Action<CraftFeedback> Feedback;

        public CraftSession(Recipe recipe)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            maker = new DrinkMaker(recipe);

            maker.StepFinished += r =>
            {
                if (r.Outcome == StepOutcome.Off)
                {
                    Deviations++;
                    Emit(CraftFeedback.Deviation);
                }
                else Emit(CraftFeedback.StepDone);
            };
            maker.ServedEvent += r => LastReview = r;
        }

        public Recipe Recipe => maker.Recipe;
        public DrinkMaker Maker => maker;

        public int StepIndex => maker.Index;
        public int StepCount => Recipe.steps == null ? 0 : Recipe.steps.Length;
        public string StepId => maker.Current != null ? maker.Current.id : "";
        public string StepName => maker.Current != null ? maker.Current.name : "";
        public MakePrimitive Primitive => maker.Current != null ? maker.Current.Primitive : MakePrimitive.Click;

        /// <summary>当前步骤的 param：hold=达标秒数、click=按下达标秒数（0=点一下）、wait=等待秒数。</summary>
        public float StepParam => maker.Current != null ? maker.Current.param : 0f;

        /// <summary>按住/等待型的进度 0–1。</summary>
        public float Progress01 => maker.Progress01;

        /// <summary>
        /// 本步的候选：select 是选项，place 是热点，别的原语为空。
        /// place 步骤没写 options 时，用配方里的 targets 兜底（单一热点）。
        /// </summary>
        public string[] Options
        {
            get
            {
                var c = maker.Current;
                if (c == null) return Array.Empty<string>();
                if (c.Primitive == MakePrimitive.Select || c.Primitive == MakePrimitive.Place)
                {
                    if (c.options != null && c.options.Length > 0) return c.options;
                    if (!string.IsNullOrEmpty(c.correct)) return new[] { c.correct };
                }
                return Array.Empty<string>();
            }
        }

        public CraftUiMode Mode
        {
            get
            {
                if (maker.Served || maker.Finished) return CraftUiMode.Done;
                if (maker.PendingRetry) return CraftUiMode.Decide;
                switch (Primitive)
                {
                    case MakePrimitive.Hold: return CraftUiMode.Hold;
                    case MakePrimitive.Select: return CraftUiMode.Choose;
                    case MakePrimitive.Place: return CraftUiMode.Place;
                    case MakePrimitive.Wait: return CraftUiMode.Wait;
                    default: return CraftUiMode.Press;
                }
            }
        }

        /// <summary>判偏了多少次（整杯评价的原始素材，不给玩家看）。</summary>
        public int Deviations { get; private set; }
        /// <summary>被系统门槛挡了多少次（不算错）。</summary>
        public int BlockedActions { get; private set; }
        public int WrongActions { get; private set; }
        public bool IsPressing => pressing;
        public float PressSeconds => pressSeconds;
        public DrinkReview LastReview { get; private set; }
        public MakeAction LastResult { get; private set; } = MakeAction.Ok;

        // ------------------------------------------------------------ 按压

        /// <summary>指针按下。Choose/Place/Decide/Wait 不需要按，调用无副作用。</summary>
        public void BeginPress()
        {
            if (Mode == CraftUiMode.Done) return;
            pressing = true;
            pressSeconds = 0f;
        }

        /// <summary>每帧喂一次时间：等待型自动推进；按住型只在按下期间攒进度。</summary>
        public void Tick(float dt)
        {
            dt = Math.Max(0f, dt);
            if (Mode == CraftUiMode.Wait) { Note(maker.Await(dt)); return; }
            if (!pressing) return;

            pressSeconds += dt;
            if (Mode == CraftUiMode.Hold) Note(maker.Hold(dt));
        }

        /// <summary>指针抬起，结算这一次按压。</summary>
        public void EndPress()
        {
            if (!pressing) return;
            pressing = false;

            switch (Mode)
            {
                case CraftUiMode.Hold: Note(maker.Release()); break;
                case CraftUiMode.Press: Note(maker.Click(pressSeconds)); break;
            }
            pressSeconds = 0f;
        }

        /// <summary>自动化/快捷键用：按住 <paramref name="seconds"/> 秒再抬起。</summary>
        public MakeAction HoldFor(float seconds, float step = 0.05f)
        {
            if (maker.Served) return Note(MakeAction.Served);
            if (Mode != CraftUiMode.Hold && Mode != CraftUiMode.Press) return Note(MakeAction.NotCurrent);
            BeginPress();
            for (float t = 0f; t < seconds - 1e-4f; t += step) Tick(Math.Min(step, seconds - t));
            EndPress();
            return LastResult;
        }

        /// <summary>自动化/快捷键用：按一下（click 步骤带按下时长）。</summary>
        public MakeAction Tap(float seconds = 0.06f)
        {
            if (maker.Served) return Note(MakeAction.Served);
            if (Mode == CraftUiMode.Press) return Note(maker.Click(seconds));
            if (Mode == CraftUiMode.Hold) return HoldFor(0f);   // 等于按下即抬起 → 偏差
            return Note(MakeAction.NotCurrent);
        }

        // ------------------------------------------------------------ 选择 / 放置

        public MakeAction Choose(int optionIndex)
        {
            if (maker.Served) return Note(MakeAction.Served);
            var opts = Options;
            if (Mode != CraftUiMode.Choose) return Note(MakeAction.NotCurrent);
            if (optionIndex < 0 || optionIndex >= opts.Length) return Note(MakeAction.NotCurrent);
            return Note(maker.Choose(opts[optionIndex]));
        }

        public MakeAction PutAt(int hotspotIndex)
        {
            if (maker.Served) return Note(MakeAction.Served);
            var opts = Options;
            if (Mode != CraftUiMode.Place) return Note(MakeAction.NotCurrent);
            if (hotspotIndex < 0 || hotspotIndex >= opts.Length) return Note(MakeAction.NotCurrent);
            return Note(maker.Put(opts[hotspotIndex]));
        }

        // ------------------------------------------------------------ 决定 / 跳过 / 出杯

        /// <summary>把刚做偏的这一步重做（抹掉偏差）。</summary>
        public bool Retry() => maker.RetryStep();

        /// <summary>带着偏差继续（FR-EVAL-02 的"一般/差"就是这样攒出来的）。</summary>
        public bool Continue() => maker.Continue();

        /// <summary>跳过这一步（FR-MAKE-01）。必需步跳过 = 缺步。</summary>
        public bool Skip() => maker.SkipCurrent();

        /// <summary>出杯（可提前，FR-MAKE-01）。返回整杯评价——评价只给老板/顾客，不给玩家界面。</summary>
        public DrinkReview Serve()
        {
            var review = maker.Serve();
            LastReview = review;
            pressing = false;
            Emit(CraftFeedback.Served);
            return review;
        }

        /// <summary>整杯跑完（走到最后一步且最后一步做完）。</summary>
        public bool ReadyToServe => maker.Finished && !maker.Served;

        // ------------------------------------------------------------ 内部

        MakeAction Note(MakeAction action)
        {
            LastResult = action;
            switch (action)
            {
                case MakeAction.Blocked:
                    BlockedActions++;
                    Emit(CraftFeedback.Blocked);
                    break;
                case MakeAction.NotCurrent:
                case MakeAction.Invalid:
                    WrongActions++;
                    Emit(CraftFeedback.WrongAction);
                    break;
            }
            return action;
        }

        void Emit(CraftFeedback f) => Feedback?.Invoke(f);

        public string Describe()
        {
            var c = maker.Current;
            return $"{Recipe.sku} 第{maker.Index + 1}/{StepCount}步" +
                   (c != null ? $"[{c.id}:{c.name}]" : "[完毕]") +
                   $" mode={Mode} 进度={Progress01:P0} 偏差={Deviations} 挡={BlockedActions}" +
                   (maker.Served ? " 已出杯" : "");
        }
    }
}

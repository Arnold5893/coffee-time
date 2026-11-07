using System;
using System.Collections.Generic;
using System.Linq;

namespace CoffeeShop.Drinks
{
    /// <summary>一步的结局。</summary>
    public enum StepOutcome
    {
        Pending = 0,   // 还没做（含被跳过之外的"没轮到"）
        Good = 1,      // 做到位
        Off = 2,       // 做了但有偏差
        Skipped = 3,   // 跳过（必需步跳过 = 缺步）
    }

    /// <summary>一次操作请求的结果。Blocked 是系统门槛，不算错（FR-EVAL-04）。</summary>
    public enum MakeAction
    {
        Ok = 0,        // 这一步完成或推进了
        InProgress = 1,// 按住/等待还没到量，继续
        Blocked = 2,   // 门槛没满足，操作不被允许（不判错）
        NotCurrent = 3,// 和当前步骤的原语不符
        Invalid = 4,   // 状态不允许（已出杯 / 待决定重做还是继续）
        Served = 5,    // 已经出杯了
    }

    public readonly struct StepResult
    {
        public readonly string StepId;
        public readonly string Name;
        public readonly bool Judged;
        public readonly StepOutcome Outcome;
        public readonly bool Required;

        public StepResult(RecipeStep step, StepOutcome outcome)
        {
            StepId = step.id;
            Name = step.name;
            Judged = step.judged;
            Required = step.IsRequired;
            Outcome = outcome;
        }

        public override string ToString() =>
            $"{StepId}={(Outcome == StepOutcome.Off ? "偏差" : Outcome == StepOutcome.Skipped ? "跳过" : "好")}" +
            (Judged ? "(判)" : "") + (Required ? "" : "(可选)");
    }

    /// <summary>四档（FR-EVAL-02 / 7.9.6）。档位只给老板与顾客用，不给玩家显示文字评级。</summary>
    public enum DrinkQuality
    {
        Good = 0,
        Fair = 1,
        Poor = 2,
        Terrible = 3,
    }

    /// <summary>出杯后的一份评价依据：判定步数、错误步数、错误率、缺了哪些必需步。</summary>
    public class DrinkReview
    {
        public int JudgedSteps;
        public int ErrorSteps;
        public float ErrorRate;
        public bool MissingRequired;
        public string[] MissingStepIds = Array.Empty<string>();
        public DrinkQuality Quality;

        public override string ToString() =>
            $"错误 {ErrorSteps}/{JudgedSteps}（{ErrorRate:P0}）→ {Quality}" +
            (MissingRequired ? $" 缺步: {string.Join(",", MissingStepIds)}" : "");
    }

    /// <summary>
    /// 一杯饮品的制作过程：状态拥有者。手动（玩家）与自动（老板）两种驱动都打在这里，
    /// 读的是同一份 <see cref="Recipe"/>（FR-MAKE-02）。
    ///
    /// 规则：
    ///   - 只有**当前步**能操作，别的原语一律 NotCurrent；
    ///   - 有 gate 的步骤，门槛没满足时一律 Blocked —— 只是不许做，不记偏差（FR-EVAL-04）；
    ///   - 判定步做偏了会停在"待决定"：`RetryStep()` 重做（抹掉偏差）或 `Continue()` 带着偏差走（7.9.4）；
    ///   - 可以随时 `Serve()` 提前出杯；没做的必需步算缺步，缺步直接判非常差（FR-EVAL-01）；
    ///   - 自动驱动按 `cost` 计时推进，完全不产生偏差。
    /// </summary>
    public class DrinkMaker
    {
        readonly StepOutcome[] outcomes;
        int index;
        float progress;         // 当前步的按住/等待进度（秒）
        bool served;

        public Recipe Recipe { get; }
        public int Index => index;
        public bool Served => served;

        /// <summary>做偏了但还没决定重做还是继续。</summary>
        public bool PendingRetry { get; private set; }

        public RecipeStep Current => index < (Recipe.steps?.Length ?? 0) ? Recipe.steps[index] : null;
        public bool Finished => index >= (Recipe.steps?.Length ?? 0);

        /// <summary>当前步的进度（0–1），按住与等待型用得上。</summary>
        public float Progress01
        {
            get
            {
                var c = Current;
                if (c == null || c.param <= 0.01f) return 0f;
                return Math.Min(1f, progress / c.param);
            }
        }

        /// <summary>本步每次推进后已经攒了多少秒（按住/等待共用）。</summary>
        public float Elapsed => progress;

        public IReadOnlyList<StepResult> Results { get; private set; } = Array.Empty<StepResult>();

        /// <summary>某一步的结局（没做返回 Pending）；出杯后也保留。</summary>
        public StepOutcome OutcomeOf(string stepId)
        {
            var s = Recipe.Find(stepId);
            if (s == null) return StepOutcome.Pending;
            int at = Array.IndexOf(Recipe.steps, s);
            return at < 0 ? StepOutcome.Pending : outcomes[at];
        }

        public event Action<StepResult> StepFinished;
        public event Action<DrinkReview> ServedEvent;

        public DrinkMaker(Recipe recipe)
        {
            Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            if (recipe.steps == null || recipe.steps.Length == 0)
                throw new ArgumentException("配方没有步骤", nameof(recipe));
            outcomes = new StepOutcome[recipe.steps.Length];
        }

        // ------------------------------------------------------------------ 手动驱动

        /// <summary>
        /// 按住（磨豆、加奶）。每帧喂 dt 攒进度，松手才结算（见 <see cref="Release"/>）。
        /// 按过头不罚：进度到 param 就停住，多按的秒数不算数。
        /// </summary>
        public MakeAction Hold(float dt)
        {
            var guard = Guard(MakePrimitive.Hold);
            if (guard != MakeAction.Ok) return guard;

            progress = Math.Min(Current.param, progress + Math.Max(0f, dt));
            return MakeAction.InProgress;
        }

        /// <summary>松手结算：按够 = 好，没按够 = 偏差（按住类唯一的偏差来源）。</summary>
        public MakeAction Release()
        {
            var guard = Guard(MakePrimitive.Hold);
            if (guard != MakeAction.Ok) return guard;

            bool enough = progress >= Current.param - 1e-4f;
            return Complete(enough ? StepOutcome.Good : StepOutcome.Off);
        }

        /// <summary>点击（压粉、取杯、出杯）。press 是按下时长：判定步要求按到位。</summary>
        public MakeAction Click(float press = 0f)
        {
            var guard = Guard(MakePrimitive.Click);
            if (guard != MakeAction.Ok) return guard;

            bool enough = Current.param <= 0.01f || press >= Current.param - 1e-4f;
            return Complete(Current.judged && !enough ? StepOutcome.Off : StepOutcome.Good);
        }

        /// <summary>选择（选奶、选风味）。选错 = 偏差。</summary>
        public MakeAction Choose(string option)
        {
            var guard = Guard(MakePrimitive.Select);
            if (guard != MakeAction.Ok) return guard;

            bool right = string.Equals(option, Current.correct, StringComparison.Ordinal);
            return Complete(right ? StepOutcome.Good : StepOutcome.Off);
        }

        /// <summary>放置（放杯、上托盘）。放错位置 = 偏差。</summary>
        public MakeAction Put(string target)
        {
            var guard = Guard(MakePrimitive.Place);
            if (guard != MakeAction.Ok) return guard;

            bool right = string.Equals(target, Current.correct, StringComparison.Ordinal);
            return Complete(right ? StepOutcome.Good : StepOutcome.Off);
        }

        /// <summary>等待（萃取、浸泡）。攒够 param 自动完成，等待型没有判定。</summary>
        public MakeAction Await(float dt)
        {
            var guard = Guard(MakePrimitive.Wait);
            if (guard != MakeAction.Ok) return guard;

            progress += Math.Max(0f, dt);
            if (progress >= Current.param - 1e-4f) return Complete(StepOutcome.Good);
            return MakeAction.InProgress;
        }

        /// <summary>跳过当前步（FR-MAKE-01 步骤可跳过）。必需步跳过 = 缺步。</summary>
        public bool SkipCurrent()
        {
            if (served || PendingRetry || Current == null) return false;
            outcomes[index] = StepOutcome.Skipped;
            var snapshot = new StepResult(Current, StepOutcome.Skipped);
            Advance();
            StepFinished?.Invoke(snapshot);
            return true;
        }

        /// <summary>原地重做当前步：抹掉刚记下的偏差，回到未做状态（7.9.4）。</summary>
        public bool RetryStep()
        {
            if (served || !PendingRetry) return false;
            outcomes[index] = StepOutcome.Pending;
            progress = 0f;
            PendingRetry = false;
            return true;
        }

        /// <summary>带着偏差继续（7.9.4）。</summary>
        public bool Continue()
        {
            if (served || !PendingRetry) return false;
            PendingRetry = false;
            Advance();
            return true;
        }

        // ------------------------------------------------------------------ 自动驱动（老板）

        /// <summary>
        /// 自动驱动：按步骤的 cost 计时推进，一步不错（FR-MAKE-02 的"动画＋计时、无偏差"）。
        /// 老板队列每帧喂 dt 即可，不需要任何输入。
        /// </summary>
        public MakeAction AutoTick(float dt)
        {
            if (served) return MakeAction.Served;
            if (PendingRetry) { Continue(); return MakeAction.Ok; }

            var step = Current;
            if (step == null) return MakeAction.Ok;

            progress += Math.Max(0f, dt);
            float cost = step.cost > 0.01f ? step.cost : 1f;
            if (progress < cost - 1e-4f) return MakeAction.InProgress;
            return Complete(StepOutcome.Good);
        }

        /// <summary>自动驱动跑完整杯。</summary>
        public void AutoRun(float dt = 0.1f, float maxSeconds = 120f)
        {
            for (float t = 0f; t < maxSeconds && !served && !Finished; t += dt) AutoTick(dt);
        }

        // ------------------------------------------------------------------ 出杯与评价

        /// <summary>出杯（可提前，FR-MAKE-01）。返回整杯评价。</summary>
        public DrinkReview Serve()
        {
            if (served) return Review();
            served = true;
            var review = Review();
            ServedEvent?.Invoke(review);
            return review;
        }

        /// <summary>算评价。不留状态，出杯前后都能调。</summary>
        public DrinkReview Review()
        {
            int judged = Recipe.JudgedCount;
            int errors = 0;
            var missing = new List<string>();

            for (int i = 0; i < outcomes.Length; i++)
            {
                var step = Recipe.steps[i];
                if (step == null) continue;
                if (step.judged && outcomes[i] == StepOutcome.Off) errors++;
                bool done = outcomes[i] == StepOutcome.Good || outcomes[i] == StepOutcome.Off;
                if (!done && step.IsRequired) missing.Add(step.id);
            }

            float rate = judged == 0 ? 0f : (float)errors / judged;
            var r = new DrinkReview
            {
                JudgedSteps = judged,
                ErrorSteps = errors,
                ErrorRate = rate,
                MissingRequired = missing.Count > 0,
                MissingStepIds = missing.ToArray(),
            };

            if (r.MissingRequired) r.Quality = DrinkQuality.Terrible;
            else if (errors == 0) r.Quality = DrinkQuality.Good;
            else if (rate <= Recipe.FairMax + 1e-4f) r.Quality = DrinkQuality.Fair;
            else if (rate < Recipe.PoorMax - 1e-4f) r.Quality = DrinkQuality.Poor;
            else r.Quality = DrinkQuality.Terrible;

            return r;
        }

        // ------------------------------------------------------------------ 内部

        MakeAction Guard(MakePrimitive expected)
        {
            if (served) return MakeAction.Served;
            if (PendingRetry) return MakeAction.Invalid;    // 先决定重做还是继续
            var step = Current;
            if (step == null) return MakeAction.Invalid;
            if (step.Primitive != expected) return MakeAction.NotCurrent;
            if (!GateOpen(step)) return MakeAction.Blocked;
            return MakeAction.Ok;
        }

        /// <summary>门槛只看"前一步做完了没有"：做完（好或带偏差）就放行；跳过不算做完。</summary>
        bool GateOpen(RecipeStep step)
        {
            if (string.IsNullOrWhiteSpace(step.gate)) return true;
            var o = OutcomeOf(step.gate);
            return o == StepOutcome.Good || o == StepOutcome.Off;
        }

        MakeAction Complete(StepOutcome outcome)
        {
            var step = Current;
            outcomes[index] = outcome;
            var snapshot = new StepResult(step, outcome);
            progress = 0f;

            if (outcome == StepOutcome.Off)
            {
                // 判偏了先别走，等玩家决定（7.9.4）
                PendingRetry = true;
            }
            else
            {
                Advance();
            }
            StepFinished?.Invoke(snapshot);
            return MakeAction.Ok;
        }

        void Advance()
        {
            index++;
            progress = 0f;
            PendingRetry = false;
            Results = outcomes.Select((o, i) => new StepResult(Recipe.steps[i], o)).ToArray();
        }

        public string Describe()
        {
            var c = Current;
            return $"make {Recipe.sku} 第{index + 1}/{Recipe.steps.Length}步" +
                   (c != null ? $"[{c.id}]" : "[完毕]") +
                   $" 进度={Progress01:P0}" + (PendingRetry ? " 待决定(重做/继续)" : "") +
                   (served ? " 已出杯" : "");
        }
    }
}

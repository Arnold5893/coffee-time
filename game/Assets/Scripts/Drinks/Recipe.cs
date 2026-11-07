using System;
using System.Collections.Generic;
using System.Linq;

namespace CoffeeShop.Drinks
{
    /// <summary>
    /// 五种操作原语（FR-MAKE-03）。PC 与移动端只是输入方式不同，落到领域层都是这五件事：
    ///   按住（磨豆、加奶）/ 点击（压粉、取杯）/ 选择（选奶、选风味）/ 放置（放杯、上托盘）/ 等待（萃取、浸泡）。
    /// </summary>
    public enum MakePrimitive
    {
        Click = 0,
        Hold = 1,
        Select = 2,
        Place = 3,
        Wait = 4,
    }

    /// <summary>配方里的一步。字段含义按原语解释，见 recipes.json 的 note。</summary>
    [Serializable]
    public class RecipeStep
    {
        public string id;
        public string name;
        public string primitive;     // click / hold / select / place / wait
        public bool judged;          // 参与错误率统计（等待型一律 false）
        public bool optional;        // 缺省 false = 必需，缺了直接判非常差
        public float param;          // hold=达标秒数，wait=等待秒数，click=按下达标秒数（0=点一下）
        public string[] options;     // select 的候选
        public string correct;       // select 正确项 / place 正确目标
        public string gate;          // 系统门槛：这个步骤没做完，本步操作一律被拒（FR-EVAL-04）
        public float cost;           // 自动驱动（老板）耗时秒数

        public MakePrimitive Primitive => ParsePrimitive(primitive);

        public bool IsRequired => !optional;

        public static MakePrimitive ParsePrimitive(string raw)
        {
            switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "hold": return MakePrimitive.Hold;
                case "select": return MakePrimitive.Select;
                case "place": return MakePrimitive.Place;
                case "wait": return MakePrimitive.Wait;
                case "click":
                default: return MakePrimitive.Click;
            }
        }

        public override string ToString() => $"{id}({primitive}{(judged ? ",判" : "") }{ (IsRequired ? "" : ",可选")})";
    }

    /// <summary>一杯饮品的配方。手动与自动两种驱动读的是同一个对象（FR-MAKE-02）。</summary>
    [Serializable]
    public class Recipe
    {
        public string sku;
        public string name;
        public string line;
        public RecipeStep[] steps;

        /// <summary>错误率阈值覆盖（FR-EVAL-02 "可按饮品单独覆盖"）。0 表示用默认 0.5 / 1.0。</summary>
        public float fairMax;
        public float poorMax;

        public int JudgedCount => steps == null ? 0 : steps.Count(s => s.judged);

        public float FairMax => fairMax > 0f ? fairMax : 0.5f;
        public float PoorMax => poorMax > 0f ? poorMax : 1.0f;

        /// <summary>整杯自动做完要多久（老板队列的计时依据）。</summary>
        public float AutoSeconds => steps == null ? 0f : steps.Sum(s => s.cost > 0.01f ? s.cost : 1f);

        public RecipeStep Find(string stepId) =>
            steps == null ? null : steps.FirstOrDefault(s => string.Equals(s.id, stepId, StringComparison.Ordinal));

        public override string ToString() => $"{sku}({name}) {steps?.Length ?? 0}步/判定{JudgedCount}";
    }

    [Serializable]
    public class RecipeFile
    {
        public int version;
        public string note;
        public Recipe[] recipes;
    }

    /// <summary>配方读取与校验。纯函数，不碰场景。</summary>
    public static class RecipeBook
    {
        public const string ResourcePath = "Config/recipes";
        public const string AssetRelativePath = "Resources/Config/recipes.json";

        public static RecipeFile LoadFromResources()
        {
            var ta = UnityEngine.Resources.Load<UnityEngine.TextAsset>(ResourcePath);
            if (ta == null) throw new InvalidOperationException($"缺少配方配置资源: {ResourcePath}");
            return Parse(ta.text);
        }

        public static RecipeFile Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("配方配置为空", nameof(json));
            var file = UnityEngine.JsonUtility.FromJson<RecipeFile>(json);
            if (file == null || file.recipes == null)
                throw new ArgumentException("配方配置里没有 recipes 数组", nameof(json));
            return file;
        }

        public static Recipe Find(RecipeFile file, string sku)
        {
            if (file?.recipes == null) return null;
            return file.recipes.FirstOrDefault(r => string.Equals(r.sku, sku, StringComparison.Ordinal));
        }

        /// <summary>返回配置问题清单；空列表代表合法。</summary>
        public static List<string> Validate(RecipeFile file)
        {
            var errors = new List<string>();
            if (file == null || file.recipes == null)
            {
                errors.Add("配置为空或没有 recipes 数组");
                return errors;
            }

            var skus = new HashSet<string>();
            for (int i = 0; i < file.recipes.Length; i++)
            {
                var r = file.recipes[i];
                string who = $"recipes[{i}]";
                if (r == null) { errors.Add($"{who} 为 null"); continue; }
                if (string.IsNullOrWhiteSpace(r.sku)) errors.Add($"{who} 缺少 sku");
                else if (!skus.Add(r.sku)) errors.Add($"{who} sku 重复: {r.sku}");
                if (string.IsNullOrWhiteSpace(r.name)) errors.Add($"{who} 缺少 name");
                if (r.steps == null || r.steps.Length < 6 || r.steps.Length > 9)
                    errors.Add($"{who} 步骤数应在 6–9 之间（FR-MAKE-05），实际 {r.steps?.Length ?? 0}");
                if (r.steps == null) continue;

                var ids = new HashSet<string>();
                foreach (var s in r.steps)
                {
                    if (s == null) { errors.Add($"{who} 有 null 步骤"); continue; }
                    if (string.IsNullOrWhiteSpace(s.id)) errors.Add($"{who} 有步骤缺少 id");
                    else if (!ids.Add(s.id)) errors.Add($"{who} 步骤 id 重复: {s.id}");

                    if (s.Primitive == MakePrimitive.Select)
                    {
                        if (s.options == null || s.options.Length < 2)
                            errors.Add($"{who}.{s.id} select 至少要两个候选");
                        if (s.options != null && !s.options.Contains(s.correct))
                            errors.Add($"{who}.{s.id} correct 不在 options 里");
                    }
                    if (s.Primitive == MakePrimitive.Select || s.Primitive == MakePrimitive.Place)
                        if (string.IsNullOrWhiteSpace(s.correct)) errors.Add($"{who}.{s.id} 缺少 correct");

                    if ((s.Primitive == MakePrimitive.Hold || s.Primitive == MakePrimitive.Wait) && s.param <= 0f)
                        errors.Add($"{who}.{s.id} {s.Primitive} 的 param 必须 > 0");
                    if (s.judged && s.Primitive == MakePrimitive.Wait)
                        errors.Add($"{who}.{s.id} 等待型步骤不能有判定（FR-EVAL-01）");
                }

                // gate 必须指向本配方里更靠前的步骤，否则就是写错或者成环
                for (int k = 0; k < r.steps.Length; k++)
                {
                    var s = r.steps[k];
                    if (s == null || string.IsNullOrWhiteSpace(s.gate)) continue;
                    int at = Array.FindIndex(r.steps, x => x != null && x.id == s.gate);
                    if (at < 0) errors.Add($"{who}.{s.id} gate 指向了不存在的步骤: {s.gate}");
                    else if (at >= k) errors.Add($"{who}.{s.id} gate 必须在它之前: {s.gate}");
                }

                if (r.JudgedCount == 0) errors.Add($"{who} 一个判定步都没有");
            }
            return errors;
        }
    }
}

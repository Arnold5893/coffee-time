using CoffeeShop.Cameras;
using CoffeeShop.Controls;
using CoffeeShop.Drinks;
using CoffeeShop.Player;
using UnityEngine;

namespace CoffeeShop.Bar
{
    /// <summary>
    /// 吧台工位（07 票）。
    ///
    /// 玩家站进吧台内侧通道的工位范围 → 底部提示「左键开始制作」→ 按交互 →
    /// 镜头切到原地近景、界面打开、走路锁住；按 Esc / 点「退出」/ 出杯后自动收起 → 恢复。
    ///
    /// 镜头不是切到另一个固定机位：还是同一台跟随相机，只是把距离、俯角、FOV 平滑收到近景参数
    /// （见 <see cref="CameraFocusState"/>），过渡 0.9 秒，远低于 3 秒上限。
    /// </summary>
    [DefaultExecutionOrder(70)]
    public class BarStation : MonoBehaviour
    {
        [Header("接线")]
        public PlayerMotor motor;
        public PlayerInputRouter router;
        public ThirdPersonCameraRig camRig;
        public CraftPanel panel;

        [Header("工位")]
        [Tooltip("工位范围中心（世界坐标），由构建脚本按柜台与咖啡机包围盒推出来")]
        public Vector3 center;
        [Tooltip("工位范围尺寸（米）：沿吧台 2m、垂直通道 1.15m 抠掉两侧柜台")]
        public Vector3 size = new Vector3(2.0f, 1.9f, 1.15f);
        [Tooltip("近景注视点：咖啡机台面")]
        public Vector3 workPoint;
        [Tooltip("默认做哪款（配方 sku）")]
        public string sku = "classic-latte";
        [Tooltip("台面工作区（道具摆放的基准点），由构建脚本按后柜包围盒推出来")]
        public Vector3 counterAnchor;

        bool active;
        float activeTime;
        Recipe recipe;

        public bool Active => active;
        public float ActiveTime => activeTime;
        public int EnterCount { get; private set; }
        public int ExitCount { get; private set; }
        public int RejectedEnters { get; private set; }
        public string RecipeName => recipe != null ? recipe.name : "";

        public bool InZone(Vector3 worldPos)
        {
            Vector3 d = worldPos - center;
            return Mathf.Abs(d.x) <= size.x * 0.5f && Mathf.Abs(d.z) <= size.z * 0.5f;
        }

        /// <summary>工位中心落到地面（传送/站位判定用，别拿 center 的 y 直接传送）。</summary>
        public Vector3 GroundPoint => new Vector3(center.x, 0.06f, center.z);

        void Awake()
        {
            // 直接操作层（07 返工）：道具与手势都挂在工位上，随进入/退出启停
            if (panel != null && panel.view == null)
            {
                var viewGO = new GameObject("CraftView");
                viewGO.transform.SetParent(transform, false);
                var view = viewGO.AddComponent<CraftView>();
                view.anchor = counterAnchor;
                panel.view = view;
            }

            recipe = LoadRecipe(sku);
            if (recipe == null) Debug.LogError($"[bar] 找不到配方 {sku}，工位无法开工");
        }

        void Update()
        {
            if (panel == null || motor == null || router == null) return;

            if (!active)
            {
                bool near = InZone(motor.transform.position);
                panel.ShowHint(near, near ? $"按左键开始制作 · {RecipeName}" : "");
                if (near && router.Intent.Interact) Enter();
                return;
            }

            activeTime += Time.deltaTime;
            if (router.Intent.Cancel || panel.CloseRequested) Exit();
        }

        /// <summary>进入制作：开界面、锁走路、切近景。</summary>
        public bool Enter()
        {
            if (active) return false;

            recipe = recipe ?? LoadRecipe(sku);
            if (recipe == null || panel == null) { RejectedEnters++; return false; }

            active = true;
            activeTime = 0f;
            EnterCount++;

            if (panel.view != null) panel.view.anchor = counterAnchor;
            panel.Open(new CraftSession(recipe));
            if (motor != null) motor.enabled = false;
            if (router != null) router.SetUiMode(true);
            if (camRig != null)
            {
                camRig.FocusOn(workPoint);
                // 机位方向由工位定死：相机站在工位中心那一侧（通道里）正对柜台。
                //     不这么钉的话，相机是"猜"出来的侧肩机位，柜台只有 0.65m 深，横排的道具会叠成一条。
                Vector3 side = center - workPoint;
                side.y = 0f;
                if (side.sqrMagnitude > 1e-4f) camRig.AimFrom(side.normalized);
            }
            return true;
        }

        /// <summary>退出制作：关界面、恢复走路与跟随镜头。</summary>
        public void Exit()
        {
            if (!active) return;
            active = false;
            ExitCount++;

            if (panel != null) panel.Close();
            if (motor != null) motor.enabled = true;
            if (router != null) router.SetUiMode(false);
            if (camRig != null) camRig.ClearFocus();
        }

        static Recipe LoadRecipe(string sku)
        {
            try
            {
                var file = RecipeBook.LoadFromResources();
                var errors = RecipeBook.Validate(file);
                foreach (var e in errors) Debug.LogWarning("[bar] 配方配置问题: " + e);
                return RecipeBook.Find(file, sku) ?? (file.recipes != null && file.recipes.Length > 0 ? file.recipes[0] : null);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[bar] 读取配方失败: " + e.Message);
                return null;
            }
        }
    }
}

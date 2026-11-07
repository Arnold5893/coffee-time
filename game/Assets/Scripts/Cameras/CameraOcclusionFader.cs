using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoffeeShop.Cameras
{
    /// <summary>
    /// 遮挡淡化（FR-MOV-03）：镜头与角色之间被家具/墙体挡住时，把挡住的物体临时换成半透明材质，
    /// 松开后还原。做法是为每个原材质缓存一份 URP Lit 透明克隆，只替换 Renderer 的材质引用，
    /// 不改共享资产，退出 Play 后场景不受影响。
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class CameraOcclusionFader : MonoBehaviour
    {
        [SerializeField] Transform cameraTransform;
        [SerializeField] Transform target;
        [Tooltip("射线半径。宁可细一点：镜头是高俯角，粗射线会把脚边的桌面也判成遮挡，整屏发灰")]
        [SerializeField] float probeRadius = 0.09f;
        [Tooltip("淡化后的不透明度")]
        [Range(0.05f, 0.95f)]
        [SerializeField] float fadedAlpha = 0.32f;
        [SerializeField] LayerMask occluderMask = ~0;

        sealed class Faded
        {
            public Renderer renderer;
            public Material[] original;
        }

        readonly Dictionary<Renderer, Faded> active = new Dictionary<Renderer, Faded>();
        readonly Dictionary<Material, Material> cloneCache = new Dictionary<Material, Material>();
        static readonly RaycastHit[] hits = new RaycastHit[32];

        void Awake()
        {
            // 门外地台围挡是没有渲染器的逻辑碰撞体，不参与遮挡淡化判定
            occluderMask = occluderMask.value & ~LayerMask.GetMask("Boundary");
        }

        public float FadedAlpha
        {
            get => fadedAlpha;
            set => fadedAlpha = Mathf.Clamp(value, 0.05f, 0.9f);
        }

        public void Bind(Transform cam, Transform tgt)
        {
            cameraTransform = cam;
            target = tgt;
        }

        void LateUpdate()
        {
            if (cameraTransform == null || target == null) return;

            var want = new HashSet<Renderer>();
            // 只看到胸口：往脚下打会把贴地的桌面/椅面全判成遮挡
            Vector3 from = cameraTransform.position;
            Vector3 to = target.position + Vector3.up * 1.05f;
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist > 1e-3f)
            {
                dir /= dist;
                int n = Physics.SphereCastNonAlloc(from, probeRadius, dir, hits, dist, occluderMask,
                                                   QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var r = hits[i].collider != null ? hits[i].collider.GetComponentInParent<Renderer>() : null;
                    if (r == null) continue;
                    if (r.transform.IsChildOf(target)) continue;   // 不淡化角色自己
                    // 不淡化座椅：人坐上去时椅子就在身体位置上，淡化了像穿模
                    if (r.GetComponentInParent<CoffeeShop.Seating.SeatFurniture>() != null) continue;
                    want.Add(r);
                }
            }

            // 新进入的：换上透明克隆
            foreach (var r in want)
            {
                if (active.ContainsKey(r)) continue;
                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;
                var clones = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                    clones[i] = GetClone(mats[i]);
                if (System.Array.TrueForAll(clones, c => c == null)) continue;
                active[r] = new Faded { renderer = r, original = mats };
                r.sharedMaterials = clones;
            }

            // 已经不再挡住的：还原
            var release = new List<Renderer>();
            foreach (var kv in active)
            {
                if (!want.Contains(kv.Key)) release.Add(kv.Key);
            }
            foreach (var r in release)
            {
                r.sharedMaterials = active[r].original;
                active.Remove(r);
            }
        }

        Material GetClone(Material src)
        {
            if (src == null) return null;
            if (cloneCache.TryGetValue(src, out var c) && c != null) return c;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;

            var m = new Material(shader) { name = src.name + " (faded)" };
            if (src.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
            if (src.HasProperty("_BaseColor"))
            {
                var col = src.GetColor("_BaseColor");
                col.a = fadedAlpha;
                m.SetColor("_BaseColor", col);
            }
            if (src.HasProperty("_Metallic")) m.SetFloat("_Metallic", src.GetFloat("_Metallic"));
            if (src.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", src.GetFloat("_Smoothness") * 0.5f);

            // 切成 URP 的 Transparent 表面类型
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);

            cloneCache[src] = m;
            return m;
        }
    }
}

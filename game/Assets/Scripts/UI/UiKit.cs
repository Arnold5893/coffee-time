using UnityEngine;
using UnityEngine.UI;

namespace CoffeeShop.UI
{
    /// <summary>
    /// 运行时搭 uGUI 的小工具（07 票起用）。场景里没有预制体，界面全部代码生成，
    /// 场景文件保持干净；12 票换成美术资源时只要替换这里的生成体。
    /// </summary>
    public static class UiKit
    {
        static Font cachedFont;

        /// <summary>
        /// 取一个能显示中文的字体：内置 LegacyRuntime.ttf 只有拉丁字形，中文会整段空白。
        /// </summary>
        public static Font CjkFont(int size = 32)
        {
            if (cachedFont != null) return cachedFont;
            string[] prefer =
            {
                "Microsoft YaHei", "微软雅黑", "PingFang SC", "Hiragino Sans GB",
                "Noto Sans CJK SC", "Source Han Sans SC", "SimHei", "Arial Unicode MS"
            };
            cachedFont = Font.CreateDynamicFontFromOSFont(prefer, size);
            if (cachedFont == null) cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return cachedFont;
        }

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>按锚点摆一块：anchor 用 (0,0)=左下 (0.5,0.5)=中心 (1,1)=右上。</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot,
                                          Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Box(string name, Transform parent, Color color)
        {
            var rt = Node(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(string name, Transform parent, int size, TextAnchor align, Color color)
        {
            var rt = Node(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = CjkFont(size);
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        static Sprite solid;

        /// <summary>1x1 纯白贴图：配合 Image.Type.Filled 做进度条，不依赖 RectTransform 的宽高。</summary>
        public static Sprite Solid()
        {
            if (solid != null) return solid;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            tex.name = "UiSolid";
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            solid = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            return solid;
        }

        /// <summary>圆角按钮的底：一张程序生成的圆点贴图，省一个美术资源。</summary>
        public static Sprite Circle(int size = 128)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "UiCircle";
            tex.wrapMode = TextureWrapMode.Clamp;
            float r = size * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r, dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d);          // 1px 软边，避免锯齿
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>一个带文字的按钮（矩形）。</summary>
        public static Button TextButton(string name, Transform parent, string caption, int fontSize,
                                        Vector2 size, Color bg, Color fg)
        {
            var img = Box(name, parent, bg);
            img.rectTransform.sizeDelta = size;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            var label = Label(name + "_Label", img.transform, fontSize, TextAnchor.MiddleCenter, fg);
            label.text = caption;
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            btn.colors = colors;
            return btn;
        }

        public static void SetText(Text t, string value)
        {
            if (t != null && t.text != value) t.text = value;
        }
    }
}

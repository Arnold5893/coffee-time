using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoffeeShop.Seating
{
    /// <summary>座椅类型。只影响坐下姿态与默认座面高度，不影响逻辑。</summary>
    public enum SeatKind
    {
        BarStool = 0,
        Armchair = 1,
        Sofa = 2,
    }

    /// <summary>
    /// 座位配置项（FR-SEAT-01）。字段刻意只描述"语义"，不含世界坐标：
    /// 位置、朝向、坐下点、桌面点位都在运行时由 SeatingManager 从场景几何解析，
    /// 这样家具在 build_shop.py 里微调后，配置不用跟着改。
    /// </summary>
    [Serializable]
    public class SeatEntry
    {
        public string id;              // 唯一编号
        public string zone;            // 区域：bar / sun / round / meeting
        public string kind;            // 座椅类型，见 SeatConfig.ParseKind
        public string anchor;          // 场景里座椅对象的名字（必填）
        public string faceTarget;      // 朝向参照物：朝它最近的一面
        public string faceDir;         // 朝向兜底：N/S/E/W/NE/NW/SE/SW（faceTarget 优先）
        public float seatHeight;       // 座面高度（米）；0 表示按 kind 取默认值
        public float surfaceForward = 0.50f; // 桌面探测点：沿朝向前方多少米打向下射线
        public float surfaceHeight;          // 显式指定桌面高度（给了就不打射线）；0 = 打射线探测
        public float standOffset;      // 起身后站位在座位后方多少米；0 = 默认
    }

    [Serializable]
    public class SeatConfigFile
    {
        public int version;
        public string note;
        public SeatEntry[] seats;
    }

    /// <summary>配置读取与取值规则。纯函数，不碰场景，Edit Mode 测试可直接跑。</summary>
    public static class SeatConfig
    {
        public const string ResourcePath = "Config/seats";
        public const string AssetRelativePath = "Resources/Config/seats.json";

        public static SeatConfigFile LoadFromResources()
        {
            var ta = Resources.Load<TextAsset>(ResourcePath);
            if (ta == null) throw new InvalidOperationException($"缺少座位配置资源: {ResourcePath}");
            return Parse(ta.text);
        }

        public static SeatConfigFile Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("座位配置为空", nameof(json));
            var file = JsonUtility.FromJson<SeatConfigFile>(json);
            if (file == null || file.seats == null)
                throw new ArgumentException("座位配置里没有 seats 数组", nameof(json));
            return file;
        }

        /// <summary>返回配置里的问题清单；空列表代表合法。</summary>
        public static List<string> Validate(SeatConfigFile file)
        {
            var errors = new List<string>();
            if (file == null || file.seats == null)
            {
                errors.Add("配置为空或没有 seats 数组");
                return errors;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < file.seats.Length; i++)
            {
                var e = file.seats[i];
                string who = $"seats[{i}]";
                if (e == null) { errors.Add($"{who} 为 null"); continue; }
                if (string.IsNullOrWhiteSpace(e.id)) errors.Add($"{who} 缺少 id");
                else if (!seen.Add(e.id)) errors.Add($"{who} id 重复: {e.id}");
                if (string.IsNullOrWhiteSpace(e.anchor)) errors.Add($"{who} 缺少 anchor");
                if (string.IsNullOrWhiteSpace(e.zone)) errors.Add($"{who} 缺少 zone");
                if (!string.IsNullOrWhiteSpace(e.faceDir) && !faceDirs.ContainsKey(e.faceDir.ToUpperInvariant()))
                    errors.Add($"{who} faceDir 无法识别: {e.faceDir}");
                if (e.seatHeight < 0f) errors.Add($"{who} seatHeight 不能为负");
            }
            return errors;
        }

        public static SeatKind ParseKind(string raw)
        {
            switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "barstool":
                case "bar_stool":
                case "bar-stool": return SeatKind.BarStool;
                case "sofa": return SeatKind.Sofa;
                case "armchair":
                case "chair":
                default: return SeatKind.Armchair;
            }
        }

        public static float DefaultSeatHeight(SeatKind kind)
        {
            switch (kind)
            {
                case SeatKind.BarStool: return 0.66f;   // 蘑菇背吧凳座面（build_shop.py: sh = 0.66 if bar）
                case SeatKind.Sofa: return 0.42f;
                default: return 0.45f;                  // 弧形靠背软包椅（SH = 0.45）
            }
        }

        public static float ResolveSeatHeight(SeatEntry e)
        {
            var kind = ParseKind(e.kind);
            return e.seatHeight > 0.01f ? e.seatHeight : DefaultSeatHeight(kind);
        }

        static readonly Dictionary<string, Vector3> faceDirs = new Dictionary<string, Vector3>
        {
            { "N",  new Vector3(0f, 0f, 1f) },
            { "S",  new Vector3(0f, 0f, -1f) },
            { "E",  new Vector3(1f, 0f, 0f) },
            { "W",  new Vector3(-1f, 0f, 0f) },
            { "NE", new Vector3(1f, 0f, 1f).normalized },
            { "NW", new Vector3(-1f, 0f, 1f).normalized },
            { "SE", new Vector3(1f, 0f, -1f).normalized },
            { "SW", new Vector3(-1f, 0f, -1f).normalized },
        };

        /// <summary>把 "NE" 这类方向名转成世界水平方向；不认识就返回 Vector3.zero。</summary>
        public static Vector3 ParseFaceDir(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Vector3.zero;
            return faceDirs.TryGetValue(raw.Trim().ToUpperInvariant(), out var v) ? v : Vector3.zero;
        }
    }
}

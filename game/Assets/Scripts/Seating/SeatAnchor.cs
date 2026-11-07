using UnityEngine;

namespace CoffeeShop.Seating
{
    /// <summary>
    /// 一个已经在场景里落地好的座位：配置语义 + 解析出来的世界几何。
    /// 位置/朝向/桌面点都是运行时算出来的，配置里不存坐标。
    /// </summary>
    public class SeatAnchor
    {
        public SeatEntry Entry;
        public SeatState State;
        public Transform Anchor;

        /// <summary>坐下时脚落的位置（世界坐标，y = 地面）。</summary>
        public Vector3 SeatPoint;
        /// <summary>坐下后的朝向（正对桌子 / 窗户）。</summary>
        public Quaternion Facing = Quaternion.identity;
        /// <summary>起身后站的位置（座位后方）。</summary>
        public Vector3 StandPoint;
        /// <summary>桌面点位：放杯子的地方（世界坐标，已含桌面高度）。</summary>
        public Vector3 SurfacePoint;
        /// <summary>座面高度，用于坐下姿态下沉量。</summary>
        public float SeatHeight = 0.45f;

        public string Id => State != null ? State.Id : Entry != null ? Entry.id : "?";
        public string Zone => State != null ? State.Zone : Entry != null ? Entry.zone : "?";
        public bool IsFree => State == null || State.IsFree;

        public override string ToString() =>
            $"{Id}({Zone}) seat=({SeatPoint.x:F2},{SeatPoint.z:F2}) " +
            $"face=({Facing * Vector3.forward}) surface=({SurfacePoint.x:F2},{SurfacePoint.y:F2},{SurfacePoint.z:F2})";
    }

    /// <summary>
    /// 挂在座椅家具的根节点上，只做一件事：告诉遮挡淡化"这是座位，别把我变透明"。
    /// 人坐上去时椅子本来就在身体位置上，淡化了就会像穿进身体里。
    /// </summary>
    public class SeatFurniture : MonoBehaviour
    {
    }
}

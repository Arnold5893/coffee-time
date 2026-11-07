using UnityEngine;

namespace CoffeeShop.Controls
{
    /// <summary>
    /// 操作请求：输入层每帧产出的"意图"，本身不持有任何游戏状态。
    /// Move     相机相对的二维方向（x 右、y 前），已归一到单位圆内；
    /// Look     本帧累计的视角旋转增量（度，x 偏航、y 俯仰）；
    /// Interact 本帧是否按下了"交互"（PC 左键 / 移动端点触），边沿触发；
    /// Cancel   本帧是否按了"取消/退出"（PC Esc / 移动端返回），边沿触发。
    /// </summary>
    public struct PlayerIntent
    {
        public Vector2 Move;
        public Vector2 Look;
        public bool Interact;
        public bool Cancel;

        public static readonly PlayerIntent None = default;

        public bool HasMove => Move.sqrMagnitude > 1e-4f;
        public bool HasLook => Look.sqrMagnitude > 1e-4f;
    }

    /// <summary>输入源：PC 键鼠、移动端摇杆都实现这一个接口，上层不感知设备。</summary>
    public interface IPlayerInputSource
    {
        PlayerIntent Read();
    }
}

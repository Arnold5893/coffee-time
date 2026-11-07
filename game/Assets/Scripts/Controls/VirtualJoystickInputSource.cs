using UnityEngine;

namespace CoffeeShop.Controls
{
    /// <summary>
    /// 移动端预留输入源。将来接摇杆 UI 时，只需把摇杆输出写进 SetMove / AddLook，
    /// 角色与镜头代码一行都不用改（02 票"输入层预留移动端摇杆接口"）。
    /// </summary>
    public class VirtualJoystickInputSource : IPlayerInputSource
    {
        Vector2 move;
        Vector2 look;
        bool interact;
        bool cancel;

        public void SetMove(Vector2 v) => move = v.sqrMagnitude > 1f ? v.normalized : v;
        public void AddLook(Vector2 deltaDegrees) => look += deltaDegrees;
        /// <summary>模拟一次点按交互（开门 / 点家具）。读完即消费。</summary>
        public void PressInteract() => interact = true;
        /// <summary>模拟一次"取消/退出"（关掉制作界面）。读完即消费。</summary>
        public void PressCancel() => cancel = true;
        public void Clear() { move = Vector2.zero; look = Vector2.zero; interact = false; cancel = false; }

        public PlayerIntent Read()
        {
            var intent = new PlayerIntent { Move = move, Look = look, Interact = interact, Cancel = cancel };
            look = Vector2.zero;      // 增量读完即清零
            interact = false;         // 边沿触发
            cancel = false;
            return intent;
        }
    }
}

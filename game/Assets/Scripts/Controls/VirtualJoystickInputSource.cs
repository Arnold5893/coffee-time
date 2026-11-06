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

        public void SetMove(Vector2 v) => move = v.sqrMagnitude > 1f ? v.normalized : v;
        public void AddLook(Vector2 deltaDegrees) => look += deltaDegrees;
        public void Clear() { move = Vector2.zero; look = Vector2.zero; }

        public PlayerIntent Read()
        {
            var intent = new PlayerIntent { Move = move, Look = look };
            look = Vector2.zero;   // 增量读完即清零
            return intent;
        }
    }
}

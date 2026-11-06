using UnityEngine;

namespace CoffeeShop.Controls
{
    /// <summary>
    /// PC 键盘 + 鼠标。
    /// 只有走路：没有跑步 / 跳跃 / 蹲下 / 传送的键位（FR-MOV-02）。
    /// 鼠标左键锁光标、Esc 解锁；锁定或按住右键时鼠标才旋转镜头。
    /// </summary>
    public class KeyboardMouseInputSource : IPlayerInputSource
    {
        public float LookSensitivity = 2.4f;   // 度 / 鼠标像素
        public bool InvertY = false;

        public PlayerIntent Read()
        {
            var move = new Vector2(
                UnityEngine.Input.GetAxisRaw("Horizontal"),
                UnityEngine.Input.GetAxisRaw("Vertical"));
            if (move.sqrMagnitude > 1f) move.Normalize();

            HandleCursorLock();

            if (UnityEngine.Input.GetKeyDown(KeyCode.LeftAlt))
                InvertY = !InvertY;

            bool rotating = Cursor.lockState == CursorLockMode.Locked ||
                            UnityEngine.Input.GetMouseButton(1);
            var look = Vector2.zero;
            if (rotating)
            {
                look = new Vector2(
                    UnityEngine.Input.GetAxisRaw("Mouse X") * LookSensitivity,
                    UnityEngine.Input.GetAxisRaw("Mouse Y") * LookSensitivity * (InvertY ? 1f : -1f));
            }

            // 左键交互：光标已锁定时是"点一下"；未锁定时这一次点击同时用于锁定光标，
            // 所以也当交互发出去（否则玩家要点两下才能开门）。右键旋转视角，不参与交互。
            bool interact = UnityEngine.Input.GetMouseButtonDown(0);

            return new PlayerIntent { Move = move, Look = look, Interact = interact };
        }

        static void HandleCursorLock()
        {
            if (UnityEngine.Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) && Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }
}

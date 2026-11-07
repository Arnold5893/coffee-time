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

        /// <summary>
        /// 界面模式（07 票）：制作 UI 打开期间光标必须可见、可点。
        /// 这时不锁光标、也不发 Interact——否则点按钮会顺带触发一次世界交互。
        /// 视野仍可用右键微调：近景机位是"非固定机位"，玩家还能转视角看台面。
        /// </summary>
        public bool UiMode;

        public PlayerIntent Read()
        {
            bool cancel = UnityEngine.Input.GetKeyDown(KeyCode.Escape);

            if (UiMode)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }

                var lookUi = Vector2.zero;
                if (UnityEngine.Input.GetMouseButton(1))
                {
                    lookUi = new Vector2(
                        UnityEngine.Input.GetAxisRaw("Mouse X") * LookSensitivity,
                        UnityEngine.Input.GetAxisRaw("Mouse Y") * LookSensitivity * (InvertY ? 1f : -1f));
                }
                return new PlayerIntent { Look = lookUi, Cancel = cancel };
            }

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

            return new PlayerIntent { Move = move, Look = look, Interact = interact, Cancel = cancel };
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

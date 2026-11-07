using UnityEngine;

namespace CoffeeShop.Controls
{
    /// <summary>
    /// 输入路由：持有当前设备，把本帧意图暴露给消费者（角色 / 镜头）。
    /// 默认执行顺序靠前，保证同一帧内消费者读到的是本帧的意图。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class PlayerInputRouter : MonoBehaviour
    {
        public enum Device { KeyboardMouse, VirtualJoystick }

        [SerializeField] Device device = Device.KeyboardMouse;
        [SerializeField] bool suppressed;
        bool uiMode;

        public Device CurrentDevice => device;
        public VirtualJoystickInputSource Virtual { get; } = new VirtualJoystickInputSource();
        public PlayerIntent Intent { get; private set; } = PlayerIntent.None;

        IPlayerInputSource active;

        void Awake() => Apply();

        public void SetDevice(Device d)
        {
            device = d;
            Apply();
        }

        /// <summary>自动化 / 过场用：暂停真实设备读取，只保留外部写入的虚拟输入。</summary>
        public void SetSuppressed(bool value)
        {
            suppressed = value;
            if (suppressed) Virtual.Clear();
        }

        /// <summary>
        /// 界面模式（07 票）：进入制作 UI 时打开——光标可见可点、不再发世界交互，
        /// 走路输入清零（角色停在工位），视野仍允许右键微调。PC 与移动端语义一致。
        /// </summary>
        public void SetUiMode(bool on)
        {
            uiMode = on;
            if (active is KeyboardMouseInputSource kb) kb.UiMode = on;
            if (on) Virtual.Clear();
        }

        public bool UiMode => uiMode;

        void Apply()
        {
            active = device == Device.KeyboardMouse
                ? (IPlayerInputSource)new KeyboardMouseInputSource()
                : Virtual;
            if (active is KeyboardMouseInputSource kb) kb.UiMode = uiMode;
        }

        void Update()
        {
            // 抑制只针对物理设备（键鼠）；虚拟摇杆是自动化/过场自己写的，照常放行
            if (suppressed && active is KeyboardMouseInputSource)
            {
                Intent = PlayerIntent.None;
                return;
            }
            Intent = active?.Read() ?? PlayerIntent.None;
        }
    }
}

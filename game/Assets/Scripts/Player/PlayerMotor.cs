using CoffeeShop.Controls;
using UnityEngine;

namespace CoffeeShop.Player
{
    /// <summary>
    /// 角色移动。只走路（FR-MOV-02）：没有跑步 / 跳跃 / 蹲下 / 座位传送。
    /// 位移由 CharacterController 驱动，碰撞来自场景里烘焙好的碰撞体。
    /// 移动方向是相机相对的：镜头转，前进方向跟着转。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] PlayerInputRouter inputRouter;
        [Tooltip("移动参考系，通常是跟随相机；只取它的水平朝向")]
        [SerializeField] Transform moveReference;
        [SerializeField] Transform cameraPivot;

        [Header("移动")]
        [Tooltip("走路速度 m/s（店内只走路，不支持跑步）")]
        [SerializeField] float walkSpeed = 1.5f;
        [Tooltip("转身角速度 度/秒")]
        [SerializeField] float turnSpeed = 720f;
        [SerializeField] float gravity = -18f;

        CharacterController controller;
        float verticalVelocity;
        Vector3 lastPlanarDir = Vector3.forward;

        public int UpdateTicks { get; private set; }
        public bool ControllerEnabled => controller != null && controller.enabled;
        public bool IsGrounded => controller != null && controller.isGrounded;
        public bool IsMoving { get; private set; }
        public float WalkSpeed => walkSpeed;
        public Transform CameraPivot => cameraPivot;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        void Update()
        {
            UpdateTicks++;
            var intent = inputRouter != null ? inputRouter.Intent : PlayerIntent.None;

            Vector3 dir = Vector3.zero;
            if (intent.HasMove)
            {
                Vector3 fwd = PlanarForward();
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                dir = fwd * intent.Move.y + right * intent.Move.x;
                if (dir.sqrMagnitude > 1f) dir.Normalize();
            }

            IsMoving = dir.sqrMagnitude > 1e-4f;
            if (IsMoving)
            {
                lastPlanarDir = dir.normalized;
                var targetRot = Quaternion.LookRotation(lastPlanarDir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, turnSpeed * Time.deltaTime);
            }

            // 重力：不提供跳跃，只保证贴地
            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = dir * walkSpeed + Vector3.up * verticalVelocity;
            controller.Move(velocity * Time.deltaTime);
        }

        Vector3 PlanarForward()
        {
            Transform r = moveReference != null ? moveReference : transform;
            Vector3 f = Vector3.ProjectOnPlane(r.forward, Vector3.up);
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        }

        /// <summary>供自动化 / 后续"座位传送"以外的流程使用：瞬移后同步控制器内部状态。</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            bool was = controller.enabled;
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = was;
            verticalVelocity = 0f;
        }

        public void Bind(PlayerInputRouter router, Transform reference, Transform pivot)
        {
            inputRouter = router;
            moveReference = reference;
            cameraPivot = pivot;
        }
    }
}

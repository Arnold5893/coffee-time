using CoffeeShop.Controls;
using UnityEngine;

namespace CoffeeShop.Cameras
{
    /// <summary>
    /// 第三人称高位斜俯视跟随镜头（FR-MOV-01）。
    /// 可旋转、平滑跟随；**不可缩放**——本类不读滚轮、也不提供任何改变距离的入口，
    /// Distance 只由序列化字段决定。
    /// 与墙体/家具做球投射避让，避免镜头钻进墙里（这不是缩放，是遮挡回避，有最小距离下限）。
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class ThirdPersonCameraRig : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] Transform target;
        [SerializeField] Transform pivot;
        [SerializeField] PlayerInputRouter inputRouter;

        [Header("构图")]
        [Tooltip("跟随距离（固定值，不接受任何缩放输入）")]
        [SerializeField] float distance = 3.6f;
        [Tooltip("初始俯角")]
        [SerializeField] float pitch = 35f;
        [Tooltip("初始水平朝向（度），场景构建时按「面朝店内」写入")]
        [SerializeField] float initialYaw;
        [SerializeField] float minPitch = 20f;
        [SerializeField] float maxPitch = 58f;

        [Header("阻尼")]
        [SerializeField] float positionSmoothTime = 0.10f;
        [SerializeField] float yawSmoothTime = 0.05f;

        [Header("避让")]
        [SerializeField] float probeRadius = 0.22f;
        [Tooltip("避让后允许的最近距离，保证不会钻进角色身体")]
        [SerializeField] float minDistance = 1.0f;
        [SerializeField] LayerMask obstructionMask = ~0;

        float yaw;
        float currentYaw;
        float pitchCurrent;
        Vector3 pivotSmooth;
        Vector3 pivotVelocity;

        public Transform Target => target;
        public float Pitch => pitch;
        public float Distance => distance;
        /// <summary>水平朝向，供角色把移动方向转成相机相对。</summary>
        public Transform MoveReference => transform;

        void Awake()
        {
            if (pivot != null) pivotSmooth = pivot.position;
            pitchCurrent = pitch;
            yaw = initialYaw;
            currentYaw = initialYaw;
            // 门外地台围挡是纯逻辑碰撞体（没有可见几何），镜头不该被它顶住
            obstructionMask = obstructionMask.value & ~LayerMask.GetMask("Boundary");
        }

        void Update()
        {
            var intent = inputRouter != null ? inputRouter.Intent : PlayerIntent.None;
            if (intent.HasLook)
            {
                yaw += intent.Look.x;
                pitchCurrent = Mathf.Clamp(pitchCurrent + intent.Look.y, minPitch, maxPitch);
            }
            yaw = Mathf.Repeat(yaw, 360f);
        }

        void LateUpdate()
        {
            if (pivot == null) return;

            // 跟随点平滑
            pivotSmooth = Vector3.SmoothDamp(pivotSmooth, pivot.position, ref pivotVelocity, positionSmoothTime);
            currentYaw = Mathf.LerpAngle(currentYaw, yaw, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, yawSmoothTime)));

            Quaternion rot = Quaternion.Euler(pitchCurrent, currentYaw, 0f);
            Vector3 back = rot * Vector3.back;
            Vector3 desired = pivotSmooth + back * distance;

            // 遮挡回避：从跟随点朝相机方向探，撞到就往前收，但不近于 minDistance
            Vector3 dir = desired - pivotSmooth;
            float len = dir.magnitude;
            if (len > 1e-4f)
            {
                dir /= len;
                if (Physics.SphereCast(pivotSmooth, probeRadius, dir, out var hit, len, obstructionMask,
                                       QueryTriggerInteraction.Ignore))
                {
                    float allowed = Mathf.Clamp(hit.distance - 0.02f, minDistance, len);
                    desired = pivotSmooth + dir * allowed;
                }
            }

            transform.SetPositionAndRotation(desired, Quaternion.LookRotation(pivotSmooth - desired, Vector3.up));
        }

        /// <summary>批处理截图用：瞬移到位，跳过平滑。</summary>
        public void SnapToTarget()
        {
            if (pivot == null) return;
            pivotSmooth = pivot.position;
            pivotVelocity = Vector3.zero;
            currentYaw = yaw;
            LateUpdate();
        }

        public void SetYawPitch(float newYaw, float newPitch)
        {
            yaw = newYaw;
            pitchCurrent = Mathf.Clamp(newPitch, minPitch, maxPitch);
            SnapToTarget();
        }

        /// <summary>场景构建时调用：写入默认朝向，运行时 Awake 会读回。</summary>
        public void ConfigureInitialView(float yawDeg, float pitchDeg)
        {
            initialYaw = yawDeg;
            pitch = Mathf.Clamp(pitchDeg, minPitch, maxPitch);
        }

        /// <summary>回到默认构图（自动化截图用）。</summary>
        public void RestoreDefaultView()
        {
            yaw = initialYaw;
            pitchCurrent = pitch;
            SnapToTarget();
        }

        public void Bind(Transform tgt, Transform piv, PlayerInputRouter router)
        {
            target = tgt;
            pivot = piv;
            inputRouter = router;
            pivotSmooth = piv != null ? piv.position : Vector3.zero;
        }
    }
}

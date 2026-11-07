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

        [Header("吧台近景（07 票 / FR-MOV-05）")]
        [Tooltip("近景距离：凑到台面前，道具要占住画面 —— 退远了整排器物就只有几十个像素")]
        [SerializeField] float focusDistance = 0.80f;
        [Tooltip("近景俯角：工作区落在柜面（0.95m）上，俯角比看机顶时低得多，否则就是高机位俯拍")]
        [SerializeField] float focusPitch = 54f;
        [Tooltip("近景视野：收窄，画面更集中")]
        [SerializeField] float focusFov = 34f;
        [Tooltip("跟随点朝工作点前移多少米（把人让到画面边缘，台面居中）")]
        [SerializeField] float focusPivotToward = 1.00f;
        [Tooltip("跟随点抬高多少米（工作区在柜面上，抬一点让整排道具落在画面中间）")]
        [SerializeField] float focusPivotRise = 0.06f;
        [Tooltip("切入时长（秒）；FR-MOV-05 要求 ≤3 秒")]
        [SerializeField] float focusInSeconds = CameraFocusState.DefaultInSeconds;
        [Tooltip("退出时长（秒）")]
        [SerializeField] float focusOutSeconds = CameraFocusState.DefaultOutSeconds;

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

        Camera cam;
        CameraFocusState focus;
        Vector3 focusPoint;
        float baseFov = 60f;

        public Transform Target => target;
        public float Pitch => pitch;
        public float Distance => distance;
        /// <summary>水平朝向，供角色把移动方向转成相机相对。</summary>
        public Transform MoveReference => transform;

        /// <summary>近景过渡进度 0–1（验收用）。</summary>
        public float FocusProgress => focus != null ? focus.Progress : 0f;
        public bool InFocus => focus != null && focus.Aimed;
        public Vector3 FocusPoint => focusPoint;
        /// <summary>当前实际使用的距离 / 视野，供验收断言"确实收紧了"。</summary>
        public float CurrentDistance =>
            focus == null ? distance : Mathf.Lerp(distance, focusDistance, focus.Ease);
        public float CurrentFov =>
            cam == null ? baseFov : Mathf.Lerp(baseFov, focusFov, focus == null ? 0f : focus.Ease);

        void Awake()
        {
            if (pivot != null) pivotSmooth = pivot.position;
            pitchCurrent = pitch;
            yaw = initialYaw;
            currentYaw = initialYaw;
            cam = GetComponent<Camera>();
            if (cam != null) baseFov = cam.fieldOfView;
            focus = new CameraFocusState(focusInSeconds, focusOutSeconds);
            // 门外地台围挡是纯逻辑碰撞体（没有可见几何），镜头不该被它顶住
            obstructionMask = obstructionMask.value & ~LayerMask.GetMask("Boundary");
        }

        /// <summary>
        /// 切到"原地近景"：同一台相机换一套构图（更近、更窄、俯角更低），平滑过渡。
        /// 玩家站在吧台内侧通道里，正后方 1.65m 会顶进前柜，所以机位沿通道轴摆到侧面；
        /// 两侧都堵死时才退回正后方。
        /// </summary>
        public void FocusOn(Vector3 workPoint)
        {
            if (focus == null) focus = new CameraFocusState(focusInSeconds, focusOutSeconds);
            focusPoint = workPoint;
            focus.Begin();

            float chosen = ChooseFocusYaw(workPoint);
            if (!float.IsNaN(chosen))
            {
                yaw = chosen;
                currentYaw = chosen;
                pitchCurrent = focusPitch;
            }
        }

        /// <summary>退出近景，回到跟随构图（时长 focusOutSeconds）。</summary>
        public void ClearFocus()
        {
            if (focus != null) focus.End();
        }

        /// <summary>
        /// 指定近景机位落在工作点的哪一侧（世界方向，从工作点指向相机）。
        /// 用于"站在通道里正对柜台"这类由工位确定的构图 —— 别让相机自己猜，
        /// 猜出来的侧肩机位在只有 0.65m 深的柜台上会把整排道具看叠。
        /// </summary>
        public void AimFrom(Vector3 dirFromWork)
        {
            dirFromWork.y = 0f;
            if (dirFromWork.sqrMagnitude < 1e-4f) return;
            float chosen = YawForCameraDir(dirFromWork.normalized);
            yaw = chosen;
            currentYaw = chosen;
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
            if (focus == null) focus = new CameraFocusState(focusInSeconds, focusOutSeconds);
            focus.Tick(Time.deltaTime);
            float ease = focus.Ease;

            // 跟随点平滑
            pivotSmooth = Vector3.SmoothDamp(pivotSmooth, pivot.position, ref pivotVelocity, positionSmoothTime);
            currentYaw = Mathf.LerpAngle(currentYaw, yaw, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, yawSmoothTime)));

            // 近景把跟随点和构图参数一起推向"工作点 + 更近更窄"，过渡量都是同一条曲线
            Vector3 pivotTarget = pivotSmooth;
            if (ease > 0f)
            {
                Vector3 toWork = focusPoint - pivotTarget;
                float d = toWork.magnitude;
                if (d > 1e-3f) pivotTarget += toWork / d * (focusPivotToward * ease);
                pivotTarget.y += focusPivotRise * ease;
            }

            float distEff = Mathf.Lerp(distance, focusDistance, ease);
            float pitchEff = Mathf.Lerp(pitchCurrent, focusPitch, ease);
            Quaternion rot = Quaternion.Euler(pitchEff, currentYaw, 0f);
            Vector3 back = rot * Vector3.back;
            Vector3 desired = pivotTarget + back * distEff;

            // 遮挡回避：从跟随点朝相机方向探，撞到就往前收，但不近于 minDistance
            Vector3 dir = desired - pivotTarget;
            float len = dir.magnitude;
            if (len > 1e-4f)
            {
                dir /= len;
                if (Physics.SphereCast(pivotTarget, probeRadius, dir, out var hit, len, obstructionMask,
                                       QueryTriggerInteraction.Ignore))
                {
                    float floor = Mathf.Min(minDistance, distEff * 0.85f);
                    float allowed = Mathf.Clamp(hit.distance - 0.02f, floor, len);
                    desired = pivotTarget + dir * allowed;
                }
            }

            transform.SetPositionAndRotation(desired, Quaternion.LookRotation(pivotTarget - desired, Vector3.up));

            if (cam == null) cam = GetComponent<Camera>();
            if (cam != null) cam.fieldOfView = Mathf.Lerp(baseFov, focusFov, ease);
        }

        /// <summary>近景机位摆哪一侧：沿"人→工作点"的左右两侧各探一次，选没挡住的那侧。</summary>
        float ChooseFocusYaw(Vector3 workPoint)
        {
            Vector3 from = pivot != null ? pivot.position : transform.position;
            Vector3 toWork = workPoint - from;
            toWork.y = 0f;
            if (toWork.sqrMagnitude < 1e-4f) return float.NaN;
            toWork.Normalize();

            // 相机抬得比工作面高出一大截时，柜台是矮的、挡不住它 —— 直接站到"人这一侧"正着俯看。
            //     吧台近景必须这样：柜台只有 0.65m 深，侧肩视角看过去只剩一条，横向排开的道具全叠在一起。
            //     探不到侧面遮挡也没关系，LateUpdate 里还有一层遮挡回避兜底。
            if (focusDistance * Mathf.Sin(focusPitch * Mathf.Deg2Rad) > 0.75f)
                return YawForCameraDir(-toWork);

            Vector3 left = new Vector3(-toWork.z, 0f, toWork.x);
            for (int k = 0; k < 2; k++)
            {
                Vector3 side = k == 0 ? left : -left;
                if (!CameraSpotBlocked(from, side)) return YawForCameraDir(side);
            }
            return YawForCameraDir(left);   // 两侧都堵，退回其中一侧，靠遮挡回避硬收
        }

        bool CameraSpotBlocked(Vector3 from, Vector3 dir)
        {
            float len = Mathf.Max(0.5f, focusDistance * 0.95f);
            if (!Physics.SphereCast(from, probeRadius, dir, out var hit, len, obstructionMask,
                                    QueryTriggerInteraction.Ignore)) return false;
            return hit.distance < len;
        }

        /// <summary>相机要落在 dir 这一侧 ⇒ 朝向 yaw 满足 -forward = dir。</summary>
        static float YawForCameraDir(Vector3 dir) => Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;

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
            focus?.Snap(false);
            focusPoint = Vector3.zero;
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

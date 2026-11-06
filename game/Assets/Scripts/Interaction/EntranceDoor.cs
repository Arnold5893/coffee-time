using CoffeeShop.Audio;
using CoffeeShop.Controls;
using UnityEngine;

namespace CoffeeShop.Interaction
{
    /// <summary>
    /// 03 票：主入口原木单开门（右下侧门，方案 12.2）。
    ///
    /// 流程：门外点击门 → 门扇向外摆开 ＋ 门铃 → 玩家自己走进店内 →
    ///       门在身后自动关闭。进门后不弹任何引导、箭头或提示（FR-INIT-06）。
    ///
    /// 状态机：Closed → Opening → Open → Closing → Closed
    ///   - 只有 Opening / Closing 期间不接受新的点击，其余状态都可以（重复开关不会打架）
    ///   - Closing 时若发现玩家正站在门洞里，立刻反向转回 Opening，不会把人夹住
    ///
    /// 门外左侧那扇双开玻璃门（Door_locked）在方案里明确「保持关闭」，
    /// 是整块不可开的几何，不挂本脚本。
    /// </summary>
    public class EntranceDoor : MonoBehaviour
    {
        public enum State { Closed, Opening, Open, Closing }

        [Header("门扇（对象原点必须落在铰链轴上，局部 +X 指向自由边）")]
        public Transform leaf;

        [Header("门体")]
        public Vector3 doorCenter;                 // 门洞中心，y = 0
        public Vector3 interiorDir = Vector3.right; // 水平方向，由门外指向店内
        public float halfWidth = 0.75f;            // 门洞半宽，用来判定"站在门洞里"
        public float openingHeight = 2.35f;        // 门洞高度，射线兜底判定用

        [Header("动作")]
        public float openAngle = 92f;              // 向店外摆开（平面图的门弧画在外侧）
        public float openDuration = 0.60f;
        public float closeDuration = 0.90f;
        public float enterDepth = 0.55f;           // 越过门洞多少米算"进店了"
        public float doorwayHalfSpan = 0.40f;      // 门洞前后这段距离内视为"还在门里"

        [Header("接线")]
        public Transform player;
        public Collider[] clickTargets = new Collider[0];
        public AudioSource bellSource;
        [Tooltip("射线长度。第三人称镜头在角色身后 3.6m，太短会够不到门")]
        public float rayRange = 8f;
        [Tooltip("玩家本人要离门多近才能点开（米）")]
        public float playerReach = 2.6f;

        public State Current => state;
        /// <summary>0 = 全关，1 = 全开。给测试与截图用。</summary>
        public float OpenAmount => openAmount;
        public bool HasEntered => hasEntered;
        /// <summary>门铃响过几次（自动化验收用）。</summary>
        public int BellCount => bellCount;

        PlayerInputRouter router;
        State state = State.Closed;
        float openAmount;
        Quaternion closedRot;
        float swingSign = 1f;
        bool hasEntered;
        int bellCount;

        /// <summary>由 ProjectSetup 注入输入路由（玩家与门是同一场景里的两个对象）。</summary>
        public void Bind(PlayerInputRouter inputRouter) => router = inputRouter;

        void Awake()
        {
            if (leaf != null)
            {
                closedRot = leaf.rotation;
                swingSign = SwingSign(leaf);
            }
        }

        void Start()
        {
            Apply();   // 保证初始就是全关姿态
            if (bellSource != null && bellSource.clip == null)
                bellSource.clip = ProceduralSfx.DoorBell();
        }

        /// <summary>
        /// 选一个"往店外摆"的方向：让自由边背离 interiorDir 的那个旋转符号。
        /// 不写死正负号，门换到别的墙上也不会开反。
        /// </summary>
        float SwingSign(Transform lf)
        {
            var r = lf.GetComponentInChildren<Renderer>();
            Vector3 off = r != null ? r.bounds.center - lf.position : Vector3.zero;
            off.y = 0f;
            if (off.sqrMagnitude < 1e-6f) return 1f;
            Vector2 flat = new Vector2(interiorDir.x, interiorDir.z);
            if (flat.sqrMagnitude < 1e-6f) return 1f;
            flat.Normalize();
            float a = Vector3.Dot(Quaternion.AngleAxis(90f, Vector3.up) * off, interiorDir);
            float b = Vector3.Dot(Quaternion.AngleAxis(-90f, Vector3.up) * off, interiorDir);
            return a < b ? 1f : -1f;
        }

        /// <summary>玩家越过了门槛多远（正 = 已在店内）。</summary>
        public float InsideDepth(Vector3 worldPos)
        {
            Vector3 d = worldPos - doorCenter; d.y = 0f;
            Vector3 n = interiorDir; n.y = 0f;
            if (n.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.Dot(d, n.normalized);
        }

        void Update()
        {
            if (router != null && router.Intent.Interact && state != State.Opening && state != State.Closing)
                TryClickOpen();

            Step(Time.deltaTime);
        }

        /// <summary>推进状态机与门扇动画。抽出来给自动化测试用固定步长驱动。</summary>
        public void Step(float dt)
        {
            bool wantOpen = state == State.Opening || state == State.Open;
            float speed = 1f / Mathf.Max(0.01f, wantOpen ? openDuration : closeDuration);
            openAmount = Mathf.MoveTowards(openAmount, wantOpen ? 1f : 0f, speed * dt);
            Apply();

            switch (state)
            {
                case State.Opening:
                    if (openAmount >= 0.999f) state = State.Open;
                    break;

                case State.Open:
                    if (player != null && InsideDepth(player.position) > enterDepth)
                    {
                        hasEntered = true;
                        state = State.Closing;
                    }
                    break;

                case State.Closing:
                    // 玩家还卡在门洞里就先别关，转回开门（避免夹人 / 反复抖）
                    if (player != null && PlayerInDoorway()) { state = State.Opening; break; }
                    if (openAmount <= 0.001f) state = State.Closed;
                    break;
            }
        }

        public bool PlayerInDoorway()
        {
            if (player == null) return false;
            Vector3 d = player.position - doorCenter;
            float depth = InsideDepth(player.position);
            if (Mathf.Abs(depth) > doorwayHalfSpan) return false;
            // 门洞横向范围内才算"在门里"，站在门边墙外侧不算
            Vector3 n = interiorDir; n.y = 0f; n.Normalize();
            Vector3 lateral = Vector3.Cross(Vector3.up, n);
            float side = Vector3.Dot(new Vector3(d.x, 0f, d.z), lateral);
            return Mathf.Abs(side) < halfWidth;
        }

        // ---------------- 交互 ----------------

        void TryClickOpen()
        {
            var cam = Camera.main;
            if (cam == null) return;

            // 光标锁定时从屏幕中心发射线，未锁定时跟着鼠标走（两者都能点）
            Vector3 sp = Cursor.lockState == CursorLockMode.Locked
                ? new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f)
                : (Vector3)Input.mousePosition;
            TryInteractFromCamera(cam, sp);
        }

        /// <summary>
        /// 从一条屏幕射线找门。抽成公开方法，自动化验收可以指定机位直接调，
        /// 不必依赖光标锁定状态。
        /// </summary>
        public bool TryInteractFromCamera(Camera cam, Vector3 screenPos)
        {
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(screenPos);
            // 门外地台围挡不可见，不能挡住点门的那条射线
            int mask = ~LayerMask.GetMask("Boundary");
            bool hitSomething = Physics.Raycast(ray, out var hit, rayRange, mask, QueryTriggerInteraction.Ignore);
            bool onDoor = hitSomething && IsDoorPart(hit.collider);

            // 门扇与固定亮子之间留了几厘米缝，正对缝隙时射线会直接穿过去打到店内地面。
            // 只要门平面的交点在命中点之前，就认为点的是门。
            if (!onDoor)
            {
                float planeDist = DistanceAlongRayToDoor(ray);
                bool planeFirst = planeDist >= 0f && (!hitSomething || hit.distance > planeDist);
                if (planeFirst) onDoor = RayCrossesOpening(ray);
            }
            if (!onDoor) return false;

            // 人得站在门口附近（射线是从镜头发的，不能靠"镜头够得着"来判定）
            if (player != null)
            {
                Vector3 flat = player.position - doorCenter; flat.y = 0f;
                if (flat.magnitude > playerReach) return false;
            }
            return Open();
        }

        float DistanceAlongRayToDoor(Ray ray)
        {
            Vector3 n = interiorDir; n.y = 0f;
            if (n.sqrMagnitude < 1e-6f) return -1f;
            var plane = new Plane(-n.normalized, doorCenter);
            return plane.Raycast(ray, out float enter) ? enter : -1f;
        }

        /// <summary>射线在门洞矩形（净宽 × 门洞高）范围内穿过门平面。</summary>
        bool RayCrossesOpening(Ray ray)
        {
            float enter = DistanceAlongRayToDoor(ray);
            if (enter < 0f || enter > rayRange) return false;

            Vector3 n = interiorDir; n.y = 0f; n.Normalize();
            Vector3 p = ray.GetPoint(enter);
            if (p.y < 0f || p.y > openingHeight) return false;
            Vector3 d = p - doorCenter; d.y = 0f;
            Vector3 lat = Vector3.Cross(Vector3.up, n).normalized;
            return Mathf.Abs(Vector3.Dot(d, lat)) < halfWidth;
        }

        bool IsDoorPart(Collider c)
        {
            if (c == null) return false;
            foreach (var t in clickTargets)
                if (t == c) return true;
            // 兜底：命中的是自己层级下的碰撞体
            return c.transform.IsChildOf(transform) ||
                   (leaf != null && (c.transform == leaf || c.transform.IsChildOf(leaf)));
        }

        /// <summary>开门 + 门铃。已开 / 正在开时返回 false。</summary>
        public bool Open()
        {
            if (state == State.Opening || state == State.Open) return false;
            state = State.Opening;
            RingBell();
            return true;
        }

        public void ForceClosed()
        {
            state = State.Closed;
            openAmount = 0f;
            Apply();
        }

        void RingBell()
        {
            bellCount++;
            if (bellSource != null && bellSource.clip != null) bellSource.Play();
        }

        void Apply()
        {
            if (leaf != null)
                leaf.rotation = Quaternion.AngleAxis(swingSign * openAngle * openAmount, Vector3.up) * closedRot;
        }

        void OnDrawGizmosSelected()
        {
            Vector3 n = interiorDir; n.y = 0f;
            if (n.sqrMagnitude < 1e-6f) return;
            n.Normalize();
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(doorCenter, doorCenter + n * 1.5f);
            Gizmos.DrawWireSphere(doorCenter, 0.15f);
        }
    }
}

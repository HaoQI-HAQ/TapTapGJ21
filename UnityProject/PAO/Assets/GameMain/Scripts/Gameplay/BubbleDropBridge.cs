using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 泡泡墙 → 掉落桥：泡泡墙被打破后，被它"包住"的那几块（砖块 + 铁网格）从上面的柱子断开，
    /// 像吊桥一样绕着**底边**倒下去，落到前方与大石头接上，变成可以走的桥。
    ///
    /// 【为什么不真的加刚体让它掉】
    /// 1. 关卡要的是"每次都长成同一个样子"，物理下落会滚、会弹、会歪；
    /// 2. 掉落件都是大 MeshCollider，下坠快了会穿地形，落地还抖；
    /// 3. 策划调"倒多少度、用多久"比调质量/摩擦直观得多。
    /// 所以用脚本化的绕轴旋转：用 t^power 模拟自由落体的加速，落点精确可控。
    ///
    /// 【为什么不把零件挂到自己下面】
    /// 那几块零件现在挂在场景的分组（比如 sm_l3_ji_guan）下面，
    /// 如果那层是 prefab 实例，`SetParent` 出去会被 Unity 挡掉（拖出去会自动弹回来），
    /// 结果就是"脚本建好了但零件没跟着动"。
    /// 所以这里**完全不改父子关系**：只记住每个零件自己的初始位置/朝向，
    /// 每帧算好旋转再把位置/朝向写回去 —— 谁当爸爸都不影响。
    ///
    /// 【怎么知道泡泡墙被打破了】
    /// 墙上有 <see cref="TerrainBubble"/>：
    ///   · 炸弹炸 → Break(Bomb) → 墙对象被销毁 → 这里发现 m_Wall 变 null；
    ///   · 弹力泡泡打破 → 只把渲染/碰撞关掉 N 秒后长回来 → 这里检测到碰撞体全关。
    /// 默认只有"永久破（炸弹）"才触发掉桥，因为桥掉下去就回不来了。
    ///
    /// 结构：
    ///   Bridge_xxx（本组件，只当逻辑载体和支点标记，位置=铰链支点）
    ///   零件们留在原来的父级下面，由本组件驱动。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/泡泡墙掉落桥（Bubble Drop Bridge）")]
    public sealed class BubbleDropBridge : MonoBehaviour
    {
        public enum FallMode
        {
            /// <summary>吊桥式：绕底边转到指定角度（推荐）。</summary>
            Hinge,

            /// <summary>直落：垂直往下掉，直到射线打到地面。</summary>
            StraightDown,

            /// <summary>精确落点：插值到 m_Target 的位置和朝向。</summary>
            TargetPose
        }

        // ==================== 触发 ====================

        [Header("触发")]
        [Tooltip("要盯着的泡泡墙（带 TerrainBubble 的那个球）。")]
        [SerializeField] private Transform m_Wall;

        [Tooltip("弹力泡泡的「短暂破坏」也触发掉桥。默认关：桥掉下去回不来，短暂破墙不该永久改地形。")]
        [SerializeField] private bool m_TriggerOnTemporaryBreak = false;

        [Tooltip("墙破了之后延迟多久开始掉（秒）。留时间给爆炸特效。")]
        [SerializeField] private float m_TriggerDelay = 0.35f;

        // ==================== 掉落 ====================

        [Header("掉落")]
        [Tooltip("会掉下去的那些零件（砖块 + 铁网格…）。父子关系不会被改动。")]
        [SerializeField] private Transform[] m_FallingParts;

        [Tooltip("掉落方式。默认 Hinge（吊桥式倒下）。")]
        [SerializeField] private FallMode m_Mode = FallMode.Hinge;

        [Tooltip("绕哪根轴倒（世界方向）。墙是薄片，轴取它的宽度方向，倒下去就垂直于墙面。")]
        [SerializeField] private Vector3 m_HingeAxis = Vector3.right;

        [Tooltip("手动指定铰链支点（世界坐标）。\n" +
                 "不勾 = 用掉落件包围盒的底边中心。\n" +
                 "想要「倒下后正好压在下面那块石台/方块上」，就把支点挪到那块东西的上边缘。")]
        [SerializeField] private bool m_UseHingePivotOverride = false;

        [SerializeField] private Vector3 m_HingePivotOverride = Vector3.zero;

        [Tooltip("倒多少度。88 ≈ 放平；超过 90 会往下方压（远端落到更低的石头上）；负值反向倒。")]
        [SerializeField] private float m_FallAngle = 88f;

        [Tooltip("倒完用多少秒。")]
        [SerializeField] private float m_FallDuration = 1.4f;

        [Tooltip("下落加速：1 = 匀速，2 = 自由落体（越来越快）。")]
        [SerializeField, Range(1f, 3f)] private float m_GravityPower = 2f;

        [Tooltip("TargetPose 模式的落点。")]
        [SerializeField] private Transform m_Target;

        [Tooltip("StraightDown 模式：往下打到地面就停。")]
        [SerializeField] private LayerMask m_GroundMask = ~0;

        [Tooltip("StraightDown 模式最多掉多少米（防止掉出地图）。")]
        [SerializeField] private float m_MaxDropDistance = 20f;

        // ==================== 落地后 ====================

        [Header("落地后")]
        [Tooltip("StraightDown 落地后再往下压一点，避免看着浮在地面上（米）。")]
        [SerializeField] private float m_LandSink = 0.05f;

        [Tooltip("掉完之后保留碰撞体（要能站上去就勾着）。")]
        [SerializeField] private bool m_KeepColliders = true;

        // ==================== 调试 ====================

        [Header("调试")]
        [SerializeField] private bool m_Log = true;

        // ==================== 运行时 ====================

        private bool m_Triggered;
        private bool m_Falling;
        private bool m_Finished;
        private float m_FallTimer;
        private float m_DelayLeft;

        private Vector3[] m_PartPositions;
        private Quaternion[] m_PartRotations;
        private Vector3 m_HingePivot;
        private float m_StraightDropDistance;

        /// <summary>桥已经落下并停稳。</summary>
        public bool HasFallen { get { return m_Finished; } }

        /// <summary>铰链支点（世界坐标）。</summary>
        public Vector3 HingePivot { get { return m_HingePivot; } }

        // ==================== 生命周期 ====================

        private void Awake()
        {
            if (m_Wall == null)
            {
                TerrainBubble bubble = GetComponentInChildren<TerrainBubble>(true);

                if (bubble != null)
                {
                    m_Wall = bubble.transform;
                }
            }

            CacheParts();
        }

        private void CacheParts()
        {
            if (m_FallingParts == null || m_FallingParts.Length == 0)
            {
                m_PartPositions = new Vector3[0];
                m_PartRotations = new Quaternion[0];
                m_HingePivot = transform.position;
                return;
            }

            m_PartPositions = new Vector3[m_FallingParts.Length];
            m_PartRotations = new Quaternion[m_FallingParts.Length];

            for (int i = 0; i < m_FallingParts.Length; i++)
            {
                Transform part = m_FallingParts[i];

                if (part == null)
                {
                    continue;
                }

                m_PartPositions[i] = part.position;
                m_PartRotations[i] = part.rotation;
            }

            m_HingePivot = ComputeHingePivot();
        }

        private void OnDisable()
        {
            if (m_Falling && !m_Finished)
            {
                RestoreParts();
            }
        }

        // ==================== 每帧 ====================

        private void Update()
        {
            if (m_Finished)
            {
                return;
            }

            if (!m_Triggered)
            {
                if (WallBroken())
                {
                    Trigger();
                }

                return;
            }

            if (m_DelayLeft > 0f)
            {
                m_DelayLeft -= Time.deltaTime;

                if (m_DelayLeft > 0f)
                {
                    return;
                }
            }

            StepFall(Time.deltaTime);
        }

        /// <summary>墙还在不在。炸弹炸掉 = 对象没了；弹力泡泡打破 = 碰撞体全关。</summary>
        private bool WallBroken()
        {
            if (m_Wall == null)
            {
                return true;
            }

            Collider[] colliders = m_Wall.GetComponentsInChildren<Collider>(true);
            bool anyEnabled = false;

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && colliders[i].enabled)
                {
                    anyEnabled = true;
                    break;
                }
            }

            return !anyEnabled && m_TriggerOnTemporaryBreak;
        }

        private void Trigger()
        {
            m_Triggered = true;
            m_Falling = true;
            m_FallTimer = 0f;
            m_DelayLeft = Mathf.Max(0f, m_TriggerDelay);

            if (m_Mode == FallMode.StraightDown)
            {
                m_StraightDropDistance = MeasureFloorDrop();
            }

            if (m_Log)
            {
                Debug.Log("[BubbleDropBridge] \"" + name + "\" 泡泡墙被打破 → 开始掉落（" + m_Mode +
                          "，角度 " + m_FallAngle + "°，用时 " + m_FallDuration + "s，" +
                          "延迟 " + m_TriggerDelay + "s，掉落件 " +
                          (m_FallingParts != null ? m_FallingParts.Length : 0) + " 个）", this);
            }
        }

        private void StepFall(float dt)
        {
            m_FallTimer += dt;

            float duration = Mathf.Max(0.05f, m_FallDuration);
            float t = Mathf.Clamp01(m_FallTimer / duration);
            float eased = Mathf.Pow(t, Mathf.Max(1f, m_GravityPower));

            switch (m_Mode)
            {
                case FallMode.Hinge:
                    ApplyHinge(eased);
                    break;

                case FallMode.StraightDown:
                    ApplyStraightDown(eased);
                    break;

                case FallMode.TargetPose:
                    ApplyTargetPose(eased);
                    break;
            }

            if (t < 1f)
            {
                return;
            }

            m_Falling = false;
            m_Finished = true;
            Land();

            if (m_Log)
            {
                Debug.Log("[BubbleDropBridge] \"" + name + "\" 落地完成", this);
            }
        }

        // ==================== 三种掉落方式 ====================

        private void ApplyHinge(float eased)
        {
            Vector3 axis = m_HingeAxis.sqrMagnitude > 0.0001f ? m_HingeAxis.normalized : Vector3.right;
            float angle = m_FallAngle * eased;
            Quaternion rotation = Quaternion.AngleAxis(angle, axis);

            for (int i = 0; i < m_FallingParts.Length; i++)
            {
                Transform part = m_FallingParts[i];

                if (part == null)
                {
                    continue;
                }

                Vector3 localOffset = m_PartPositions[i] - m_HingePivot;
                part.position = m_HingePivot + rotation * localOffset;
                part.rotation = rotation * m_PartRotations[i];
            }
        }

        private void ApplyStraightDown(float eased)
        {
            float distance = m_StraightDropDistance > 0f ? m_StraightDropDistance : m_MaxDropDistance;

            for (int i = 0; i < m_FallingParts.Length; i++)
            {
                Transform part = m_FallingParts[i];

                if (part == null)
                {
                    continue;
                }

                part.position = m_PartPositions[i] + Vector3.down * (distance * eased);
                part.rotation = m_PartRotations[i];
            }
        }

        private void ApplyTargetPose(float eased)
        {
            if (m_Target == null)
            {
                return;
            }

            // 目标姿态：以目标点的旋转为准，把每个零件的相对偏移一起转过去
            Quaternion deltaRotation = m_Target.rotation;

            for (int i = 0; i < m_FallingParts.Length; i++)
            {
                Transform part = m_FallingParts[i];

                if (part == null)
                {
                    continue;
                }

                Vector3 localOffset = m_PartPositions[i] - m_HingePivot;

                // 先按目标姿态转，再按 eased 在"原位 ↔ 目标位"之间插值
                Vector3 targetPosition = m_Target.position + deltaRotation * localOffset;
                Quaternion targetRotation = deltaRotation * m_PartRotations[i];

                part.position = Vector3.Lerp(m_PartPositions[i], targetPosition, eased);
                part.rotation = Quaternion.Slerp(m_PartRotations[i], targetRotation, eased);
            }
        }

        private void Land()
        {
            if (m_LandSink > 0f && m_Mode == FallMode.StraightDown)
            {
                for (int i = 0; i < m_FallingParts.Length; i++)
                {
                    if (m_FallingParts[i] != null)
                    {
                        m_FallingParts[i].position += Vector3.down * m_LandSink;
                    }
                }
            }

            for (int i = 0; i < m_FallingParts.Length; i++)
            {
                Transform part = m_FallingParts[i];

                if (part == null)
                {
                    continue;
                }

                Collider[] colliders = part.GetComponentsInChildren<Collider>(true);

                for (int c = 0; c < colliders.Length; c++)
                {
                    if (colliders[c] != null)
                    {
                        colliders[c].enabled = m_KeepColliders;
                    }
                }
            }
        }

        private void RestoreParts()
        {
            for (int i = 0; i < m_FallingParts.Length; i++)
            {
                if (m_FallingParts[i] == null)
                {
                    continue;
                }

                m_FallingParts[i].position = m_PartPositions[i];
                m_FallingParts[i].rotation = m_PartRotations[i];
            }
        }

        // ==================== 辅助 ====================

        /// <summary>铰链支点：默认是所有掉落件包围盒的**底面中心**；也可以手动指定。</summary>
        private Vector3 ComputeHingePivot()
        {
            if (m_UseHingePivotOverride)
            {
                return m_HingePivotOverride;
            }

            Bounds bounds;
            bool has = false;
            bounds = default(Bounds);

            for (int i = 0; i < m_FallingParts.Length; i++)
            {
                Bounds b;

                if (m_FallingParts[i] == null || !TryGetBounds(m_FallingParts[i], out b))
                {
                    continue;
                }

                if (!has) { bounds = b; has = true; }
                else bounds.Encapsulate(b);
            }

            if (!has)
            {
                return transform.position;
            }

            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        private float MeasureFloorDrop()
        {
            Bounds bounds;

            if (!TryGetBounds(transform, out bounds))
            {
                return m_MaxDropDistance;
            }

            Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + 0.1f, bounds.center.z);
            RaycastHit hit;

            if (Physics.Raycast(origin, Vector3.down, out hit, m_MaxDropDistance, m_GroundMask,
                                QueryTriggerInteraction.Ignore))
            {
                return Mathf.Max(0f, hit.distance - 0.1f);
            }

            return m_MaxDropDistance;
        }

        private static bool TryGetBounds(Transform root, out Bounds bounds)
        {
            bounds = default(Bounds);
            bool has = false;

            if (root == null)
            {
                return false;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;

                if (!has) { bounds = renderers[i].bounds; has = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }

            if (has)
            {
                return true;
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null) continue;

                if (!has) { bounds = colliders[i].bounds; has = true; }
                else bounds.Encapsulate(colliders[i].bounds);
            }

            return has;
        }

        // ==================== 编辑器可视化 ====================

        private void OnDrawGizmosSelected()
        {
            Vector3 pivot = Application.isPlaying ? m_HingePivot : ComputeHingePivot();
            Vector3 axis = m_HingeAxis.sqrMagnitude > 0.0001f ? m_HingeAxis.normalized : Vector3.right;

            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(pivot, 0.35f);
            Gizmos.DrawLine(pivot - axis * 2f, pivot + axis * 2f);

            Bounds bounds;

            if (TryGetBounds(transform, out bounds))
            {
                Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.7f);
                Gizmos.DrawLine(pivot, pivot + Quaternion.AngleAxis(m_FallAngle, axis) * (Vector3.up * bounds.size.y));
            }
        }

        // ==================== 由搭建脚本调用 ====================

        public void Configure(Transform wall, Transform[] fallingParts, FallMode mode,
                              Vector3 hingeAxis, float fallAngle, float fallDuration)
        {
            m_Wall = wall;
            m_FallingParts = fallingParts;
            m_Mode = mode;
            m_HingeAxis = hingeAxis.sqrMagnitude > 0.0001f ? hingeAxis.normalized : Vector3.right;
            m_FallAngle = fallAngle;
            m_FallDuration = Mathf.Max(0.1f, fallDuration);
        }
    }
}

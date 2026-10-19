using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 传送触发器：玩家碰到就瞬间移动到一个指定位置。
    ///
    /// 【为什么目标是 Transform 而不是三个坐标数字】
    /// 关卡里要反复调落点。用一个空物体当锚点，在 Scene 视图里直接拖那个点就行，
    /// 不用回来对着 Inspector 敲 x/y/z；锚点改了，脚本不用动。
    ///
    /// 【和 tiaozhuan（切场景）的区别】
    /// tiaozhuan 是 SceneManager.LoadScene —— 换整张地图。
    /// 这个只是在同一张地图里挪位置，场景状态（机关、泡泡、地形）全部保留。
    ///
    /// 【为什么传送必须走 PlayerController.Teleport】
    /// CharacterController 会把它自己"修正"回原来的位置，直接改 transform.position
    /// 会被弹回去。Teleport 内部先关掉 CharacterController 再设位置再打开，
    /// 并且顺手清掉下坠速度、冲刺和相机 yaw —— 不然传送完人会带着原来的速度飞出去。
    ///
    /// 【落点别放在触发区里面】
    /// 目标点如果落在触发区自己的体积里，玩家传过去仍然"在里面"，
    /// 物理不会产生新的进入事件，这个门就变成单向了（其实是好事，不会来回弹）。
    /// 真遇到这种情况 Console 会在开始时给一条警告（见 WarnIfTargetInsideTrigger）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/传送触发器（Teleport Trigger）")]
    public sealed class TeleportTrigger : MonoBehaviour
    {
        [Header("目的地")]
        [Tooltip("传送目的地。把场景里一个空物体拖进来，玩家会被送到它所在的位置")]
        [SerializeField] private Transform m_Target;

        [Tooltip("落点再往上抬多少（米）。用来避免脚陷进地板，或者从地板缝里掉下去")]
        [SerializeField] private float m_Lift = 0.1f;

        [Tooltip("传送后朝向是否跟随目的地。关掉则保持玩家原来的朝向")]
        [SerializeField] private bool m_UseTargetRotation = true;

        [Header("触发规则")]
        [Tooltip("只能传送一次（单程门）")]
        [SerializeField] private bool m_Once = false;

        [Tooltip("两次传送之间的最短间隔（秒）。防止在两个门之间来回弹")]
        [SerializeField] private float m_Cooldown = 0.75f;

        [Header("调试")]
        [Tooltip("传送时在 Console 打一行日志")]
        [SerializeField] private bool m_Log = true;

        private bool m_Used;
        private float m_NextAllowedTime;

        private void Awake()
        {
            EnsureTriggerCollider();
        }

        /// <summary>编辑器里刚挂上组件时也把 Trigger 摆好，不用先进 Play 才看得见范围。</summary>
        private void Reset()
        {
            EnsureTriggerCollider();
        }

        private void Start()
        {
            WarnIfTargetInsideTrigger();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_Used || Time.unscaledTime < m_NextAllowedTime)
            {
                return;
            }

            // 认玩家：工程里没有自定义 Tag，所以按组件判断。
            // 玩家的 CharacterController 能触发 OnTriggerEnter，触发区这边不需要刚体。
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player == null)
            {
                return;
            }

            if (m_Target == null)
            {
                Debug.LogError("[TeleportTrigger] \"" + name + "\" 没有指定目的地（Target），" +
                               "玩家碰到不会有任何反应。请把一个空物体拖进 Target。", this);
                return;
            }

            Vector3 destination = m_Target.position + Vector3.up * m_Lift;
            Quaternion rotation = m_UseTargetRotation ? m_Target.rotation : player.transform.rotation;

            m_NextAllowedTime = Time.unscaledTime + Mathf.Max(0f, m_Cooldown);
            m_Used = m_Once;

            // 骑着泡泡时被传送，人和泡泡的关系会留在半路，先跳下来
            DetachFromRide(player);

            player.Teleport(destination, rotation);

            if (m_Log)
            {
                Debug.Log("[TeleportTrigger] \"" + name + "\" 把玩家传送到 " + destination + "。", this);
            }
        }

        private static void DetachFromRide(PlayerController player)
        {
            BubbleRideInteractor ride = player.GetComponent<BubbleRideInteractor>();
            if (ride == null)
            {
                ride = player.GetComponentInChildren<BubbleRideInteractor>(true);
            }

            if (ride != null && ride.IsRiding)
            {
                ride.EndInteraction();
            }
        }

        // ==================== Trigger 碰撞体 ====================

        /// <summary>
        /// 保证物体上有一个 **Trigger** 碰撞体，否则 OnTriggerEnter 永远不触发，
        /// 而且一点报错都没有 —— 最难查的那种问题。
        ///
        /// 【绝对不能把实心碰撞体改成 Trigger】
        /// 把本组件挂到门框 / 石头这类有实心碰撞的物体上时，那会让它不再是实心 ——
        /// 玩家直接穿过去，而且不会报任何错。
        /// 所以这里只在"没有任何 Trigger"时**额外加一个**，原有的实心碰撞体保持原样。
        /// </summary>
        private void EnsureTriggerCollider()
        {
            Collider[] cols = GetComponents<Collider>();

            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i].isTrigger)
                {
                    return;
                }
            }

            bool hadSolid = cols.Length > 0;

            BoxCollider zone = gameObject.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            zone.size = new Vector3(2f, 3f, 2f);
            zone.center = new Vector3(0f, 1.5f, 0f);

            if (m_Log)
            {
                Debug.Log(hadSolid
                        ? "[TeleportTrigger] \"" + name + "\" 上没有 Trigger 碰撞体，" +
                          "已额外加一个 2×3×2 的传送检测区（原有实心碰撞体保持不变）。"
                        : "[TeleportTrigger] \"" + name + "\" 上没有任何碰撞体，" +
                          "已自动补一个 2×3×2 的 Trigger 检测区。", this);
            }
        }

        /// <summary>
        /// 落点落在自己体积里时提醒一句。
        ///
        /// 这种情况下物理不会产生"离开 → 再进入"，门会变成单向的，
        /// 关卡作者通常不是故意这么摆的，所以提前在 Console 里说一声。
        /// </summary>
        private void WarnIfTargetInsideTrigger()
        {
            if (m_Target == null)
            {
                Debug.LogWarning("[TeleportTrigger] \"" + name + "\" 的 Target 是空的，这个门现在不会传送任何人。", this);
                return;
            }

            Vector3 destination = m_Target.position + Vector3.up * m_Lift;
            Collider[] cols = GetComponents<Collider>();

            for (int i = 0; i < cols.Length; i++)
            {
                if (!cols[i].isTrigger)
                {
                    continue;
                }

                if (cols[i].bounds.Contains(destination))
                {
                    Debug.LogWarning("[TeleportTrigger] \"" + name + "\" 的落点 " + destination +
                                     " 落在它自己的触发区里。玩家传过去仍然「在里面」，" +
                                     "这个门会变成单向的。建议把 Target 挪到触发区外面。", this);
                    return;
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>选中时画出触发范围、落点，以及一条从门到落点的连线，方便摆位。</summary>
        private void OnDrawGizmosSelected()
        {
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.5f);
                Gizmos.matrix = transform.localToWorldMatrix;

                BoxCollider box = col as BoxCollider;
                if (box != null)
                {
                    Gizmos.DrawWireCube(box.center, box.size);
                }
                else
                {
                    SphereCollider sphere = col as SphereCollider;
                    if (sphere != null)
                    {
                        Gizmos.DrawWireSphere(sphere.center, sphere.radius);
                    }
                    else
                    {
                        CapsuleCollider capsule = col as CapsuleCollider;
                        if (capsule != null)
                        {
                            Gizmos.DrawWireSphere(capsule.center, capsule.radius);
                        }
                    }
                }

                Gizmos.matrix = Matrix4x4.identity;
            }

            if (m_Target == null)
            {
                return;
            }

            Vector3 destination = m_Target.position + Vector3.up * m_Lift;

            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawLine(transform.position, destination);
            Gizmos.DrawWireSphere(destination, 0.4f);
        }
#endif
    }
}

using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 掉到某个高度以下就把玩家送回出生点。
    ///
    /// 【为什么需要它】
    /// 爬塔玩法里地板会塌，玩家掉下去之后塔里没有任何东西接得住 ——
    /// 会一直往深渊里落。没有这个组件的话，"掉下去"就等于卡死。
    ///
    /// 【为什么不用触发器体积】
    /// 塔很高（100 米以上），掉下去的速度也快。用触发器体积要么做得极大，
    /// 要么就有高速穿透的风险（一帧跨过整个体积，触发器根本不触发）。
    /// 直接比较高度每帧检查，没有穿透问题，也省一堆物体。
    ///
    /// 【传送为什么要走 PlayerController.Teleport】
    /// 它内部会先关掉 CharacterController 再设位置再打开 ——
    /// 不这么做的话，CharacterController 会把你设的位置"修正"回去。
    /// 另外 PlayerController 每帧用 transform.position 更新相机枢轴，
    /// 所以传送之后相机会自动跟上，不用额外处理。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/坠落重生（Fall Respawn）")]
    public sealed class FallRespawn : MonoBehaviour
    {
        [Header("判定")]
        [Tooltip("掉到这个高度以下就重生（米）")]
        [SerializeField] private float m_KillHeight = -12f;

        [Tooltip("勾上后，判定高度 = 本物体所在高度 + 上面的 Kill Height。把深渊做成预制体随处摆时用这个")]
        [SerializeField] private bool m_RelativeToThisHeight = false;

        [Tooltip("连续低于判定高度多久才重生（秒）。给一点缓冲，避免边缘试探时误触发")]
        [SerializeField] private float m_GraceTime = 0.35f;

        [Header("重生点")]
        [Tooltip("重生位置。留空则用玩家开局的位置")]
        [SerializeField] private Transform m_SpawnPoint;

        [Header("表现")]
        [Tooltip("重生时是否在控制台打一行日志")]
        [SerializeField] private bool m_Log = true;

        private PlayerController m_Player;
        private Vector3 m_FallbackSpawn;
        private Quaternion m_FallbackRotation = Quaternion.identity;
        private float m_BelowTime;

        /// <summary>当前的判定高度。相对模式下跟着本物体的高度走。</summary>
        public float CurrentKillHeight
        {
            get { return m_RelativeToThisHeight ? transform.position.y + m_KillHeight : m_KillHeight; }
        }

        private void Start()
        {
            m_Player = FindFirstObjectByType<PlayerController>();

            if (m_Player == null)
            {
                Debug.LogWarning("[FallRespawn] 场景里没有 PlayerController，坠落重生不会生效。", this);
                return;
            }

            // 记下玩家开局的位置当作默认重生点
            m_FallbackSpawn = m_Player.transform.position;
            m_FallbackRotation = m_Player.transform.rotation;
        }

        private void Update()
        {
            if (m_Player == null)
            {
                return;
            }

            if (m_Player.transform.position.y > CurrentKillHeight)
            {
                m_BelowTime = 0f;
                return;
            }

            m_BelowTime += Time.deltaTime;

            if (m_BelowTime < m_GraceTime)
            {
                return;
            }

            m_BelowTime = 0f;
            Respawn();
        }

        private void Respawn()
        {
            Vector3 position = m_SpawnPoint != null ? m_SpawnPoint.position : m_FallbackSpawn;
            Quaternion rotation = m_SpawnPoint != null ? m_SpawnPoint.rotation : m_FallbackRotation;

            m_Player.Teleport(position, rotation);

            if (m_Log)
            {
                Debug.Log("[FallRespawn] 玩家掉出关卡，已送回 " + position + "。", this);
            }
        }

        /// <summary>由搭建脚本调用，写入判定高度和重生点。</summary>
        public void Configure(float killHeight, Transform spawnPoint, bool relativeToThisHeight = false)
        {
            m_KillHeight = killHeight;
            m_SpawnPoint = spawnPoint;
            m_RelativeToThisHeight = relativeToThisHeight;
        }
    }
}

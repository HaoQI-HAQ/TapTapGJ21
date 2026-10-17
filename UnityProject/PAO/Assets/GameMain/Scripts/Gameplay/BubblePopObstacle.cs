using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 爬塔的障碍物：**载着玩家的泡泡碰到它就破裂**，玩家随即掉下去。
    ///
    /// 【为什么用 Trigger 而不是实体碰撞】
    /// 如果做成实体，泡泡会先被物理弹开、或者卡在障碍上，
    /// 然后才轮到脚本处理 —— 表现是"泡泡贴着障碍滑了两下才破"，很迟钝。
    /// 做成 Trigger 就是"穿过去的那一瞬间就破"，手感干脆。
    ///
    /// 【为什么调 BubbleRideInteractor.EndInteraction 而不是自己 Destroy 泡泡】
    /// 那个方法本来就负责"从泡泡里出来"的完整收尾：
    ///   · 泡泡 ExitRide（恢复正常物理）
    ///   · 泡泡 Explode（破裂，以后加破裂特效/冲击波也走这条路）
    ///   · 重新打开角色的 CharacterController（让重力把人带下去）
    ///   · 清掉 PlayerController 的载具状态
    /// 自己 Destroy 泡泡会漏掉后面三步，玩家就会卡在原地不掉。
    ///
    /// 【只破"载着人的"那颗】
    /// 场上飘着的其他泡泡撞到障碍不动它 —— 否则玩家会看到
    /// 自己没受任何影响、远处的泡泡却自己破了。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/爬塔障碍（Pop On Touch）")]
    public sealed class BubblePopObstacle : MonoBehaviour
    {
        [Header("行为")]
        [Tooltip("碰到就破裂。关掉则这个障碍变成纯装饰")]
        [SerializeField] private bool m_PopOnTouch = true;

        [Tooltip("破裂后自己是否也消失（尖刺可以留下，横梁可以断掉）")]
        [SerializeField] private bool m_DestroySelfOnPop = false;

        [Tooltip("自己消失的延迟（秒），用来留一点破裂表现的余地")]
        [SerializeField] private float m_SelfDestroyDelay = 0.35f;

        [Header("表现")]
        [Tooltip("破裂时是否把自己缩一下再弹回（像被撞了一下）")]
        [SerializeField] private bool m_ReactOnPop = true;

        [Header("谁能被撞破")]
        [Tooltip("只破「玩家正在骑的那颗」。关掉则**任何**碰到它的泡泡都会破 —— " +
                 "朝上吹一颗试探的泡泡撞上来也会碎")]
        [SerializeField] private bool m_OnlyPopRiddenBubble = false;

        [Header("调试")]
        [Tooltip("打印每次触发（排查「为什么不破」时打开）。会刷屏，正常玩请关掉")]
        [SerializeField] private bool m_LogTouch = false;

        [SerializeField] private bool m_Log = false;

        private bool m_Popped;

        private void Reset()
        {
            // 挂上组件时自动把碰撞体设成触发器 ——
            // 忘了设的话它就是个实心墙，玩家会撞在上面但泡泡不破，
            // 而且没有任何报错，最难查。
            EnsureTrigger();
        }

        private void Awake()
        {
            EnsureTrigger();
        }

        private void EnsureTrigger()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);

            if (colliders.Length == 0)
            {
                // 连碰撞体都没有 → 触发器永远不会触发。补一个。
                var box = gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = transform.localScale == Vector3.zero
                    ? Vector3.one
                    : transform.localScale;

                Debug.LogWarning("[BubblePopObstacle] \"" + name +
                                 "\" 上没有 Collider，已自动补一个 Trigger BoxCollider。", this);
                return;
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                if (!colliders[i].isTrigger)
                {
                    colliders[i].isTrigger = true;

                    Debug.LogWarning("[BubblePopObstacle] \"" + name +
                                     "\" 上的 Collider 没勾 Is Trigger，已自动勾上" +
                                     "（否则泡泡会撞在实体上、不会破）。", this);
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_LogTouch)
            {
                Debug.Log("[BubblePopObstacle] \"" + name + "\" 触发了，来的是 \"" +
                          other.name + "\"（层 " + other.gameObject.layer + "）" +
                          "，PopOnTouch=" + m_PopOnTouch + "，已破=" + m_Popped, this);
            }

            if (!m_PopOnTouch || m_Popped)
            {
                return;
            }

            // 情况一：撞进来的直接是泡泡
            Bubble bubble = other.GetComponentInParent<Bubble>();

            if (bubble != null)
            {
                if (m_LogTouch)
                {
                    Debug.Log("[BubblePopObstacle] 来的是泡泡（类型 " + bubble.Type + "）。" +
                              "OnlyPopRidden=" + m_OnlyPopRiddenBubble, this);
                }

                if (m_OnlyPopRiddenBubble)
                {
                    if (TryPopRiddenBubble(bubble))
                    {
                        return;
                    }

                    if (m_LogTouch)
                    {
                        Debug.Log("[BubblePopObstacle] 这颗泡泡没人在骑 → 按设置不动它。", this);
                    }

                    return;
                }

                // 任何泡泡都破
                PoppingBubble(bubble);
                return;
            }

            // 情况二：撞进来的是玩家本人（玩家的碰撞体比泡泡大，
            // 有时候先撞到的是人而不是泡泡）
            PlayerController player = other.GetComponentInParent<PlayerController>();

            if (player != null)
            {
                if (m_LogTouch)
                {
                    Debug.Log("[BubblePopObstacle] 来的是玩家本人。", this);
                }

                BubbleRideInteractor interactor = player.GetComponent<BubbleRideInteractor>();

                if (interactor != null && interactor.IsRiding)
                {
                    Pop(interactor);
                }
            }
            else if (m_LogTouch)
            {
                Debug.Log("[BubblePopObstacle] 来的东西既不是泡泡也不是玩家 —— " +
                          "可能是场景物件或地形，忽略。", this);
            }
        }

        /// <summary>撞破一颗没有乘客的泡泡。</summary>
        private void PoppingBubble(Bubble bubble)
        {
            m_Popped = true;

            if (m_Log || m_LogTouch)
            {
                Debug.Log("[BubblePopObstacle] \"" + name + "\" 撞破了一颗无人乘坐的泡泡。", this);
            }

            // 走 Explode 而不是 Destroy —— 这样破裂特效、冲击波、返还泡泡值
            // 这些后续处理都能接上（Bubble.Explode 内部会收尾并销毁自己）
            bubble.Explode();

            if (m_ReactOnPop)
            {
                StartCoroutine(ReactRoutine());
            }

            if (m_DestroySelfOnPop)
            {
                Destroy(gameObject, m_SelfDestroyDelay);
            }
        }

        /// <summary>
        /// 如果这颗泡泡正载着玩家，就把它弄破。返回是否真的破了。
        /// </summary>
        private bool TryPopRiddenBubble(Bubble bubble)
        {
            PlayerController player = FindFirstObjectByType<PlayerController>();

            if (player == null)
            {
                return false;
            }

            BubbleRideInteractor interactor = player.GetComponent<BubbleRideInteractor>();

            if (interactor == null || !interactor.IsRidingThis(bubble))
            {
                return false;   // 是颗没人骑的泡泡，不动它
            }

            Pop(interactor);
            return true;
        }

        private void Pop(BubbleRideInteractor interactor)
        {
            m_Popped = true;

            // EndInteraction 内部会：ExitRide → Explode → 恢复角色控制
            // 于是玩家从破掉的泡泡里掉下去，正是我们要的效果
            interactor.EndInteraction();

            if (m_Log)
            {
                Debug.Log("[BubblePopObstacle] \"" + name + "\" 撞破了玩家的泡泡。", this);
            }

            if (m_ReactOnPop)
            {
                StartCoroutine(ReactRoutine());
            }

            if (m_DestroySelfOnPop)
            {
                Destroy(gameObject, m_SelfDestroyDelay);
            }
        }

        /// <summary>被撞时的缩一下 —— 让"是我把它撞破的"这件事看得出来。</summary>
        private System.Collections.IEnumerator ReactRoutine()
        {
            Vector3 original = transform.localScale;
            Vector3 squashed = new Vector3(original.x * 0.88f, original.y * 0.88f, original.z * 0.88f);

            float t = 0f;
            const float duration = 0.18f;

            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Sin(t / duration * Mathf.PI);   // 0 → 1 → 0
                transform.localScale = Vector3.Lerp(original, squashed, k);
                yield return null;
            }

            transform.localScale = original;

            // 不复位 m_Popped：一个障碍只响一次，避免同一次碰撞刷好几遍
        }

        /// <summary>由搭建脚本调用，写入这条障碍的配置。</summary>
        public void Configure(bool destroySelfOnPop, bool onlyPopRidden = false)
        {
            m_DestroySelfOnPop = destroySelfOnPop;
            m_OnlyPopRiddenBubble = onlyPopRidden;
        }
    }
}

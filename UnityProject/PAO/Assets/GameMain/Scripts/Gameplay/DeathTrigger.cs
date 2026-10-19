using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace PAO
{
    /// <summary>
    /// 死亡区域：玩家碰到就"死"一次，短暂停顿后送回出生点。
    ///
    /// 【和 UI 里的「重开」不是一回事】
    /// 暂停菜单的「重开」、失败页的「重试本章」走的都是 SceneManager.LoadScene ——
    /// 整张地图重新加载，机关 / 泡泡 / 泡泡地形全部回到初始状态。
    /// 这个组件要的是"人回到出生点，世界照旧"，所以和 FallRespawn 一样走
    /// <see cref="PlayerController.Teleport"/>。
    ///
    /// 【为什么传送必须走 PlayerController.Teleport】
    /// CharacterController 会把直接改掉的 transform.position "修正"回原来的位置，
    /// 所以必须要先关掉它再设位置。Teleport 内部做的就是这件事，另外顺手清掉
    /// 下坠速度、冲刺和相机 yaw —— 不然复活之后人会带着死亡时的速度飞出去。
    ///
    /// 【为什么死亡要停一下】
    /// 立刻瞬移玩家会以为"卡了一下"，看不出自己死了。这里做三件事把这一下做清楚：
    ///   1. 锁住移动和视角（ControlLocked），人不会一边闪黑一边还能走
    ///   2. 闪一下黑（可选），盖住瞬移本身
    ///   3. 水母脸换成「死」表情（可选），复活再换回来
    ///
    /// 【用法】
    ///   1. 挂到尖刺 / 岩浆面 / 危险水面等任意物体上
    ///   2. 挂上后会自动补一个 Trigger 碰撞体（**不会**把原有的实心碰撞体改成
    ///      Trigger，见 EnsureTriggerCollider），在 Inspector 里调 Size / Center
    ///   3. Respawn Point 留空 = 用玩家开局的位置；也可以拖一个空物体指定
    ///   4. 玩家碰到 → 死在原地 → 回到出生点，场景其余状态全部保留
    ///
    /// 【摔死请用 FallRespawn，不要用这个】
    /// 触发器是"每物理帧检查重叠"，高速下坠有可能一帧跨过整个体积而不触发。
    /// 掉出关卡这种需求用 FallRespawn（它比高度，没有穿透问题）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/死亡区域（Death Trigger）")]
    public sealed class DeathTrigger : MonoBehaviour
    {
        [Header("死亡节奏")]
        [Tooltip("变黑之后停留多久才传送（秒）。给玩家一个「我死了」的反应时间")]
        [SerializeField] private float m_DeathDelay = 0.4f;

        [Tooltip("死亡期间锁住移动和视角，避免一边闪黑一边还能走")]
        [SerializeField] private bool m_LockControl = true;

        [Tooltip("复活后多久内不再触发（秒）。防止出生点正好落在死亡区里时被反复杀")]
        [SerializeField] private float m_RespawnGrace = 0.8f;

        [Header("复活点")]
        [Tooltip("复活位置。留空则用玩家开局的位置")]
        [SerializeField] private Transform m_SpawnPoint;

        [Tooltip("复活朝向是否跟随复活点。关掉则保持玩家死亡时的朝向")]
        [SerializeField] private bool m_UseSpawnRotation = true;

        [Header("表现")]
        [Tooltip("死亡时闪一下黑屏，盖住瞬移")]
        [SerializeField] private bool m_Fade = true;

        [Tooltip("变黑用时（秒）")]
        [SerializeField] private float m_FadeIn = 0.12f;

        [Tooltip("恢复用时（秒）")]
        [SerializeField] private float m_FadeOut = 0.3f;

        [Tooltip("死亡时播一次失败反馈（音效 + 画面，走 FeedbackBus）")]
        [SerializeField] private bool m_Feedback = true;

        [Tooltip("死亡时把水母脸换成「死」表情，复活后恢复")]
        [SerializeField] private bool m_DeadFace = true;

        [Header("调试")]
        [Tooltip("触发时在 Console 打一行日志")]
        [SerializeField] private bool m_Log = true;

        private PlayerController m_Player;
        private Vector3 m_StartPosition;
        private Quaternion m_StartRotation;
        private bool m_HasStartPose;

        private bool m_Busy;
        private float m_NextAllowedTime;
        private float m_NextSearchTime;
        private int m_SearchAttempts;

        private bool m_PreviousControlLocked;
        private JellyFaceSwitcher m_Face;
        private JellyFaceDirector m_FaceDirector;
        private bool m_FaceDirectorWasEnabled;

        private GameObject m_FadeRoot;
        private Image m_FadeImage;

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
            CapturePlayer();
        }

        private void Update()
        {
            // 玩家可能是运行时由流程生成的（Map4 那种 defaultPlayer 自动生成），
            // 所以 Start 时找不到就继续找。找到的那一刻就是他的出生位置。
            if (m_Player != null)
            {
                return;
            }

            if (Time.unscaledTime < m_NextSearchTime)
            {
                return;
            }

            // 前 2 秒每帧找（保证出生位置抓得准），之后退回每 0.25 秒一次 ——
            // 免得在没有玩家的场景里一辈子每帧全场景遍历一遍。
            m_SearchAttempts++;
            m_NextSearchTime = Time.unscaledTime + (m_SearchAttempts < 120 ? 0f : 0.25f);

            CapturePlayer();
        }

        private void OnDestroy()
        {
            // 死亡演到一半被销毁（切场景、机关被删）：把玩家和表情放回去，
            // 否则人物会永远动不了、脸也一直是「死」。
            if (m_Busy)
            {
                RestoreFace();

                if (m_Player != null && m_LockControl)
                {
                    m_Player.ControlLocked = m_PreviousControlLocked;
                }
            }

            if (m_FadeRoot != null)
            {
                Destroy(m_FadeRoot);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (m_Busy || Time.unscaledTime < m_NextAllowedTime)
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

            m_Player = player;
            CaptureStartPoseIfNeeded();

            m_Busy = true;
            StartCoroutine(DeathRoutine(player));
        }

        // ==================== 玩家 / 出生点 ====================

        /// <summary>找到玩家，并记下他"刚出生时"的位置当作默认复活点。</summary>
        private void CapturePlayer()
        {
            m_Player = FindFirstObjectByType<PlayerController>();
            if (m_Player == null)
            {
                return;
            }

            CaptureStartPoseIfNeeded();
        }

        private void CaptureStartPoseIfNeeded()
        {
            if (m_HasStartPose || m_Player == null)
            {
                return;
            }

            m_StartPosition = m_Player.transform.position;
            m_StartRotation = m_Player.transform.rotation;
            m_HasStartPose = true;
        }

        // ==================== 死亡流程 ====================

        private IEnumerator DeathRoutine(PlayerController player)
        {
            m_PreviousControlLocked = player.ControlLocked;
            if (m_LockControl)
            {
                player.ControlLocked = true;
            }

            // 骑着泡泡时被传送，人和泡泡的关系会留在半路，先跳下来
            DetachFromRide(player);

            ApplyDeadFace();

            if (m_Feedback)
            {
                PAO.AV.FeedbackBus.Emit(PAO.AV.FeedbackCue.ChapterFailure, player.transform.position, 1f);
            }

            if (m_Fade)
            {
                yield return FadeTo(1f, m_FadeIn);
            }

            if (m_DeathDelay > 0f)
            {
                // 用 realtime：万一这段时间里 timeScale 被别的系统改过，也不至于卡死
                yield return new WaitForSecondsRealtime(m_DeathDelay);
            }

            if (player == null)
            {
                m_Busy = false;
                yield break;
            }

            Vector3 position;
            Quaternion rotation;

            if (m_SpawnPoint != null)
            {
                position = m_SpawnPoint.position;
                rotation = m_UseSpawnRotation ? m_SpawnPoint.rotation : player.transform.rotation;
            }
            else
            {
                if (!m_HasStartPose)
                {
                    Debug.LogWarning("[DeathTrigger] \"" + name + "\" 没记到玩家的出生位置，" +
                                     "也没有指定 Respawn Point，只能原地复活。请给 Respawn Point 拖一个空物体。", this);
                    m_StartPosition = player.transform.position;
                    m_StartRotation = player.transform.rotation;
                }

                position = m_StartPosition;
                rotation = m_UseSpawnRotation ? m_StartRotation : player.transform.rotation;
            }

            player.Teleport(position, rotation);

            // 传送完就能动了：黑屏还在淡出，但输入已经交还给玩家
            RestoreFace();
            if (m_LockControl)
            {
                player.ControlLocked = m_PreviousControlLocked;
            }

            if (m_Fade)
            {
                yield return FadeTo(0f, m_FadeOut);
            }

            m_NextAllowedTime = Time.unscaledTime + Mathf.Max(0f, m_RespawnGrace);
            m_Busy = false;

            if (m_Log)
            {
                Debug.Log("[DeathTrigger] \"" + name + "\" 杀死玩家，已送回 " + position + "。", this);
            }
        }

        private void DetachFromRide(PlayerController player)
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

        // ==================== 表情 ====================

        /// <summary>
        /// 换成「死」表情。
        ///
        /// 【为什么要先关掉 JellyFaceDirector】
        /// 它每帧都会把目标表情写进 Switcher（走路 / 落水 / 气泡值低各有目标），
        /// 不关掉的话这里刚设的 Dead 下一帧就被覆盖了。
        /// </summary>
        private void ApplyDeadFace()
        {
            if (!m_DeadFace || m_Player == null)
            {
                return;
            }

            if (m_Face == null)
            {
                m_Face = m_Player.GetComponentInChildren<JellyFaceSwitcher>(true);
            }

            if (m_Face == null)
            {
                return;
            }

            if (m_FaceDirector == null)
            {
                m_FaceDirector = m_Player.GetComponent<JellyFaceDirector>();
            }

            if (m_FaceDirector != null)
            {
                m_FaceDirectorWasEnabled = m_FaceDirector.enabled;
                m_FaceDirector.enabled = false;
            }

            m_Face.SetExpression(JellyFace.Dead, true);
        }

        private void RestoreFace()
        {
            if (m_Face == null)
            {
                return;
            }

            m_Face.ResetExpression();

            if (m_FaceDirector != null)
            {
                m_FaceDirector.enabled = m_FaceDirectorWasEnabled;
            }
        }

        // ==================== 黑屏 ====================

        private IEnumerator FadeTo(float targetAlpha, float duration)
        {
            Image image = EnsureFadeImage();
            if (image == null)
            {
                yield break;
            }

            Color color = image.color;
            float startAlpha = color.a;

            if (duration <= 0.0001f)
            {
                color.a = targetAlpha;
                image.color = color;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (image == null)
                {
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                color.a = Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(elapsed / duration));
                image.color = color;
                yield return null;
            }

            color.a = targetAlpha;
            image.color = color;
        }

        /// <summary>
        /// 懒创建一个全屏黑图。
        ///
        /// 【为什么放在根节点】死亡区可能挂在一块会塌的地板上，
        /// 做成子物体会跟着一起没，黑屏就永远停在半黑。
        ///
        /// 【sortingOrder = 900】世界由摄像机画，Overlay Canvas 永远盖在世界之上；
        /// 游戏自己的 V2 UI Canvas 是 1000，所以这个 900 会盖住 HUD 但不挡暂停菜单。
        /// </summary>
        private Image EnsureFadeImage()
        {
            if (m_FadeImage != null)
            {
                return m_FadeImage;
            }

            m_FadeRoot = new GameObject("Death Fade");

            Canvas canvas = m_FadeRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;

            GameObject quad = new GameObject("Black");
            quad.transform.SetParent(m_FadeRoot.transform, false);

            m_FadeImage = quad.AddComponent<Image>();
            m_FadeImage.raycastTarget = false;
            m_FadeImage.color = new Color(0f, 0f, 0f, 0f);

            RectTransform rect = m_FadeImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return m_FadeImage;
        }

        // ==================== Trigger 碰撞体 ====================

        /// <summary>
        /// 保证物体上有一个 **Trigger** 碰撞体，否则 OnTriggerEnter 永远不触发，
        /// 而且一点报错都没有 —— 最难查的那种问题。
        ///
        /// 【绝对不能把实心碰撞体改成 Trigger】
        /// 把本组件挂到一块石头 / 一面墙 / 带碰撞的岩浆上时，那会让它不再是实心 ——
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
            zone.size = new Vector3(3f, 3f, 3f);
            zone.center = new Vector3(0f, 1.5f, 0f);

            if (m_Log)
            {
                Debug.Log(hadSolid
                        ? "[DeathTrigger] \"" + name + "\" 上没有 Trigger 碰撞体，" +
                          "已额外加一个 3×3×3 的死亡检测区（原有实心碰撞体保持不变）。"
                        : "[DeathTrigger] \"" + name + "\" 上没有任何碰撞体，" +
                          "已自动补一个 3×3×3 的 Trigger 检测区。", this);
            }
        }

#if UNITY_EDITOR
        /// <summary>选中时画出死亡范围，以及它和复活点之间的关系。</summary>
        private void OnDrawGizmosSelected()
        {
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.25f, 0.5f);
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

            if (m_SpawnPoint == null)
            {
                return;
            }

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            Gizmos.DrawLine(transform.position, m_SpawnPoint.position);
            Gizmos.DrawWireSphere(m_SpawnPoint.position, 0.4f);
        }
#endif
    }
}

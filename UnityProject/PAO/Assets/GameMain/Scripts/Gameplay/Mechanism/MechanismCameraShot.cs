using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace PAO
{
    /// <summary>
    /// 门边镜头：机关开启时把画面切到门旁边的摄像机，过一会儿再切回玩家。
    ///
    /// 【为什么不是把主摄像机瞬移过去】
    ///   主摄像机的 Transform 被 <see cref="PlayerController"/> 每帧接管（跟随、防穿墙），
    ///   直接搬走会和它打架，还要处理搬回来的位置。这里改用“第二台摄像机”：
    ///   - 门边的摄像机平时禁用，开门时启用；
    ///   - 它的 Depth 比主摄像机高，所以画面会盖在主摄像机上面；
    ///   - 显示期间锁住玩家移动（PlayerController.ControlLocked）；
    ///   - 时间到了自动禁用，主摄像机一直在原位，切回来完全没有跳变。
    ///
    /// 【怎么摆】
    ///   1. 在门旁边放一台普通 Camera，摆好构图，勾掉 Camera 组件的 Enabled（或把物体隐藏）；
    ///   2. Tag 保持 Untagged，不要设成 MainCamera；
    ///   3. 在任意空物体上挂本脚本，把摄像机拖到 Shot Camera；
    ///   4. 把本脚本拖到门的 Open Camera Shot。
    ///
    /// 【可选】
    ///   Shot Point 不为空时，播放前会把摄像机搬到它的位置和朝向；
    ///   Look At 不为空时，会强制看向它。适合“摄像机位置固定、只换目标”的用法。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/门边镜头（Mechanism Camera Shot）")]
    public sealed class MechanismCameraShot : MonoBehaviour
    {
        [Header("镜头")]
        [Tooltip("门边那台摄像机。不填则运行时在本物体下面自动创建一台")]
        [SerializeField] private Camera m_ShotCamera;

        [Tooltip("可选：播放时把摄像机搬到这个位置和朝向")]
        [SerializeField] private Transform m_ShotPoint;

        [Tooltip("可选：播放时强制看向这个目标")]
        [SerializeField] private Transform m_LookAt;

        [Tooltip("停留多久后切回玩家（秒）。填 0 = 不自动切回，等外部调 StopShot()")]
        [SerializeField] private float m_Duration = 3.5f;

        [Tooltip("时间到后自动切回玩家。关掉 = 一直停在门边镜头，直到外部调 StopShot()")]
        [SerializeField] private bool m_ReturnToGameplay = true;

        [Tooltip("镜头显示期间锁住玩家移动和视角，避免玩家在过场里走开")]
        [SerializeField] private bool m_LockPlayerControl = true;

        [Tooltip("镜头 Depth 比主摄像机高多少。一般 20 就够，越大越晚渲染")]
        [SerializeField] private int m_DepthBoost = 20;

        [Header("事件")]
        [Tooltip("镜头切换到门边时")]
        [SerializeField] private UnityEvent m_OnStarted;

        [Tooltip("切回玩家、镜头结束时")]
        [SerializeField] private UnityEvent m_OnFinished;

        private static int s_PlayingCount;

        private bool m_Playing;
        private bool m_CameraWasEnabled;
        private bool m_CameraObjectWasActive;
        private float m_PreviousDepth;
        private AudioListener m_ShotListener;
        private bool m_ShotListenerWasEnabled;
        private PlayerController m_Player;
        private bool m_PlayerWasLocked;
        private bool m_LockedPlayer;
        private Coroutine m_Routine;

        /// <summary>当前有没有门边镜头在播。交互提示和拉杆会靠它闭嘴。</summary>
        public static bool IsAnyPlaying
        {
            get { return s_PlayingCount > 0; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_PlayingCount = 0;
        }

        private void Awake()
        {
            if (m_ShotCamera == null)
            {
                m_ShotCamera = GetComponent<Camera>();
            }

            if (m_ShotCamera == null)
            {
                m_ShotCamera = GetComponentInChildren<Camera>(true);
            }

            if (m_ShotCamera == null)
            {
                GameObject cameraObject = new GameObject("Mechanism Shot Camera");
                cameraObject.transform.SetParent(transform, false);
                m_ShotCamera = cameraObject.AddComponent<Camera>();
                m_ShotCamera.enabled = false;
            }
        }

        private void OnDisable()
        {
            if (m_Playing)
            {
                StopShotInternal(false);
            }
        }

        private void OnDestroy()
        {
            if (m_Playing)
            {
                StopShotInternal(false);
            }
        }

        /// <summary>切到门边镜头。已经在播时忽略。</summary>
        public void Play()
        {
            if (m_Playing || m_ShotCamera == null || !isActiveAndEnabled)
            {
                return;
            }

            Camera gameplayCamera = Camera.main;

            if (m_ShotCamera == gameplayCamera || m_ShotCamera.CompareTag("MainCamera"))
            {
                Debug.LogWarning("[MechanismCameraShot] 门边镜头不要用主摄像机（MainCamera）。" +
                                 "请另外放一台 Tag 为 Untagged 的相机，否则切镜头会干扰 Camera.main 和玩家视角。", this);
            }

            // 记住摄像机原本的状态，结束时还原
            m_CameraObjectWasActive = m_ShotCamera.gameObject.activeSelf;
            m_CameraWasEnabled = m_ShotCamera.enabled;
            m_PreviousDepth = m_ShotCamera.depth;

            if (!m_CameraObjectWasActive)
            {
                m_ShotCamera.gameObject.SetActive(true);
            }

            m_ShotCamera.enabled = true;
            m_ShotCamera.depth = (gameplayCamera != null ? gameplayCamera.depth : 0f) + m_DepthBoost;

            // 两台摄像机同时开着时，两个 AudioListener 会刷警告，也会叠音
            m_ShotListener = m_ShotCamera.GetComponent<AudioListener>();
            if (m_ShotListener != null)
            {
                m_ShotListenerWasEnabled = m_ShotListener.enabled;
                m_ShotListener.enabled = false;
            }

            PoseCamera();
            LockPlayer();

            m_Playing = true;
            s_PlayingCount++;
            m_OnStarted?.Invoke();

            if (m_Playing && m_Duration > 0.0001f)
            {
                m_Routine = StartCoroutine(PlayRoutine());
            }
        }

        /// <summary>立刻结束镜头并切回玩家。没在播时什么也不做。</summary>
        public void StopShot()
        {
            StopShotInternal(true);
        }

        private IEnumerator PlayRoutine()
        {
            yield return new WaitForSeconds(m_Duration);

            if (m_ReturnToGameplay)
            {
                StopShotInternal(true);
            }
            else
            {
                // 不自动切回：镜头保持，但协程结束，之后可以随时 StopShot。
                m_Routine = null;
            }
        }

        private void PoseCamera()
        {
            if (m_ShotPoint != null)
            {
                m_ShotCamera.transform.SetPositionAndRotation(m_ShotPoint.position, m_ShotPoint.rotation);
            }

            if (m_LookAt != null)
            {
                Vector3 direction = m_LookAt.position - m_ShotCamera.transform.position;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    m_ShotCamera.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                }
            }
        }

        private void LockPlayer()
        {
            if (!m_LockPlayerControl)
            {
                return;
            }

            m_Player = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Exclude);
            if (m_Player == null)
            {
                return;
            }

            m_PlayerWasLocked = m_Player.ControlLocked;
            m_Player.ControlLocked = true;
            m_LockedPlayer = true;
        }

        private void UnlockPlayer()
        {
            if (!m_LockedPlayer || m_Player == null)
            {
                return;
            }

            m_Player.ControlLocked = m_PlayerWasLocked;
            m_LockedPlayer = false;
        }

        private void StopShotInternal(bool invokeFinished)
        {
            if (!m_Playing)
            {
                return;
            }

            if (m_Routine != null)
            {
                StopCoroutine(m_Routine);
                m_Routine = null;
            }

            UnlockPlayer();

            if (m_ShotCamera != null)
            {
                m_ShotCamera.depth = m_PreviousDepth;
                m_ShotCamera.enabled = m_CameraWasEnabled;

                if (!m_CameraObjectWasActive && m_ShotCamera.gameObject != gameObject)
                {
                    m_ShotCamera.gameObject.SetActive(false);
                }
            }

            if (m_ShotListener != null)
            {
                m_ShotListener.enabled = m_ShotListenerWasEnabled;
                m_ShotListener = null;
            }

            m_Playing = false;
            s_PlayingCount = Mathf.Max(0, s_PlayingCount - 1);

            if (invokeFinished)
            {
                m_OnFinished?.Invoke();
            }
        }

        private void OnDrawGizmosSelected()
        {
            Camera shot = m_ShotCamera != null ? m_ShotCamera : GetComponent<Camera>();
            if (shot == null)
            {
                return;
            }

            Gizmos.color = new Color(0.25f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireCube(shot.transform.position, Vector3.one * 0.2f);

            if (m_LookAt != null)
            {
                Gizmos.DrawLine(shot.transform.position, m_LookAt.position);
            }

            if (m_ShotPoint != null)
            {
                Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
                Gizmos.DrawWireSphere(m_ShotPoint.position, 0.15f);
                Gizmos.DrawLine(m_ShotPoint.position, m_ShotPoint.position + m_ShotPoint.forward * 0.8f);
            }
        }
    }
}

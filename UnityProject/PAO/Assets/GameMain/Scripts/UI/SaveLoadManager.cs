using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.UI
{
    /// <summary>
    /// 存档 / 读档的总入口（运行时自动创建，不需要往场景里挂任何东西）。
    ///
    /// 它负责四件事：
    ///   1. 建出 <see cref="SaveLoadPanel"/>（面板本身也是代码生成的）
    ///   2. 订阅 <see cref="SaveSystem.SlotLoaded"/> —— 读档后真正切场景 + 传送玩家。
    ///      这个订阅方原本不存在，是「点了读档没反应」的根因。
    ///   3. 把暂停菜单里「读档」按钮接到本系统（cs 场景原本没有 LoadPanel，
    ///      那个按钮只打了一条"暂不可用"的日志）
    ///   4. 读档目标是别的场景时，跨场景交接玩家坐标（切场景后传送）
    ///
    /// 【为什么用 DontDestroyOnLoad】
    /// 暂停菜单在每个关卡场景里各有一份，而本管理器只需要一份。
    /// 做成常驻后，跨场景读档时它不会跟着被销毁，才能完成"切完场景再传送"。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/UI/Save Load Manager")]
    public sealed class SaveLoadManager : MonoBehaviour
    {
        /// <summary>全局单例。场景里没有时由 <see cref="SaveLoadBootstrap"/> 建出来。</summary>
        public static SaveLoadManager Instance { get; private set; }

        /// <summary>存档/读档面板。第一次用到时创建。</summary>
        public SaveLoadPanel Panel
        {
            get
            {
                if (m_Panel == null)
                {
                    m_Panel = SaveLoadPanel.Create(transform);
                    m_Panel.LoadRequested += OnLoadRequested;
                    m_Panel.Closed += OnPanelClosed;
                }

                return m_Panel;
            }
        }

        private SaveLoadPanel m_Panel;

        /// <summary>
        /// 本面板自己触发的读档正在处理中。
        ///
        /// LoadFromSlot 会先抛 LoadRequested（本类已处理），再调 SaveSystem.NotifyLoaded，
        /// 于是 OnSlotLoaded 会被再调一次。用这个开关跳过第二次，避免重复切场景。
        /// </summary>
        private bool m_HandlingOwnLoad;

        /// <summary>跨场景读档时，切完场景要传送到的位置。</summary>
        private bool m_PendingTeleport;
        private Vector3 m_PendingPosition;

        /// <summary>暂停菜单（用于面板关闭后把暂停界面重新显示出来）。</summary>
        private PauseMenu m_PauseMenu;

        // ==================== 生命周期 ====================

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            SaveSystem.SlotLoaded += OnSlotLoaded;          // 兜住别的面板（如 Menu 里的 LoadPanel）
            SceneManager.sceneLoaded += OnSceneLoaded;

            // 面板先建好，避免第一次打开时有建 UI 的卡顿
            SaveLoadPanel panel = Panel;
            panel.Close();

            Debug.Log("[SaveLoadManager] 存档系统已启动。", this);
        }

        private void OnDestroy()
        {
            SaveSystem.SlotLoaded -= OnSlotLoaded;
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (m_Panel != null)
            {
                m_Panel.LoadRequested -= OnLoadRequested;
                m_Panel.Closed -= OnPanelClosed;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            // 暂停菜单是每个场景各一份，所以每帧确认一下按钮有没有接上
            if (m_PauseMenu == null)
            {
                m_PauseMenu = FindPauseMenu();
                if (m_PauseMenu != null)
                {
                    HookPauseMenu(m_PauseMenu);
                }
            }
        }

        // ==================== 对外接口 ====================

        /// <summary>打开面板（两个按钮都显示）。调试用。</summary>
        public void OpenPanel()
        {
            Panel.Open(SaveLoadPanel.PanelMode.Both);
        }

        /// <summary>以「只能存档」模式打开。暂停菜单的「存档」按钮走这里。</summary>
        public void OpenSavePanel()
        {
            Panel.Open(SaveLoadPanel.PanelMode.SaveOnly);
        }

        /// <summary>以「只能读档」模式打开。暂停菜单的「读档」按钮走这里。</summary>
        public void OpenLoadPanel()
        {
            Panel.Open(SaveLoadPanel.PanelMode.LoadOnly);
        }

        /// <summary>关掉面板。</summary>
        public void ClosePanel()
        {
            if (m_Panel != null)
            {
                m_Panel.RequestClose();
            }
        }

        /// <summary>存档到指定槽位（供外部按钮调用）。</summary>
        public bool SaveToSlot(int slot)
        {
            return Panel.SaveToSlot(slot);
        }

        /// <summary>读取指定槽位（供外部按钮调用）。</summary>
        public bool LoadFromSlot(int slot)
        {
            return Panel.LoadFromSlot(slot);
        }

        // ==================== 暂停菜单接线 ====================

        private static PauseMenu FindPauseMenu()
        {
#if UNITY_2022_2_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<PauseMenu>(FindObjectsInactive.Include);
#else
            return UnityEngine.Object.FindObjectOfType<PauseMenu>(true);
#endif
        }

        /// <summary>
        /// 把暂停菜单里「存档」「读档」两个按钮分别接到对应的模式上。
        ///
        /// PauseMenu.OpenLoadPanel() 依赖一个 LoadPanel 引用，而 cs 场景里没有 LoadPanel，
        /// 所以那些按钮原本只会打一条日志。这里按按钮名改写它们的 onClick：
        ///   「存档」→ 只存档模式
        ///   「读档」→ 只读档模式
        /// 两者互斥，防止手滑覆盖存档点。
        /// </summary>
        private void HookPauseMenu(PauseMenu pauseMenu)
        {
            Button[] buttons = pauseMenu.GetComponentsInChildren<Button>(true);

            int hooked = 0;

            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                string name = button.gameObject.name;

                if (name.IndexOf("存档", System.StringComparison.Ordinal) >= 0)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(OpenSavePanel);
                    hooked++;
                    Debug.Log("[SaveLoadManager] 暂停菜单「" + name + "」→ 只能存档。", this);
                    continue;
                }

                if (name.IndexOf("读档", System.StringComparison.Ordinal) >= 0)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(OpenLoadPanel);
                    hooked++;
                    Debug.Log("[SaveLoadManager] 暂停菜单「" + name + "」→ 只能读档。", this);
                    continue;
                }

                // 英文命名的兼容（Load / Save）。注意 Save 要放在 Load 之后判断，
                // 否则 "Save" 会先被 "Load" 之外的分支漏掉；这里两个都独立判断所以没问题。
                if (name.IndexOf("Load", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(OpenLoadPanel);
                    hooked++;
                    continue;
                }

                if (name.IndexOf("Save", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(OpenSavePanel);
                    hooked++;
                }
            }

            if (hooked == 0)
            {
                Debug.LogWarning("[SaveLoadManager] 暂停菜单里没找到「存档」或「读档」按钮，" +
                                 "存档功能暂时没有入口。", this);
            }
        }

        private void OnPanelClosed()
        {
            // 面板是叠在暂停界面之上的，关掉后暂停界面应该还在。
            // 如果暂停界面不知为何被关了，这里不动它 —— 由 PauseMenu 自己管状态。
        }

        // ==================== 读档 ====================

        private void OnLoadRequested(SaveSlotData data)
        {
            if (data == null)
            {
                return;
            }

            // 标记：这次读档是本面板发起的，紧接着 OnSlotLoaded 会再被调一次，跳过它
            m_HandlingOwnLoad = true;

            try
            {
                ApplyLoad(data);
            }
            finally
            {
                m_HandlingOwnLoad = false;
            }
        }

        private void OnSlotLoaded(SaveSlotData data)
        {
            // 这条路是给 Menu 场景里的 LoadPanel 用的（它只抛事件、自己不处理）。
            //
            // 本面板走的是 LoadRequested，而 LoadFromSlot 里先抛 LoadRequested、
            // 再调 NotifyLoaded —— 也就是说轮到本回调时 ApplyLoad 已经跑过一次了。
            // 用「面板刚刚是否处于打开状态」区分不出这两种来源，所以这里加一个
            // 一次性开关：由本面板自己触发的读档，不再重复处理。
            if (m_HandlingOwnLoad)
            {
                return;
            }

            ApplyLoad(data);
        }

        /// <summary>
        /// 真正执行读档：切场景（如果需要）+ 传送玩家。
        /// </summary>
        private void ApplyLoad(SaveSlotData data)
        {
            if (data == null || !data.IsValid)
            {
                return;
            }

            Vector3 target = new Vector3(data.posX, data.posY, data.posZ);
            string current = SceneManager.GetActiveScene().name;

            // 存档里没记场景，或者就是当前场景 → 直接传送
            if (string.IsNullOrEmpty(data.sceneName) || data.sceneName == current)
            {
                // 读完要退出暂停状态，否则游戏还冻着
                ResumeIfPaused();
                TeleportPlayer(target);
                return;
            }

            // 要换场景 → 记下坐标，等切完场景再传送
            m_PendingTeleport = true;
            m_PendingPosition = target;

            ResumeIfPaused();

            if (Application.CanStreamedLevelBeLoaded(data.sceneName))
            {
                Debug.Log("[SaveLoadManager] 读档切场景：" + data.sceneName, this);
                Time.timeScale = 1f;
                SceneManager.LoadScene(data.sceneName);
            }
            else
            {
                m_PendingTeleport = false;
                Debug.LogError("[SaveLoadManager] 存档里的场景 \"" + data.sceneName +
                               "\" 不在 Build Settings 里，无法加载。", this);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!m_PendingTeleport)
            {
                return;
            }

            m_PendingTeleport = false;
            TeleportPlayer(m_PendingPosition);
        }

        /// <summary>如果还处于暂停状态，恢复游戏。</summary>
        private void ResumeIfPaused()
        {
            if (Time.timeScale == 0f)
            {
                Time.timeScale = 1f;
            }

            if (m_PauseMenu != null && m_PauseMenu.IsOpen)
            {
                m_PauseMenu.Close();
            }
        }

        /// <summary>
        /// 把玩家挪到指定坐标。
        ///
        /// CharacterController 会覆盖 transform.position 的写入，所以必须先关掉它，
        /// 移动完再打开 —— 否则传送会"被弹回原地"。
        /// </summary>
        private static void TeleportPlayer(Vector3 position)
        {
            PlayerController player = UnityEngine.Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Exclude);

            if (player == null)
            {
                Debug.LogWarning("[SaveLoadManager] 场景里没找到 PlayerController，无法传送玩家。");
                return;
            }

            CharacterController controller = player.GetComponent<CharacterController>();

            if (controller != null)
            {
                controller.enabled = false;
                player.transform.position = position;
                controller.enabled = true;
            }
            else
            {
                player.transform.position = position;
            }

            Debug.Log("[SaveLoadManager] 玩家已传送到 " + position);
        }
    }

    /// <summary>
    /// 让存档系统在没有被挂到任何场景的情况下也能跑起来。
    ///
    /// 用 RuntimeInitializeOnLoadMethod 而不是往场景里放物体 ——
    /// 这样 cs.unity / Map0~Map5 都不需要改，任何场景进 Play 都自动有这个系统。
    /// </summary>
    public static class SaveLoadBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureManager()
        {
            if (SaveLoadManager.Instance != null)
            {
                return;
            }

            var go = new GameObject("[SaveLoad Manager]");
            go.AddComponent<SaveLoadManager>();

            Debug.Log("[SaveLoadBootstrap] 存档系统已就绪（按 Esc → 暂停菜单 → 读档/存档）。");
        }
    }
}

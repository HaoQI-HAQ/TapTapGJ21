using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.UI
{
    /// <summary>
    /// 暂停菜单控制器：挂在一张全屏 Canvas 上。
    ///
    /// 负责两件事：
    ///   1. 暂停/恢复游戏时间（Time.timeScale），进入页面时暂停
    ///   2. 承接各个按钮的点击（继续 / 地图 / 重开 / 读档 / 设置 / 主菜单）
    ///
    /// 「读档」和「设置」是可选接线：场景里如果放了 LoadPanel / SettingsPanel，
    /// 构建脚本会把它们接上；没有就只打一条提示，不影响其它按钮。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/UI/Pause Menu")]
    public sealed class PauseMenu : MonoBehaviour
    {
        [Header("行为")]
        [Tooltip("进入这个界面时把时间暂停（Time.timeScale = 0）。" +
                 "注意：这个界面现在是「按 Esc 才出现」的，所以这里应当保持关闭，否则一进游戏就暂停")]
        [SerializeField] private bool m_PauseTimeOnEnable = false;

        [Tooltip("恢复游戏时把时间恢复到多少")]
        [SerializeField] private float m_ResumeTimeScale = 1f;

        [Tooltip("主菜单场景名")]
        [SerializeField] private string m_MainMenuScene = "Menu";

        [Tooltip("打开这个界面时把时间暂停")]
        [SerializeField] private bool m_PauseTimeOnOpen = true;

        [Header("可选面板")]
        [Tooltip("读档面板。留空则「读档」按钮只打日志")]
        [SerializeField] private LoadPanel m_LoadPanel;

        [Tooltip("设置面板。留空则「设置」按钮只打日志")]
        [SerializeField] private SettingsPanel m_SettingsPanel;

        [Header("提示")]
        [Tooltip("「最近保存」那行小字，留空则不更新")]
        [SerializeField] private TMPro.TMP_Text m_LastSaveLabel;

        /// <summary>场景里当前活动的暂停菜单。</summary>
        public static PauseMenu Instance { get; private set; }

        private float m_PreviousTimeScale = 1f;
        private bool m_PausedTime;
        private bool m_IsOpen;

        /// <summary>
        /// 暂停界面当前是否显示。
        /// 面板未激活时它自己的 Update 不会跑，所以 Esc 由常驻的 BubbleTypeHud 收，
        /// 靠这个属性判断该开还是该关。
        /// </summary>
        public bool IsOpen
        {
            get { return m_IsOpen; }
        }

        private void Awake()
        {
            Instance = this;

            if (m_PauseTimeOnEnable)
            {
                Open();
            }

            RefreshLastSaveLabel();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            // 离开这个界面时一定要把时间放回去，
            // 否则回到游戏会「画面冻住」而看不出原因。
            if (m_PausedTime)
            {
                Time.timeScale = m_ResumeTimeScale;
                m_PausedTime = false;
            }
        }

        // ==================== 开关 ====================

        /// <summary>
        /// 显示暂停界面（幂等）。
        /// 幂等很重要：面板未激活时每次 SetActive(true) 都会触发一次 Awake，
        /// 所以不能靠 Awake 维护状态，必须这里自己对齐。
        /// </summary>
        public bool Open()
        {
            m_IsOpen = true;
            gameObject.SetActive(true);

            if (m_PauseTimeOnOpen)
            {
                PauseTime();
            }

            RefreshLastSaveLabel();
            Debug.Log("[PauseMenu] 暂停界面已打开。", this);
            return true;
        }

        /// <summary>隐藏暂停界面（幂等），并把时间放回去。</summary>
        public bool Close()
        {
            m_IsOpen = false;
            ResumeTime();
            gameObject.SetActive(false);
            return false;
        }

        /// <summary>切换显示，返回切换后是否处于打开状态。Esc 用它。</summary>
        public bool Toggle()
        {
            if (m_IsOpen)
            {
                return Close();
            }

            return Open();
        }

        // ==================== 按钮回调 ====================

        /// <summary>继续游戏：恢复时间并关掉这个界面。</summary>
        public void Resume()
        {
            Close();
        }

        /// <summary>重开当前关卡：恢复时间并重新加载当前场景。</summary>
        public void RestartLevel()
        {
            ResumeTime();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        /// <summary>回到主菜单。</summary>
        public void GoToMainMenu()
        {
            ResumeTime();
            SceneManager.LoadScene(m_MainMenuScene);
        }

        /// <summary>打开地图（还没做，先打日志占位）。</summary>
        public void OpenMap()
        {
            Debug.Log("[PauseMenu] 「地图」还没有实现。", this);
        }

        /// <summary>打开读档面板。</summary>
        public void OpenLoadPanel()
        {
            if (m_LoadPanel == null)
            {
                Debug.Log("[PauseMenu] 场景里没有 LoadPanel，「读档」按钮暂不可用。" +
                          "把 Menu 场景里的读档面板做成 Prefab 放进本场景即可。", this);
                return;
            }

            m_LoadPanel.Open();
        }

        /// <summary>打开设置面板。</summary>
        public void OpenSettingsPanel()
        {
            if (m_SettingsPanel == null)
            {
                Debug.Log("[PauseMenu] 场景里没有 SettingsPanel，「设置」按钮暂不可用。", this);
                return;
            }

            m_SettingsPanel.Open();
        }

        // ==================== 时间 ====================

        /// <summary>暂停游戏时间。</summary>
        public void PauseTime()
        {
            if (m_PausedTime)
            {
                return;
            }

            m_PreviousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            m_PausedTime = true;
        }

        /// <summary>恢复游戏时间。</summary>
        public void ResumeTime()
        {
            if (!m_PausedTime)
            {
                return;
            }

            Time.timeScale = m_PreviousTimeScale > 0f ? m_PreviousTimeScale : m_ResumeTimeScale;
            m_PausedTime = false;
        }

        /// <summary>把「最近保存」那行字刷新成最近一条存档的时间。</summary>
        public void RefreshLastSaveLabel()
        {
            if (m_LastSaveLabel == null)
            {
                return;
            }

            SaveSlotData newest = null;
            for (int i = 0; i < SaveSystem.DefaultSlotCount; i++)
            {
                SaveSlotData data = SaveSystem.LoadSlot(i);
                if (data == null)
                {
                    continue;
                }

                if (newest == null || string.CompareOrdinal(data.savedAtUtc, newest.savedAtUtc) > 0)
                {
                    newest = data;
                }
            }

            m_LastSaveLabel.text = newest != null ? newest.SavedAtText : "还没有存档";
        }
    }
}

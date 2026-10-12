using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.UI
{
    /// <summary>
    /// 暂停菜单控制器：挂在一张全屏 Canvas 上。
    ///
    /// 负责两件事：
    ///   1. 暂停 / 恢复游戏：Time.timeScale = 0，并把所有玩法组件 enabled = false
    ///   2. 承接各个按钮的点击（继续 / 地图 / 重开 / 读档 / 设置 / 主菜单）
    ///
    /// 【为什么光有 timeScale = 0 不够】
    /// timeScale 只把 Time.deltaTime 变成 0，并**不会**阻止 Update() 执行，
    /// 也不影响 Input。所以像 BubbleLauncher 那种
    /// 「if (Input.GetMouseButtonDown(0)) BeginCharge();」的代码会照样生成泡泡，
    /// 表现就是「弹出了暂停界面，但游戏没停」。
    ///
    /// 因此这里额外做一件事：暂停时把场景里所有**玩法**组件（PAO 命名空间下、
    /// 但不在 PAO.UI 里的 MonoBehaviour）逐个 enabled = false，恢复时按原样还原。
    /// 这样不用改任何玩法脚本，就能真正冻住当前游戏。
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

        /// <summary>被冻结的玩法组件，恢复时逐个还原。</summary>
        private readonly List<MonoBehaviour> m_DisabledComponents = new List<MonoBehaviour>();

        /// <summary>
        /// 暂停界面当前是否显示。
        /// 面板未激活时它自己的 Update 不会跑，所以 Esc 由常驻的 BubbleTypeHud 收，
        /// 靠这个属性判断该开还是该关。
        /// </summary>
        public bool IsOpen
        {
            get { return m_IsOpen; }
        }

        /// <summary>
        /// 首次初始化。把面板恢复成「关闭」的正确状态。
        ///
        /// 本组件挂在暂停面板上，而面板在场景里是未激活的；第一次 Open() 时的
        /// SetActive(true) 才触发这个 Awake。所以这里不能无条件把自己关掉 ——
        /// 那是 Open() 刚点亮又被自己关掉。
        /// </summary>
        private void Awake()
        {
            Instance = this;

            // 初始对齐：面板本来是被 Open() 打开的，此刻 m_IsOpen 已经由 Open() 置为 true；
            // 直接进游戏（面板在场景里被强制激活）时它是 false，需要收敛成"看不见"。
            //
            // 用 CanvasGroup 的 alpha 来藏，而不是 gameObject.SetActive(false) —— 后者
            // 会把挂在同一物体上的 Image 等组件一起停掉，Inspector 里看着就像它们被关了。
            if (!m_IsOpen && Application.isPlaying)
            {
                SetVisible(false);
            }

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

            // 离开时一定要解冻 + 把时间放回去，
            // 否则回到游戏会「画面冻住 / 组件全是关的」而看不出原因。
            if (m_PausedTime)
            {
                UnfreezeGameplay();
                Time.timeScale = m_ResumeTimeScale;
                m_PausedTime = false;
            }
            else
            {
                // 万一 timeScale 被别的路径改过，这里兜个底
                UnfreezeGameplay();
            }
        }

        /// <summary>
        /// 兜底的 Esc 处理：面板显示期间由自己来收 Esc 关闭。
        ///
        /// 正常情况下 Esc 是由 BubbleTypeHud 转进来的，但那个组件万一被停掉
        /// （比如冻结逻辑误伤），玩家就再也退不出暂停 —— 只能重启 Play。
        /// 面板一旦显示，这个 Update 一定在跑（PauseMenu 永远不会被冻结），
        /// 所以这里再收一次做保险。
        /// </summary>
        private void Update()
        {
            if (m_IsOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
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

            // 只 SetActive(true) 自己是不够的：如果父链上任何一层是关的，
            // activeInHierarchy 仍然是 false，界面就渲染不出来 ——
            // 表现正好是「timeScale 生效了、游戏冻住了，但看不见任何界面」。
            // 所以这里顺手把父链补活。
            ActivateSelfAndParents();

            SetVisible(true);

            Debug.Log("[PauseMenu] Open() 被调用。本物体=" + gameObject.name +
                      " activeSelf=" + gameObject.activeSelf +
                      " activeInHierarchy=" + gameObject.activeInHierarchy +
                      " canvasGroupAlpha=" + (Group != null ? Group.alpha : -1f) +
                      "\n  父链: " + DescribeParentChain(transform), this);

            if (m_PauseTimeOnOpen)
            {
                PauseTime();
            }

            RefreshLastSaveLabel();
            Debug.Log("[PauseMenu] 暂停界面已打开。", this);
            return true;
        }

        /// <summary>把关着的父物体逐级打开，保证界面能显示。</summary>
        private void ActivateSelfAndParents()
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            // 逐级向上，把关着的父物体打开（临时改动的层级，
            // 只为了让界面能显示出来；正常情况下这一圈什么都不做）
            Transform t = transform.parent;
            int guard = 0;
            while (t != null && guard++ < 16)
            {
                if (!t.gameObject.activeSelf)
                {
                    Debug.LogWarning("[PauseMenu] 父物体 \"" + t.name +
                                     "\" 原本是关闭的，已自动打开 —— 否则暂停界面无法显示。", this);
                    t.gameObject.SetActive(true);
                }

                t = t.parent;
            }
        }

        /// <summary>拼出面板到根的激活状态，方便一眼看出是哪一层关了。</summary>
        private static string DescribeParentChain(Transform start)
        {
            var sb = new System.Text.StringBuilder();
            Transform t = start;

            int guard = 0;
            while (t != null && guard++ < 16)
            {
                sb.Append(t.name);
                sb.Append(t.gameObject.activeSelf ? "(on)" : "(OFF)");
                t = t.parent;
                if (t != null)
                {
                    sb.Append(" <- ");
                }
            }

            return sb.ToString();
        }

        /// <summary>隐藏暂停界面（幂等），并把时间放回去。</summary>
        public bool Close()
        {
            m_IsOpen = false;
            ResumeTime();

            // 用 CanvasGroup 藏起来，而不是 gameObject.SetActive(false)。
            //
            // SetActive(false) 会把整个物体关掉，Inspector 里挂在上面的所有组件
            // （Image、CanvasRenderer、PauseMenu）全部变灰 —— 看起来就像"Image 也被关了"，
            // 而且 PauseMenu 自己停了，下次就没有 Update 来收 Esc。
            // 改成 alpha=0 后物体始终激活，只有画面消失。
            SetVisible(false);
            return false;
        }

        // ==================== 显隐 ====================

        private CanvasGroup m_Group;
        private bool m_GroupResolved;

        /// <summary>
        /// 控制显隐用的 CanvasGroup。没有就自动挂一个 ——
        /// 这样不改预制体也能保证「藏起来只是变透明，而不是关掉整个物体」。
        /// </summary>
        private CanvasGroup Group
        {
            get
            {
                if (!m_GroupResolved)
                {
                    m_GroupResolved = true;
                    m_Group = GetComponent<CanvasGroup>();

                    if (m_Group == null)
                    {
                        m_Group = gameObject.AddComponent<CanvasGroup>();
                        Debug.Log("[PauseMenu] 面板上没有 CanvasGroup，已自动挂一个（用于只改透明度地隐藏界面）。", this);
                    }
                }

                return m_Group;
            }
        }

        private void SetVisible(bool visible)
        {
            if (Group != null)
            {
                Group.alpha = visible ? 1f : 0f;
                Group.interactable = visible;
                Group.blocksRaycasts = visible;
                return;
            }

            // 没有 CanvasGroup 就退回 SetActive（旧行为，会关掉整个物体）
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
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

        // ==================== 时间 / 冻结 ====================

        /// <summary>暂停游戏：冻时间 + 冻住所有玩法组件。</summary>
        public void PauseTime()
        {
            if (m_PausedTime)
            {
                return;
            }

            m_PreviousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            m_PausedTime = true;

            FreezeGameplay();
        }

        /// <summary>恢复游戏：解冻玩法组件 + 把时间放回去。</summary>
        public void ResumeTime()
        {
            if (!m_PausedTime)
            {
                return;
            }

            UnfreezeGameplay();

            Time.timeScale = m_PreviousTimeScale > 0f ? m_PreviousTimeScale : m_ResumeTimeScale;
            m_PausedTime = false;
        }

        /// <summary>
        /// 把场景里所有玩法组件停掉。
        ///
        /// 判据：MonoBehaviour 落在 PAO 命名空间下、但**不是** PAO.UI 里的。
        /// 玩法脚本都在 PAO / PAO.Bubble 之类的命名空间，UI 脚本都在 PAO.UI，
        /// 所以这样能准确地把「玩法」和「界面」分开，而且以后新增玩法脚本会自动被覆盖，
        /// 不用回来维护白名单。
        ///
        /// 暂停菜单自己在 PAO.UI 里，所以不会被自己停掉 —— 否则「继续」按钮就点不了了。
        /// </summary>
        private void FreezeGameplay()
        {
            m_DisabledComponents.Clear();

            MonoBehaviour[] all = Object.FindObjectsOfType<MonoBehaviour>(true);
            var skippedUi = new List<string>();

            for (int i = 0; i < all.Length; i++)
            {
                MonoBehaviour mb = all[i];
                if (mb == null || !mb.enabled)
                {
                    continue;
                }

                if (IsUiComponent(mb))
                {
                    // 记下保留下来的界面组件，方便核对「收 Esc 的那个还在不在」
                    if (skippedUi.Count < 20)
                    {
                        skippedUi.Add(mb.GetType().Name + "@" + mb.gameObject.name);
                    }
                    continue;
                }

                // 记录「原本是开着的」，恢复时只把本来开着的打开，
                // 免得把玩法自己关掉的组件又点亮了
                m_DisabledComponents.Add(mb);
                mb.enabled = false;
            }

            Debug.Log("[PauseMenu] 已冻结 " + m_DisabledComponents.Count + " 个玩法组件；" +
                      "保留的界面组件 " + skippedUi.Count + " 个：" + string.Join(", ", skippedUi.ToArray()), this);
        }

        /// <summary>把 <see cref="FreezeGameplay"/> 停掉的组件原样打开。</summary>
        private void UnfreezeGameplay()
        {
            if (m_DisabledComponents.Count == 0)
            {
                return;
            }

            int restored = 0;
            for (int i = 0; i < m_DisabledComponents.Count; i++)
            {
                MonoBehaviour mb = m_DisabledComponents[i];
                if (mb == null)
                {
                    continue;   // 期间被销毁了（比如切场景）
                }

                mb.enabled = true;
                restored++;
            }

            m_DisabledComponents.Clear();
            Debug.Log("[PauseMenu] 已恢复 " + restored + " 个玩法组件。", this);
        }

        /// <summary>
        /// 这个组件是不是界面侧的（界面不该被冻结）。
        ///
        /// 【为什么用显式名单，而不是按命名空间判断】
        /// 原先的判据是「PAO 命名空间、但不是 PAO.UI」，结果踩了坑：
        /// BubbleTypeHud.cs 放在 Scripts/UI 目录下，命名空间却是 PAO，
        /// 而它正是**收 Esc 键**的那个组件。冻结时把它一起 enabled = false，
        /// 就再也没人监听 Esc —— 游戏冻住了、也退不出来，只能重启 Play。
        ///
        /// 所以这里改成明确列出不能被冻结的界面组件。以后新增界面组件，
        /// 往 m_NeverFreeze 里加一行即可；玩法脚本不用管，会自动被冻结。
        /// </summary>
        private static bool IsUiComponent(MonoBehaviour mb)
        {
            System.Type type = mb.GetType();

            // 暂停菜单自己必须保持运行，否则「继续」按钮失效
            if (type == typeof(PauseMenu))
            {
                return true;
            }

            for (int i = 0; i < m_NeverFreeze.Length; i++)
            {
                if (type == m_NeverFreeze[i])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 冻结时**必须保留**的界面组件。玩法脚本不要往这里加。
        ///
        /// 注意：这里只能列**当前工程里确实存在**的类型。
        /// 之前往这里加过一个被删掉的类（ControlKeyRow），会直接编译不过 ——
        /// 以后增删界面组件时记得回来同步这张表。
        ///
        /// BubbleTypeHud 是最关键的一条：它放在 Scripts/UI 目录下、命名空间却是 PAO，
        /// 而它是收 Esc 的入口。一旦被冻结，就再也没人监听 Esc，
        /// 游戏冻住了也退不出来，只能重启 Play。
        /// </summary>
        private static readonly System.Type[] m_NeverFreeze =
        {
            typeof(PAO.BubbleTypeHud),
            typeof(VolumeSlider),
            typeof(MenuButton),
            typeof(SettingsPanel),
            typeof(LoadPanel),
            typeof(LevelPathPanel),
            typeof(LevelSelectPanel),
            typeof(SettingsButtonOpener),
            typeof(LoadButtonOpener),
        };

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

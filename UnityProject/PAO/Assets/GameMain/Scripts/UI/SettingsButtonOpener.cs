using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI
{
    /// <summary>
    /// 挂在主菜单的「游戏设置」按钮上，点击后打开设置面板。
    ///
    /// 为什么不直接往 Button 的 OnClick 里拖 SettingsPanel.Open：
    ///   1. 面板初始是关闭的（SetActive(false)），没激活的物体在 Inspector 里
    ///      拖不进 OnClick 的槽位；
    ///   2. 走脚本可以顺便处理「面板是否已存在」的容错与聚焦。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    [AddComponentMenu("PAO/UI/Settings Button Opener")]
    public sealed class SettingsButtonOpener : MonoBehaviour
    {
        [Tooltip("要打开的设置面板。留空则在场景里自动查找")]
        [SerializeField] private SettingsPanel m_Panel;

        [Tooltip("面板打开后是否让按钮失去焦点，避免手柄方向键跑回主菜单")]
        [SerializeField] private bool m_ClearSelectionOnOpen = true;

        private void Awake()
        {
            Button button = GetComponent<Button>();
            button.onClick.AddListener(Open);
        }

        private void OnDestroy()
        {
            Button button = GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveListener(Open);
            }
        }

        /// <summary>打开设置面板。</summary>
        public void Open()
        {
            if (m_Panel == null)
            {
                m_Panel = FindObjectOfType<SettingsPanel>(true);
            }

            if (m_Panel == null)
            {
                Debug.LogWarning("[SettingsButtonOpener] 场景里找不到 SettingsPanel，设置面板打不开。", this);
                return;
            }

            // 面板自己会接管焦点（SelectFirstControl），这里先把焦点交出去，
            // 免得主菜单按钮在面板打开期间还保持「已选中」而不亮
            if (m_ClearSelectionOnOpen && UnityEngine.EventSystems.EventSystem.current != null)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }

            m_Panel.Open();
        }
    }
}

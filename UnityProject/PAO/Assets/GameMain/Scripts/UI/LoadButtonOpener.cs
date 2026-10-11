using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI
{
    /// <summary>
    /// 挂在主菜单的「Loading Button」上，点击后打开读档面板。
    ///
    /// 和 <see cref="SettingsButtonOpener"/> 同一套做法：面板初始是未激活的，
    /// 没法在 Inspector 里把 <c>Open()</c> 拖进 OnClick 槽位，所以用脚本注册。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    [AddComponentMenu("PAO/UI/Load Button Opener")]
    public sealed class LoadButtonOpener : MonoBehaviour
    {
        [Tooltip("要打开的读档面板。留空则在场景里自动查找（含未激活对象）")]
        [SerializeField] private LoadPanel m_Panel;

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

        /// <summary>打开读档面板。</summary>
        public void Open()
        {
            // 这条日志是排查「点了没反应」用的：能看到它就说明按钮点击已触发
            Debug.Log("[LoadButtonOpener] 点击收到，准备打开读档面板。", this);

            if (m_Panel == null)
            {
                m_Panel = FindObjectOfType<LoadPanel>(true);
            }

            if (m_Panel == null)
            {
                Debug.LogWarning("[LoadButtonOpener] 场景里找不到 LoadPanel，读档面板打不开。", this);
                return;
            }

            if (m_ClearSelectionOnOpen && UnityEngine.EventSystems.EventSystem.current != null)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }

            m_Panel.Open();
        }
    }
}

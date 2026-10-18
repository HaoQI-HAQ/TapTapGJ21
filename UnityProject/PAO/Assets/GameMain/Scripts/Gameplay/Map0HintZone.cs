using UnityEngine;

namespace PAO
{
    /// <summary>教学提示区：玩家走进来弹一句操作提示（UIRuntime Toast，缺失时退回字幕条）。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    [AddComponentMenu("PAO/关卡/教学提示区（Map0）")]
    public sealed class Map0HintZone : MonoBehaviour
    {
        [SerializeField] private string m_Text;
        [SerializeField] private bool m_Once = true;
        private bool m_Shown;

        private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }

        private void OnTriggerEnter(Collider other)
        {
            if (m_Shown && m_Once) return;
            if (other.GetComponentInParent<PlayerController>() == null) return;
            m_Shown = true;
            PAO.UI.V2.UIRuntime ui = PAO.UI.V2.UIRuntime.Instance;
            if (ui != null) ui.Toast(m_Text); else WengjieSubtitleOverlay.Show("提示", m_Text);
        }

        public void Configure(string text) { m_Text = text; }
    }
}

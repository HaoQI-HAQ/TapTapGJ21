using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed class UIAdaptiveButton : MonoBehaviour, ILayoutElement
    {
        private float lastWidth = -1;
        private int lastFontSize;
        private void LateUpdate()
        {
            Text label = GetComponentInChildren<Text>();
            float width = ((RectTransform)transform).rect.width;
            if (label == null || (Mathf.Abs(lastWidth - width) < 0.1f && lastFontSize == label.fontSize)) return;
            lastWidth = width; lastFontSize = label.fontSize;
            // Wrapped height is only reliable after the parent has assigned the final width.
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }
        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }
        public float minWidth { get { return 96; } }
        public float preferredWidth { get { return -1; } }
        public float flexibleWidth { get { return 1; } }
        public float minHeight { get { return 72; } }
        public float preferredHeight
        {
            get
            {
                Text label = GetComponentInChildren<Text>();
                return label == null ? 72 : Mathf.Max(72, label.preferredHeight + 16);
            }
        }
        public float flexibleHeight { get { return 0; } }
        public int layoutPriority { get { return 2; } }
    }
}

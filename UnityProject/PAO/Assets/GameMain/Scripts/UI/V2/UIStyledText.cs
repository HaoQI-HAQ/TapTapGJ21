using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed class UIStyledText : MonoBehaviour, ILayoutElement
    {
        private int baseSize;
        private bool themed;
        private UIConfig config;
        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }
        public float minWidth { get { return 0; } }
        public float preferredWidth { get { return -1; } }
        public float flexibleWidth { get { return 1; } }
        public float minHeight { get { return GetComponent<Text>().fontSize * 1.6f; } }
        public float preferredHeight { get { return Mathf.Max(minHeight, GetComponent<Text>().preferredHeight); } }
        public float flexibleHeight { get { return 0; } }
        public int layoutPriority { get { return 1; } }
        public void Configure(int size, bool themedColor, UIConfig theme)
        { baseSize = size; themed = themedColor; config = theme; Apply(); }
        private void OnEnable() { UISettings.Changed += Apply; Apply(); }
        private void OnDisable() { UISettings.Changed -= Apply; }
        private void Apply()
        {
            if (config == null || UISettings.Current == null) return;
            Text label = GetComponent<Text>(); label.fontSize = Mathf.RoundToInt(baseSize * UISettings.Current.textScale);
            if (themed) label.color = UISettings.Current.highContrast ? Color.black : config.ink;
            LayoutElement layout = GetComponent<LayoutElement>();
            if (layout != null) layout.minHeight = label.fontSize * 1.6f;
        }
    }
}

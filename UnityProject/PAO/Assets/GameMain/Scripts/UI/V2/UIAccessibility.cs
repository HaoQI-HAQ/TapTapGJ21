using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed class UISubtitleBackground : MonoBehaviour
    {
        private Color ink;
        public void Configure(Color value) { ink = value; Apply(); }
        private void OnEnable() { UISettings.Changed += Apply; Apply(); }
        private void OnDisable() { UISettings.Changed -= Apply; }
        private void Apply()
        {
            if (UISettings.Current == null) return;
            Color color = ink; color.a = UISettings.Current.subtitleBackground;
            GetComponent<Image>().color = color;
        }
    }

    public static class UIAbilityColor
    {
        private static readonly Color32[][] palettes = {
                new[] { new Color32(111, 235, 241, 255), new Color32(171, 157, 255, 255), new Color32(255, 153, 197, 255) },
                new[] { new Color32(0, 114, 178, 255), new Color32(230, 159, 0, 255), new Color32(204, 121, 167, 255) },
                new[] { new Color32(0, 114, 178, 255), new Color32(213, 94, 0, 255), new Color32(204, 121, 167, 255) },
                new[] { new Color32(0, 158, 115, 255), new Color32(213, 94, 0, 255), new Color32(204, 121, 167, 255) }
            };
        public static Color ColorFor(int ability)
        {
            int mode = Mathf.Clamp(UISettings.Current?.colorVision ?? 0, 0, palettes.Length - 1);
            return palettes[mode][Mathf.Clamp(ability, 0, 2)];
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    // AI artwork may replace these paper-derived skins after import; text remains live UI.
    internal static class UIHandDrawn
    {
        private static Sprite grain, button, bubble, selection;
        private static Sprite Load(string generated, string fallback) =>
            Resources.Load<Sprite>("UIHandDrawnGenerated/" + generated) ?? Resources.Load<Sprite>("UIHandDrawn/" + fallback);
        public static Sprite Grain => grain != null ? grain : grain = Load("paper-grain-v1", "paper-grain-v1");
        public static Sprite Button => button != null ? button : button = Load("button-painted-v1", "button-paper-v1");
        public static Sprite Bubble => bubble != null ? bubble : bubble = Load("bubble-painted-v1", "bubble-paper-v1");
        public static void Apply(UIConfig config)
        {
            if (Button != null) config.round = Button;
            if (selection == null) selection = Resources.Load<Sprite>("UIHandDrawn/selection-brush-v1");
            if (selection != null) config.ring = selection;
        }
        public static void Backdrop(UIWidgets ui, Transform parent)
        {
            if (Grain == null) return;
            Image paper = ui.Surface("Paper fibers", parent, Color.white);
            paper.sprite = Grain; paper.type = Image.Type.Tiled; paper.pixelsPerUnitMultiplier = .5f;
            Color tint = ui.Config.paper;
            tint.a = ui.Config.paperStyle ? .16f : .045f;
            paper.color = tint;
            paper.raycastTarget = false; UIWidgets.Stretch(paper.rectTransform);
            paper.gameObject.AddComponent<UIQuietTexture>();
        }
        public static Sprite Ability(int index) => Resources.Load<Sprite>("UIHandDrawnGenerated/" +
            (index == 0 ? "sticky-painted-v1" : index == 1 ? "bouncy-painted-v1" : "bomb-painted-v1"));
        public static void Pigment(UIWidgets ui, Transform parent)
        {
            if (Bubble == null) return;
            Image pigment = ui.Surface("Pigment texture", parent, new Color(1, 1, 1, .18f));
            pigment.sprite = Bubble; pigment.preserveAspect = true; pigment.raycastTarget = false;
            UIWidgets.Stretch(pigment.rectTransform, 2);
            pigment.gameObject.AddComponent<UIQuietTexture>();
        }
    }
    internal sealed class UIQuietTexture : MonoBehaviour
    {
        private void OnEnable() { UISettings.Changed += Refresh; Refresh(); }
        private void OnDisable() { UISettings.Changed -= Refresh; }
        private void Refresh() { GetComponent<Image>().enabled = !UISettings.Current.highContrast; }
    }
}

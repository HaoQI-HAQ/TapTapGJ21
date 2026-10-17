using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    /// <summary>A full-screen loading curtain independent of page stacks, safe areas and scene lifetimes.</summary>
    public sealed class UIBubbleTransition : MonoBehaviour
    {
        private CanvasGroup curtain;
        private UILoadingBubbleField field;
        private Text status;
        private Image progress;
        private float beganAt;
        private int generation;
        public bool IsVisible => gameObject.activeSelf;

        public static UIBubbleTransition Create(Transform parent, UIConfig config)
        {
            var ui = new UIWidgets(config);
            RectTransform root = ui.Rect("Black bubble transition", parent);
            Canvas canvas = root.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 3000;
            CanvasScaler scaler = root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 1;
            root.gameObject.AddComponent<GraphicRaycaster>();
            var transition = root.gameObject.AddComponent<UIBubbleTransition>();
            transition.curtain = root.gameObject.AddComponent<CanvasGroup>();
            Image black = ui.Surface("Opaque black", root, Color.black);
            UIWidgets.Stretch(black.rectTransform); black.raycastTarget = true;
            transition.field = ui.Rect("Buoyant membranes", root).gameObject.AddComponent<UILoadingBubbleField>();
            UIWidgets.Stretch(transition.field.rectTransform); transition.field.raycastTarget = false;
            transition.status = ui.Label(root, "", 18, new Color(.7f, .7f, .7f), TextAnchor.MiddleCenter);
            RectTransform label = transition.status.rectTransform;
            label.anchorMin = new Vector2(.1f, .08f); label.anchorMax = new Vector2(.9f, .08f);
            label.sizeDelta = new Vector2(0, 38); label.anchoredPosition = Vector2.zero;
            Image track = ui.Surface("Load progress track", root, new Color(1, 1, 1, .12f));
            track.raycastTarget = false;
            track.rectTransform.anchorMin = track.rectTransform.anchorMax = new Vector2(.5f, .065f);
            track.rectTransform.sizeDelta = new Vector2(160, 1);
            transition.progress = ui.Surface("Load progress", track.transform, new Color(.8f, .8f, .8f));
            transition.progress.raycastTarget = false;
            UIWidgets.Stretch(transition.progress.rectTransform);
            root.gameObject.SetActive(false);
            return transition;
        }

        public void Begin(bool opaque)
        {
            generation++;
            gameObject.SetActive(true); beganAt = Time.unscaledTime;
            curtain.alpha = opaque ? 1 : 0;
            curtain.blocksRaycasts = true; curtain.interactable = true;
            field.ResetField(); SetProgress(0);
        }

        public void HoldOpaque()
        {
            if (!IsVisible) { Begin(true); return; }
            generation++; curtain.alpha = 1;
            curtain.blocksRaycasts = true; curtain.interactable = true;
        }

        public void SetProgress(float value)
        {
            progress.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
            status.text = value >= .99f ? UISettings.Text("准备就绪", "Ready") :
                UISettings.Text("正在加载", "Loading") + " · " + Mathf.RoundToInt(Mathf.Clamp01(value) * 100) + "%";
        }

        public IEnumerator Cover()
        {
            yield return Fade(1, UISettings.Current.reducedMotion ? .08f : .18f, generation);
        }

        public IEnumerator Reveal()
        {
            int ticket = generation;
            // A brief readable beat avoids a flash on cached loads; never prolong reduced-motion transitions.
            while (ticket == generation && !UISettings.Current.reducedMotion && Time.unscaledTime - beganAt < .65f) yield return null;
            if (ticket != generation) yield break;
            SetProgress(1);
            yield return Fade(0, UISettings.Current.reducedMotion ? .12f : .4f, ticket);
            if (ticket == generation) HideImmediately();
        }

        private IEnumerator Fade(float target, float duration, int ticket)
        {
            float from = curtain.alpha, time = 0;
            while (time < duration && ticket == generation)
            {
                time += Time.unscaledDeltaTime;
                curtain.alpha = Mathf.Lerp(from, target, Mathf.SmoothStep(0, 1, time / duration));
                yield return null;
            }
            if (ticket == generation) curtain.alpha = target;
        }

        public void HideImmediately()
        {
            generation++;
            curtain.alpha = 0; curtain.blocksRaycasts = false; curtain.interactable = false;
            gameObject.SetActive(false);
        }
    }
}

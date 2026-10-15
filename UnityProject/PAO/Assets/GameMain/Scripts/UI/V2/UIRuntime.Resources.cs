using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private BubbleResourceManager resource;
        private RectTransform resourcePanel;
        private Text resourceAmount;
        private Image resourceFill;
        private Image resourceTrail;
        private Tween resourceDrain;
        private int lastResourceAmount;
        private int resourceFeedbackChange;
        private float resourceFeedbackUntil;
        private void BindBubbleResource()
        {
            UnbindBubbleResource();
            resource = player != null ? player.GetComponent<BubbleResourceManager>() : null;
            if (resource == null) return;
            lastResourceAmount = resource.Current;
            resource.OnChanged += OnResourceChanged;
            resource.OnInsufficient += OnResourceInsufficient;
        }

        private void UnbindBubbleResource()
        {
            if (resource != null)
            {
                resource.OnChanged -= OnResourceChanged;
                resource.OnInsufficient -= OnResourceInsufficient;
            }
            resource = null;
            resourceFeedbackChange = 0; resourceFeedbackUntil = 0;
        }

        private void OnResourceChanged(int current, int maximum)
        {
            float previous = maximum > 0 ? Mathf.Clamp01((float)lastResourceAmount / maximum) : 0;
            float next = maximum > 0 ? Mathf.Clamp01((float)current / maximum) : 0;
            if (resourceTrail != null)
            {
                if (resourceDrain != null) resourceDrain.Kill();
                resourceTrail.rectTransform.anchorMax = new Vector2(previous, 1);
                if (current < lastResourceAmount && !UISettings.Current.reducedMotion)
                    resourceDrain = DOTween.To(() => resourceTrail.rectTransform.anchorMax.x,
                        x => resourceTrail.rectTransform.anchorMax = new Vector2(x, 1), next, .55f)
                        .SetDelay(.1f).SetEase(Ease.OutCubic).SetUpdate(true)
                        .SetLink(resourceTrail.gameObject).OnKill(() => resourceDrain = null);
                else resourceTrail.rectTransform.anchorMax = new Vector2(next, 1);
            }
            if (current > lastResourceAmount)
            {
                resourceFeedbackChange = current - lastResourceAmount;
                resourceFeedbackUntil = Time.unscaledTime + 2f;
            }
            else if (current < lastResourceAmount) resourceFeedbackUntil = 0;
            lastResourceAmount = current;
            RefreshResourceHud();
        }

        private void OnResourceInsufficient(int missing)
        {
            resourceFeedbackChange = -missing;
            resourceFeedbackUntil = Time.unscaledTime + 1.5f;
            RefreshResourceHud();
        }

        private void BuildResourceHud()
        {
            if (resource == null) return;
            resourcePanel = ui.Column("Bubble resource", hud, 12, 6);
            AddHudBacking(resourcePanel);
            Outline edge = resourcePanel.gameObject.AddComponent<Outline>();
            edge.effectColor = Color.black; edge.effectDistance = Vector2.one;
            RectTransform header = ui.Row("Energy summary", resourcePanel, 8);
            UIPearlGraphic bubble = ui.Rect("Resource bubble", header).gameObject.AddComponent<UIPearlGraphic>();
            bubble.shape = UIPearlShape.Bubble; bubble.color = Config.teal; bubble.raycastTarget = false;
            UIWidgets.Size(bubble.transform, 34, 34);
            resourceAmount = ui.Label(header, "", 32, Config.ink);
            resourceAmount.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIWidgets.Size(resourceAmount.transform, -1, -1, 1);
            header.gameObject.SetActive(false);
            Image track = ui.Surface("Resource track", resourcePanel, Color.white, true);
            track.raycastTarget = false; UIWidgets.Size(track.transform, 24);
            Outline trackEdge = track.gameObject.AddComponent<Outline>();
            trackEdge.effectColor = Color.black; trackEdge.effectDistance = Vector2.one;
            resourceTrail = ui.Surface("Resource decrease", track.transform, new Color(.65f, .65f, .65f, 1));
            resourceTrail.raycastTarget = false;
            resourceTrail.rectTransform.anchorMin = Vector2.zero;
            resourceTrail.rectTransform.anchorMax = new Vector2(resource.Normalized, 1);
            resourceTrail.rectTransform.offsetMin = resourceTrail.rectTransform.offsetMax = Vector2.zero;
            resourceFill = ui.Surface("Resource fill", track.transform, Color.black);
            resourceFill.raycastTarget = false;
            resourceFill.rectTransform.anchorMin = Vector2.zero;
            resourceFill.rectTransform.offsetMin = resourceFill.rectTransform.offsetMax = Vector2.zero;
            PositionResourceHud();
            ApplyHintPresentation();
        }

        private void PositionResourceHud()
        {
            if (resourcePanel == null) return;
            float height = 48;
            Anchor(resourcePanel, new Vector2(0, 1), new Vector2(32, -28), new Vector2(340, height), new Vector2(0, 1));
            if (objectivePanel != null) ((RectTransform)objectivePanel).anchoredPosition = new Vector2(32, -height - 44);
            if (tideCue != null) tideCue.anchoredPosition = new Vector2(398, -32);
        }

        private static float ResourceTextHeight(Text label)
        {
            if (label == null || !label.gameObject.activeSelf) return 0;
            LayoutElement layout = label.GetComponent<LayoutElement>();
            layout.preferredHeight = Mathf.Max(layout.minHeight, label.preferredHeight);
            return layout.preferredHeight;
        }

        private void RefreshResourceHud()
        {
            if (hud == null) return;
            if (resourcePanel != null && resource != null)
            {
                resourceAmount.text = "";
                resourceFill.color = Color.black;
                resourceFill.rectTransform.anchorMax = new Vector2(resource.Normalized, 1);
                resourceFill.enabled = resource.Current > 0;
            }
            if (reticleCue != null) reticleCue.gameObject.SetActive(false);
            PositionResourceHud();
            PositionGameplayHud();
        }

        private void ClearResourceHud()
        {
            if (resourceDrain != null) resourceDrain.Kill();
            resourceDrain = null; resourcePanel = null; resourceAmount = null; resourceFill = resourceTrail = null;
        }

        private void BuildMechanicsTestTools(Transform parent)
        {
            MechanicsTestScene test = FindFirstObjectByType<MechanicsTestScene>();
            if (test == null || player == null) return;
            RectTransform tools = ui.Card("Mechanics test tools", parent);
            tools.SetSiblingIndex(1);
            ui.Label(tools, T("机制测试", "Mechanics testing"), 28, Config.teal);
            RectTransform actions = ui.Grid("Test actions", tools, 3, 240);
            ui.Button(actions, T("补满泡泡值", "Refill energy"), () => { if (resource != null) resource.SetCurrent(resource.Max); Pop(); });
            ui.Button(actions, T("低资源（10%）", "Low energy (10%)"), () => { if (resource != null) resource.SetCurrent(resource.Max / 10); Pop(); });
            ui.Button(actions, T("回到出生点", "Return to spawn"), () => { Pop(); test.ReturnToSpawn(player); });
        }
    }
}

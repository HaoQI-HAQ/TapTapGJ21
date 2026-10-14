using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        // Leave the center of the view clear. Text grows inside the reserved bottom lane.
        private void PositionGameplayHud()
        {
            if (hudMessages == null || abilityCluster == null || safeRoot.rect.width < 400) return;
            float scale = Mathf.Clamp((safeRoot.rect.width - 540) / 800, .75f, 1);
            abilityCluster.localScale = Vector3.one * scale;
            float summaryHeight = Mathf.Max(34, ResourceTextHeight(currentAbility));
            UIWidgets.Size(abilitySummary, summaryHeight);
            float typeHeight = 24 + summaryHeight + ResourceTextHeight(resourceCost) + 14;
            if (terrainPanel != null && terrainPanel.gameObject.activeSelf)
            {
                float terrainHeight = ResourceTextHeight(terrainAmount) + 10;
                UIWidgets.Size(terrainPanel, terrainHeight);
                typeHeight += terrainHeight + 4;
            }
            RectTransform type = (RectTransform)abilityPanel;
            type.sizeDelta = new Vector2(240, typeHeight);
            type.anchoredPosition = new Vector2(-190, 126 + typeHeight * .5f);
            abilityCluster.sizeDelta = new Vector2(380, 266 + typeHeight);
            Transform topAbility = abilityCluster.Find("Ability 0");
            if (topAbility != null) ((RectTransform)topAbility).anchoredPosition = new Vector2(-190, 206 + typeHeight);

            float reservedRight = 52 + abilityCluster.rect.width * scale;
            float width = Mathf.Min(720, safeRoot.rect.width - reservedRight - 64);
            float center = Mathf.Min(safeRoot.rect.width * .5f, safeRoot.rect.width - reservedRight - width * .5f);
            hudMessages.anchorMin = hudMessages.anchorMax = new Vector2(.5f, 0);
            hudMessages.pivot = new Vector2(.5f, 0);
            hudMessages.anchoredPosition = new Vector2(center - safeRoot.rect.width * .5f, 28);
            hudMessages.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            if (resourceFeedbackPanel != null)
            {
                float feedbackHeight = 24 + ResourceTextHeight(resourceStatus) + ResourceTextHeight(chargeLabel);
                UIWidgets.Size(resourceFeedbackPanel, feedbackHeight);
            }
            float height = 0;
            foreach (RectTransform child in hudMessages)
            {
                if (!child.gameObject.activeSelf) continue;
                if (height > 0) height += 8;
                height += Mathf.Max(LayoutUtility.GetMinHeight(child), LayoutUtility.GetPreferredHeight(child));
            }
            hudMessages.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            if (tutorialCue != null)
                Anchor(tutorialCue, new Vector2(.5f, 0), new Vector2(center - safeRoot.rect.width * .5f, height + 36),
                    tutorialCue.sizeDelta, new Vector2(.5f, 0));
        }
    }
}

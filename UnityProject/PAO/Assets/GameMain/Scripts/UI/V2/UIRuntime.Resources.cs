using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private BubbleResourceManager resource;
        private BombBubbleManager resourceBombs;
        private RectTransform resourcePanel;
        private Text resourceAmount, resourceCost, resourceStatus;
        private Image resourceFill;
        private int lastResourceAmount;
        private int resourceFeedbackChange;
        private float resourceFeedbackUntil;
        private RectTransform terrainPanel;
        private Text terrainAmount;
        private Image terrainFill;
        private readonly RaycastHit[] terrainHits = new RaycastHit[32];

        private void BindBubbleResource()
        {
            UnbindBubbleResource();
            resourceBombs = player != null ? player.GetComponent<BombBubbleManager>() : null;
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
            resource = null; resourceBombs = null;
            resourceFeedbackChange = 0; resourceFeedbackUntil = 0;
        }

        private void OnResourceChanged(int current, int maximum)
        {
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
            RectTransform header = ui.Row("Energy summary", resourcePanel, 8);
            UIPearlGraphic bubble = ui.Rect("Resource bubble", header).gameObject.AddComponent<UIPearlGraphic>();
            bubble.shape = UIPearlShape.Bubble; bubble.color = Config.teal; bubble.raycastTarget = false;
            UIWidgets.Size(bubble.transform, 34, 34);
            resourceAmount = ui.Label(header, "", 32, Config.ink);
            resourceAmount.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIWidgets.Size(resourceAmount.transform, -1, -1, 1);
            Image track = ui.Surface("Resource track", resourcePanel, Config.mist, true);
            track.raycastTarget = false; UIWidgets.Size(track.transform, 24);
            resourceFill = ui.Surface("Resource fill", track.transform, Config.teal, true);
            resourceFill.raycastTarget = false;
            resourceFill.rectTransform.anchorMin = Vector2.zero;
            resourceFill.rectTransform.offsetMin = resourceFill.rectTransform.offsetMax = Vector2.zero;
            PositionResourceHud();
            ApplyHintPresentation();
            terrainPanel = ui.Column("Terrain fill", abilityPanel, 0, 4);
            terrainAmount = ui.Label(terrainPanel, "", 18, Config.ink, TextAnchor.MiddleCenter);
            Image terrainTrack = ui.Surface("Terrain track", terrainPanel, Config.mist, true);
            terrainTrack.raycastTarget = false; UIWidgets.Size(terrainTrack.transform, 6);
            terrainFill = ui.Surface("Terrain fill bar", terrainTrack.transform, Config.teal, true);
            terrainFill.raycastTarget = false;
            terrainFill.rectTransform.anchorMin = Vector2.zero;
            terrainFill.rectTransform.offsetMin = terrainFill.rectTransform.offsetMax = Vector2.zero;
            terrainPanel.gameObject.SetActive(false);
        }

        private void PositionResourceHud()
        {
            if (resourcePanel == null) return;
            float summaryHeight = Mathf.Max(34, resourceAmount.fontSize * 1.6f);
            UIWidgets.Size(resourceAmount.transform, summaryHeight, -1, 1);
            UIWidgets.Size(resourceAmount.transform.parent, summaryHeight);
            float height = 54 + summaryHeight;
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
            RefreshTerrainHud();
            if (resourcePanel != null && resource != null)
            {
                resourceAmount.text = resource.Current + " / " + resource.Max;
                bool insufficient = launcher != null && !resource.CanAfford(launcher.MinimumCost);
                bool feedback = Time.unscaledTime < resourceFeedbackUntil;
                Color color = insufficient || resource.Normalized < .2f || (feedback && resourceFeedbackChange < 0)
                    ? UIDesignPalette.Coral : Config.teal;
                resourceAmount.color = color;
                resourceFill.color = feedback && resourceFeedbackChange > 0 && color == Config.teal
                    ? Color.Lerp(Config.teal, Config.paper, .3f) : color;
                resourceFill.rectTransform.anchorMax = new Vector2(resource.Normalized, 1);
                resourceFill.enabled = resource.Current > 0;
            }
            bool charging = launcher != null && (launcher.IsCharging || launcher.HasHeldBubble);
            if (reticleCue != null)
            {
                reticleCue.gameObject.SetActive(charging && resource == null);
                if (charging) reticleArc.Progress = launcher.HasHeldBubble ? 1 : launcher.ChargeProgress;
            }
            if (launcher == null) { resourceCost.text = resourceStatus.text = ""; }
            else
            {
                resourceCost.text = T("消耗 ", "Cost ") + launcher.MinimumCost + "–" + launcher.MaximumCost;
                string charge = GuideInputPrompt(UIInput.Charge);
                if (launcher.IsCharging)
                    resourceStatus.text = launcher.IsResourceLimited ? T("泡泡值不足 · 停止变大", "Energy limited · Growth stopped") :
                        UIInput.ToggleCharge ? charge + T(" 再按释放", " again to release") : T("松开 ", "Release ") + charge;
                else if (launcher.HasHeldBubble)
                    resourceStatus.text = T("就绪 · ", "Ready · ") + charge + T(" 发射", " to launch");
                else if (resource != null && Time.unscaledTime < resourceFeedbackUntil)
                    resourceStatus.text = resourceFeedbackChange > 0 ? T("恢复 +", "Recovered +") + resourceFeedbackChange :
                        T("泡泡值不足 · 还差 ", "Not enough · Need ") + -resourceFeedbackChange;
                else if (resourceBombs != null && resourceBombs.PendingCount > 0)
                    resourceStatus.text = T("待引爆 ", "Bombs ready ") + resourceBombs.PendingCount + " · " + GuideInputPrompt(UIInput.Detonate);
                else if (resource != null && !resource.CanAfford(launcher.MinimumCost))
                    resourceStatus.text = T("泡泡值不足 · 还差 ", "Not enough · Need ") + (launcher.MinimumCost - resource.Current);
                else resourceStatus.text = "";
            }
            if (!UISettings.Current.detailedHints) resourceStatus.text = "";
            resourceCost.gameObject.SetActive(!string.IsNullOrEmpty(resourceCost.text));
            resourceStatus.gameObject.SetActive(!string.IsNullOrEmpty(resourceStatus.text));
            chargeLabel.gameObject.SetActive(UISettings.Current.detailedHints && !resourceStatus.gameObject.activeSelf);
            resourceFeedbackPanel.gameObject.SetActive(resourceStatus.gameObject.activeSelf || chargeLabel.gameObject.activeSelf);
            PositionResourceHud();
            PositionGameplayHud();
        }

        private void ClearResourceHud()
        {
            resourcePanel = null; resourceAmount = resourceCost = resourceStatus = null; resourceFill = null;
            terrainPanel = null; terrainAmount = null; terrainFill = null;
        }

        private void RefreshTerrainHud()
        {
            if (terrainPanel == null) return;
            Camera view = Camera.main;
            TerrainBubble terrain = null;
            float closest = float.PositiveInfinity;
            if (view != null)
            {
                int count = Physics.RaycastNonAlloc(view.ViewportPointToRay(new Vector3(.5f, .5f, 0)), terrainHits,
                    35f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    Transform hit = terrainHits[i].collider.transform;
                    if (player != null && hit.IsChildOf(player.transform)) continue;
                    if (terrainHits[i].distance >= closest) continue;
                    closest = terrainHits[i].distance;
                    terrain = hit.GetComponentInParent<TerrainBubble>();
                }
            }
            terrainPanel.gameObject.SetActive(terrain != null);
            if (terrain == null) return;
            terrainAmount.text = T("地形填充  ", "Terrain fill  ") + terrain.CurrentFill + " / " + terrain.Capacity;
            terrainFill.rectTransform.anchorMax = new Vector2(terrain.FillProgress, 1);
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

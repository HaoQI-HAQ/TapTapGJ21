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
        private Renderer[] playerHudRenderers;
        private readonly RaycastHit[] terrainHits = new RaycastHit[32];

        private void BindBubbleResource()
        {
            UnbindBubbleResource();
            resourceBombs = player != null ? player.GetComponent<BombBubbleManager>() : null;
            resource = player != null ? player.GetComponent<BubbleResourceManager>() : null;
            playerHudRenderers = player != null ? player.GetComponentsInChildren<Renderer>() : null;
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
            playerHudRenderers = null;
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
            resourcePanel = ui.Column("Bubble resource", hud, 12, 5);
            AddHudBacking(resourcePanel);
            RectTransform header = ui.Row("Energy summary", resourcePanel, 8);
            UIWidgets.Size(header, -1).minHeight = 35;
            resourceAmount = ui.Label(header, "", 22, Config.ink);
            UIWidgets.Size(resourceAmount.transform, -1, -1, 1);
            Image track = ui.Surface("Resource track", resourcePanel, Config.mist, true);
            track.raycastTarget = false; UIWidgets.Size(track.transform, 8);
            resourceFill = ui.Surface("Resource fill", track.transform, Config.teal, true);
            resourceFill.raycastTarget = false;
            resourceFill.rectTransform.anchorMin = Vector2.zero;
            resourceFill.rectTransform.offsetMin = resourceFill.rectTransform.offsetMax = Vector2.zero;
            resourceCost = ui.Label(resourcePanel, "", 18, Config.ink);
            resourceStatus = ui.Label(resourcePanel, "", 18, Config.text);
            // The charge badge belongs to the energy panel, away from the player model.
            if (reticleCue != null)
            {
                reticleCue.SetParent(header, false);
                UIWidgets.Size(reticleCue, 34, 34);
            }
            PositionResourceHud();
            ApplyHintPresentation();
            terrainPanel = ui.Column("Terrain fill", hud, 10, 4);
            AddHudBacking(terrainPanel);
            Anchor(terrainPanel, new Vector2(.5f, .5f), new Vector2(0, 160), new Vector2(300, 76), new Vector2(.5f, 0));
            terrainAmount = ui.Label(terrainPanel, "", 20, Config.ink, TextAnchor.MiddleCenter);
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
            float height = 37 + ResourceTextHeight(resourceAmount) + ResourceTextHeight(resourceCost) + ResourceTextHeight(resourceStatus);
            Anchor(resourcePanel, Vector2.zero, new Vector2(32, UISettings.Current.detailedHints ? 244 : 32),
                new Vector2(300, height), Vector2.zero);
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
            if (resourcePanel == null || resource == null) return;
            RefreshTerrainHud();
            resourceAmount.text = T("泡泡值  ", "Energy  ") + resource.Current + " / " + resource.Max;
            bool insufficient = launcher != null && !resource.CanAfford(launcher.MinimumCost);
            Color color = insufficient || resource.Normalized < .2f ? UIDesignPalette.Coral : Config.teal;
            resourceAmount.color = color; resourceFill.color = color;
            resourceFill.rectTransform.anchorMax = new Vector2(resource.Normalized, 1);
            bool charging = launcher != null && (launcher.IsCharging || launcher.HasHeldBubble);
            if (reticleCue != null)
            {
                reticleCue.gameObject.SetActive(charging);
                if (charging) reticleArc.Progress = launcher.HasHeldBubble ? 1 : launcher.ChargeProgress;
            }
            if (launcher == null) { resourceCost.text = resourceStatus.text = ""; }
            else
            {
                resourceCost.text = charging ? AbilityName((int)launcher.DisplayType) + T(" · 已用 ", " · Spent ") + launcher.PaidCost :
                    UISettings.Current.detailedHints ? AbilityName((int)launcher.DisplayType) + T(" · 消耗 ", " · Cost ") + launcher.MinimumCost + "–" + launcher.MaximumCost : "";
                string charge = GuideInputPrompt(UIInput.Charge);
                if (launcher.IsCharging)
                    resourceStatus.text = launcher.IsResourceLimited ? T("泡泡值不足 · 停止变大", "Energy limited · Growth stopped") :
                        UIInput.ToggleCharge ? charge + T(" 再按释放", " again to release") : T("松开 ", "Release ") + charge;
                else if (launcher.HasHeldBubble)
                    resourceStatus.text = T("就绪 · ", "Ready · ") + charge + T(" 发射", " to launch");
                else if (Time.unscaledTime < resourceFeedbackUntil)
                    resourceStatus.text = resourceFeedbackChange > 0 ? T("恢复 +", "Recovered +") + resourceFeedbackChange :
                        T("泡泡值不足 · 还差 ", "Not enough · Need ") + -resourceFeedbackChange;
                else if (resourceBombs != null && resourceBombs.PendingCount > 0)
                    resourceStatus.text = T("待引爆 ", "Bombs ready ") + resourceBombs.PendingCount + " · " + GuideInputPrompt(UIInput.Detonate);
                else if (insufficient)
                    resourceStatus.text = T("泡泡值不足 · 还差 ", "Not enough · Need ") + (launcher.MinimumCost - resource.Current);
                else resourceStatus.text = "";
            }
            resourceCost.gameObject.SetActive(!string.IsNullOrEmpty(resourceCost.text));
            resourceStatus.gameObject.SetActive(!string.IsNullOrEmpty(resourceStatus.text));
            PositionResourceHud();
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
            PositionTerrainHud(view);
        }

        private void PositionTerrainHud(Camera view)
        {
            float height = 30 + ResourceTextHeight(terrainAmount);
            terrainPanel.sizeDelta = new Vector2(300, height);
            Rect space = hud.rect;
            Vector2 position = new Vector2(0, space.height * .18f);
            // Project the authored player meshes; temporary gameplay bubbles do not expand this bound.
            Rect actor = new Rect(); bool found = false;
            if (view != null && playerHudRenderers != null) foreach (Renderer renderer in playerHudRenderers)
            {
                if (renderer == null || !renderer.enabled) continue;
                Bounds bounds = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    Vector3 screen = view.WorldToViewportPoint(point);
                    if (screen.z <= 0) continue;
                    Vector2 projected = new Vector2((screen.x - .5f) * space.width, (screen.y - .5f) * space.height);
                    if (!found) { actor = new Rect(projected, Vector2.zero); found = true; }
                    actor = Rect.MinMaxRect(Mathf.Min(actor.xMin, projected.x), Mathf.Min(actor.yMin, projected.y),
                        Mathf.Max(actor.xMax, projected.x), Mathf.Max(actor.yMax, projected.y));
                }
            }
            float ceiling = space.height * .5f - height - 100;
            if (found && actor.xMin < 174 && actor.xMax > -174 && position.y < actor.yMax + 24 && position.y + height > actor.yMin - 24)
            {
                position.y = actor.yMax + 24 <= ceiling ? actor.yMax + 24 : actor.yMin - height - 24;
            }
            position.y = Mathf.Clamp(position.y, -space.height * .5f + 100, ceiling);
            terrainPanel.anchoredPosition = position;
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

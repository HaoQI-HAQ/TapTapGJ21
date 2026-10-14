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
            resource = null;
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
            // Charging is already shown around the selected ability in the other corner.
            if (reticleCue != null) reticleCue.gameObject.SetActive(false);
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
            float summaryHeight = Mathf.Max(34, resourceAmount.fontSize * 1.6f);
            UIWidgets.Size(resourceAmount.transform, summaryHeight, -1, 1);
            UIWidgets.Size(resourceAmount.transform.parent, summaryHeight);
            float height = 54 + summaryHeight;
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
            PositionResourceHud();
        }

        private void ClearResourceHud()
        {
            resourcePanel = null; resourceAmount = null; resourceFill = null;
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

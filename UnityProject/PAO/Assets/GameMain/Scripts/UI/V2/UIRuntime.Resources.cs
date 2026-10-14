using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private BubbleResourceManager resource;
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
            resourcePanel = ui.Column("Bubble resource", hud, 16, 6);
            AddHudBacking(resourcePanel);
            resourceAmount = ui.Label(resourcePanel, "", 24, Config.ink);
            Image track = ui.Surface("Resource track", resourcePanel, Config.mist, true);
            track.raycastTarget = false; UIWidgets.Size(track.transform, 10);
            resourceFill = ui.Surface("Resource fill", track.transform, Config.teal, true);
            resourceFill.raycastTarget = false;
            resourceFill.rectTransform.anchorMin = Vector2.zero;
            resourceFill.rectTransform.offsetMin = resourceFill.rectTransform.offsetMax = Vector2.zero;
            resourceCost = ui.Label(resourcePanel, "", 20, Config.ink);
            resourceStatus = ui.Label(resourcePanel, "", 19, Config.text);
            PositionResourceHud();
            ApplyHintPresentation();
            terrainPanel = ui.Column("Terrain fill", hud, 10, 4);
            AddHudBacking(terrainPanel);
            Anchor(terrainPanel, new Vector2(.5f, .42f), Vector2.zero, new Vector2(300, 76), new Vector2(.5f, 1));
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
            float height = 60 + ResourceTextHeight(resourceAmount) + ResourceTextHeight(resourceCost) + ResourceTextHeight(resourceStatus);
            Anchor(resourcePanel, Vector2.zero, new Vector2(32, UISettings.Current.detailedHints ? 244 : 32),
                new Vector2(360, Mathf.Max(184, height)), Vector2.zero);
        }

        private static float ResourceTextHeight(Text label)
        {
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
            if (launcher == null) { resourceCost.text = ""; resourceStatus.text = ""; return; }
            resourceCost.text = AbilityName(switcher != null ? (int)switcher.Current : 0) + T(" · 消耗 ", " · Cost ") +
                launcher.MinimumCost + "–" + launcher.MaximumCost;
            string charge = UIInput.Prompt(UIInput.Charge);
            if (launcher.IsCharging)
            {
                resourceStatus.text = T("已用 ", "Spent ") + launcher.PaidCost +
                    (launcher.IsResourceLimited ? T(" · 泡泡值不足", " · Energy limited") : T(" · 蓄力中", " · Charging")) +
                    "\n" + (UIInput.ToggleCharge ? charge + T(" 再按释放", " to release") : T("松开 ", "Release ") + charge) +
                    T(" · 类型锁定", " · Type locked");
            }
            else if (launcher.HasHeldBubble)
                resourceStatus.text = T("已用 ", "Spent ") + launcher.PaidCost + T(" · 泡泡已就绪\n", " · Bubble ready\n") +
                    charge + T(" 发射 · 不再扣值", " to launch · No extra cost");
            else if (Time.unscaledTime < resourceFeedbackUntil)
                resourceStatus.text = resourceFeedbackChange > 0 ? T("泡泡值恢复 +", "Recovered +") + resourceFeedbackChange :
                    T("泡泡值不足 · 还差 ", "Not enough · Need ") + -resourceFeedbackChange;
            else if (insufficient)
                resourceStatus.text = T("不足以吹泡 · 还差 ", "Cannot charge · Need ") + (launcher.MinimumCost - resource.Current) +
                    T("\n破坏地形或回收浮粘泡泡", "\nBreak terrain or reclaim sticky bubbles");
            else resourceStatus.text = UIInput.ToggleCharge ? charge + T(" 点击蓄力 · 再按释放", " to charge · Again to release") :
                T("按住 ", "Hold ") + charge + T(" 蓄力 · 越大消耗越多", " to charge · Larger costs more");
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

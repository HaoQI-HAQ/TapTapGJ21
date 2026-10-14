using System.Collections.Generic;
using PAO.BubbleTide;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private sealed class InteractionCue
        {
            public Component owner;
            public Transform target;
            public Vector3 point;
            public UIGuideSymbol symbol;
            public int action, secondary, frame;
            public bool pinned;
        }
        private sealed class CueKey
        {
            public RectTransform root;
            public Text label;
            public UIGuideGraphic mouse;
        }
        private readonly List<InteractionCue> interactionCues = new List<InteractionCue>();
        private readonly RaycastHit[] guideHits = new RaycastHit[16];
        private RectTransform visualHud, goalCue, interactionCue, tutorialCue, reticleCue;
        private UIGuideGraphic goalSymbol, interactionSymbol, tutorialSymbol, secondarySymbol;
        private CueKey interactionKey, secondaryKey, tutorialKey;
        private UIChargeArc reticleArc, goalHalo;
        private RectTransform tideCue;
        private UIChargeArc tideArc;
        private Text tideTime;
        private float tideGraceDuration;
        private bool showingTideGrace;
        private Image tutorialBacking;
        private CanvasGroup tutorialGroup;
        private float tutorialRemaining;
        private int tutorialAction = -1;
        private bool pulseIsTutorial;
        private Transform objectivePanel, abilityPanel;
        private Font inputTypeface;

        private string GuideInputPrompt(int action)
        {
            if (UIInput.ControllerActive)
                return action == UIInput.Forward ? (UISettings.Current.swapSticks ? "RS" : "LS") : UIInput.Prompt(action);
            KeyCode binding = UIInput.Binding(action);
            if (binding == KeyCode.Space) return "Space";
            if (binding >= KeyCode.Mouse0 && binding <= KeyCode.Mouse6) return "M" + ((int)binding - (int)KeyCode.Mouse0 + 1);
            return UIInput.Prompt(action);
        }
        private void SetInputTypeface(Text label)
        {
            bool ascii = true;
            foreach (char character in label.text) if (character > 127) { ascii = false; break; }
            if (inputTypeface == null) inputTypeface = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Font target = ascii ? inputTypeface : Config.font;
            if (label.font != target) label.font = target;
        }

        private UIGuideGraphic GuideSymbol(Transform parent, string name, UIGuideSymbol symbol, float size, Color color)
        {
            UIGuideGraphic graphic = ui.Rect(name, parent).gameObject.AddComponent<UIGuideGraphic>();
            graphic.Symbol = symbol; graphic.color = color; graphic.raycastTarget = false;
            Anchor(graphic.rectTransform, new Vector2(.5f, .5f), Vector2.zero, Vector2.one * size, new Vector2(.5f, .5f));
            return graphic;
        }
        private CueKey GuideKey(Transform parent, Vector2 position)
        {
            Image plate = ui.Surface("Input cue", parent, new Color(Config.paper.r, Config.paper.g, Config.paper.b, .97f), true);
            plate.raycastTarget = false;
            Anchor(plate.rectTransform, new Vector2(.5f, .5f), position, new Vector2(80, 42), new Vector2(.5f, .5f));
            Text label = ui.Label(plate.transform, "", 20, null, TextAnchor.MiddleCenter); UIWidgets.Stretch(label.rectTransform, 5);
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 14; label.resizeTextMaxSize = label.fontSize;
            UIGuideGraphic mouse = GuideSymbol(plate.transform, "Mouse cue", UIGuideSymbol.MouseLeft, 30, Config.ink);
            return new CueKey { root = plate.rectTransform, label = label, mouse = mouse };
        }
        private void SetGuideKey(CueKey key, int action)
        {
            KeyCode binding = UIInput.Binding(action);
            bool mouse = !UIInput.ControllerActive && (binding == KeyCode.Mouse0 || binding == KeyCode.Mouse1);
            key.mouse.gameObject.SetActive(mouse); key.label.gameObject.SetActive(!mouse);
            if (mouse) key.mouse.Symbol = binding == KeyCode.Mouse0 ? UIGuideSymbol.MouseLeft : UIGuideSymbol.MouseRight;
            else key.label.text = GuideInputPrompt(action);
            SetInputTypeface(key.label);
            key.label.resizeTextMaxSize = key.label.fontSize;
            key.root.sizeDelta = new Vector2(mouse ? 54 : Mathf.Clamp(key.label.preferredWidth + 24, 54, 180), 42);
        }
        private Text HudIconButton(Transform parent, UIGuideSymbol symbol, System.Action action)
        {
            Image plate = ui.Surface(symbol.ToString(), parent, new Color(Config.paper.r, Config.paper.g, Config.paper.b, .97f), true);
            UIWidgets.Size(plate.transform, 56, 64);
            Button button = plate.gameObject.AddComponent<Button>(); button.targetGraphic = plate; button.onClick.AddListener(() => action());
            plate.gameObject.AddComponent<UIFocusRing>().Configure(Config.teal);
            GuideSymbol(plate.transform, symbol + " icon", symbol, 28, Config.ink).rectTransform.anchoredPosition = new Vector2(0, 6);
            Text key = ui.Label(plate.transform, "", 18, null, TextAnchor.MiddleCenter);
            key.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            key.resizeTextForBestFit = true; key.resizeTextMinSize = 12; key.resizeTextMaxSize = 18;
            Anchor(key.rectTransform, new Vector2(.5f, .5f), new Vector2(0, -16), new Vector2(62, 24), new Vector2(.5f, .5f));
            return key;
        }
        private void BuildVisualGuidance()
        {
            visualHud = ui.Rect("Visual guidance", hud); UIWidgets.Stretch(visualHud);
            goalCue = ui.Rect("Goal cue", visualHud);
            Anchor(goalCue, new Vector2(.5f, .5f), Vector2.zero, new Vector2(48, 48), new Vector2(.5f, .5f));
            Image goalPlate = ui.Rect("Goal backing", goalCue).gameObject.AddComponent<UICircleImage>();
            UIWidgets.Stretch(goalPlate.rectTransform); goalPlate.color = new Color(Config.paper.r, Config.paper.g, Config.paper.b, .96f); goalPlate.raycastTarget = false;
            goalHalo = ui.Rect("Goal halo", goalCue).gameObject.AddComponent<UIChargeArc>(); UIWidgets.Stretch(goalHalo.rectTransform, -3);
            goalHalo.Progress = 1; goalHalo.color = Config.teal; goalHalo.raycastTarget = false;
            goalSymbol = GuideSymbol(goalCue, "Destination", UIGuideSymbol.Goal, 28, Config.ink);

            interactionCue = ui.Rect("Interaction cue", visualHud);
            Anchor(interactionCue, new Vector2(.5f, .5f), Vector2.zero, new Vector2(160, 104), new Vector2(.5f, .5f));
            Image target = ui.Rect("Interaction backing", interactionCue).gameObject.AddComponent<UICircleImage>();
            Anchor(target.rectTransform, new Vector2(.5f, .5f), new Vector2(0, 16), new Vector2(58, 58), new Vector2(.5f, .5f));
            target.color = new Color(Config.paper.r, Config.paper.g, Config.paper.b, .97f); target.raycastTarget = false;
            interactionSymbol = GuideSymbol(target.transform, "Object cue", UIGuideSymbol.Memory, 34, Config.ink);
            interactionKey = GuideKey(interactionCue, new Vector2(0, -34));
            secondaryKey = GuideKey(interactionCue, new Vector2(70, -34));
            secondarySymbol = GuideSymbol(secondaryKey.root, "Steering cue", UIGuideSymbol.Steer, 22, Config.ink);
            secondarySymbol.rectTransform.anchoredPosition = new Vector2(0, 40);

            tutorialBacking = ui.Surface("First action cue", visualHud, new Color(Config.paper.r, Config.paper.g, Config.paper.b, .96f), true);
            tutorialBacking.raycastTarget = false; tutorialCue = tutorialBacking.rectTransform;
            tutorialGroup = tutorialCue.gameObject.AddComponent<CanvasGroup>(); tutorialGroup.blocksRaycasts = false;
            Anchor(tutorialCue, Vector2.zero, new Vector2(32, 32), new Vector2(174, 72), Vector2.zero);
            tutorialSymbol = GuideSymbol(tutorialCue, "Action example", UIGuideSymbol.Move, 44, Config.ink);
            tutorialSymbol.rectTransform.anchoredPosition = new Vector2(-51, 0);
            tutorialKey = GuideKey(tutorialCue, new Vector2(30, 0));

            reticleCue = ui.Rect("Charge reticle", visualHud);
            reticleCue.SetParent(abilityCluster, false);
            Anchor(reticleCue, new Vector2(1, 0), new Vector2(-190, 130), new Vector2(28, 28), new Vector2(.5f, .5f));
            Image reticleBacking = ui.Rect("Charge backing", reticleCue).gameObject.AddComponent<UICircleImage>();
            UIWidgets.Stretch(reticleBacking.rectTransform, -4);
            reticleBacking.color = new Color(Config.paper.r, Config.paper.g, Config.paper.b, .9f); reticleBacking.raycastTarget = false;
            UIChargeArc track = ui.Rect("Charge outline", reticleCue).gameObject.AddComponent<UIChargeArc>(); UIWidgets.Stretch(track.rectTransform);
            track.Progress = 1; track.color = new Color(Config.teal.r, Config.teal.g, Config.teal.b, .22f); track.raycastTarget = false;
            reticleArc = ui.Rect("Charge progress", reticleCue).gameObject.AddComponent<UIChargeArc>(); UIWidgets.Stretch(reticleArc.rectTransform);
            reticleArc.color = Config.teal; reticleArc.raycastTarget = false;
            GuideSymbol(reticleCue, "Launch direction", UIGuideSymbol.Charge, 20, Config.ink);
            tideCue = ui.Rect("Tide urgency", visualHud);
            Anchor(tideCue, new Vector2(0, 1), new Vector2(32, -32), new Vector2(48, 48), new Vector2(0, 1));
            Image tideBacking = ui.Rect("Tide backing", tideCue).gameObject.AddComponent<UICircleImage>();
            UIWidgets.Stretch(tideBacking.rectTransform, -4);
            tideBacking.color = new Color(Config.paper.r, Config.paper.g, Config.paper.b, .96f); tideBacking.raycastTarget = false;
            tideArc = ui.Rect("Tide remaining", tideCue).gameObject.AddComponent<UIChargeArc>(); UIWidgets.Stretch(tideArc.rectTransform);
            tideArc.color = Config.teal; tideArc.raycastTarget = false;
            GuideSymbol(tideCue, "Tide waves", UIGuideSymbol.Current, 24, Config.ink);
            Image clockBacking = ui.Surface("Tide clock backing", tideCue, new Color(Config.paper.r, Config.paper.g, Config.paper.b, .96f), true);
            Anchor(clockBacking.rectTransform, new Vector2(1, .5f), new Vector2(10, 0), new Vector2(126, 40), new Vector2(0, .5f));
            clockBacking.raycastTarget = false;
            tideTime = ui.Label(tideCue, "", 22, null);
            SetInputTypeface(tideTime);
            Anchor(tideTime.rectTransform, new Vector2(1, .5f), new Vector2(16, 0), new Vector2(120, 40), new Vector2(0, .5f));
            tideGraceDuration = 1; showingTideGrace = false;
            goalCue.gameObject.SetActive(false); interactionCue.gameObject.SetActive(false);
            tutorialCue.gameObject.SetActive(false); reticleCue.gameObject.SetActive(false); tideCue.gameObject.SetActive(false);
            ApplyHintPresentation();
        }
        private void ApplyHintPresentation()
        {
            bool detailed = UISettings.Current.detailedHints;
            if (objectivePanel != null) objectivePanel.gameObject.SetActive(detailed);
            if (abilityPanel != null)
            {
                abilityPanel.gameObject.SetActive(true);
            }
            PositionResourceHud();
            if (tutorialCue != null && detailed) tutorialCue.gameObject.SetActive(false);
            PositionGameplayHud();
            if (visualHud != null) visualHud.gameObject.SetActive(!detailed);
        }

        // Call while the interaction is actually available; unrefreshed owners expire automatically.
        public void RequestInteractionCue(Component owner, Vector3 point, UIGuideSymbol symbol, int action, int secondaryAction = -1, Transform target = null, bool pinned = false)
        {
            if (owner == null || !IsGameplay) return;
            InteractionCue cue = interactionCues.Find(item => item.owner == owner);
            if (cue == null) { cue = new InteractionCue { owner = owner }; interactionCues.Add(cue); }
            cue.point = point; cue.symbol = symbol; cue.action = action; cue.secondary = secondaryAction; cue.frame = Time.frameCount;
            cue.target = target != null ? target : owner.transform; cue.pinned = pinned;
        }
        public void RemoveInteractionCue(Component owner) { interactionCues.RemoveAll(item => item.owner == owner); }
        public void PulseVisualCue(UIGuideSymbol symbol, int action = -1, float duration = 3)
        {
            if (tutorialSymbol == null) return;
            tutorialSymbol.Symbol = symbol; tutorialAction = action; tutorialRemaining = duration;
            pulseIsTutorial = false;
            tutorialGroup.alpha = 1;
        }
        private void ShowVisualTutorial(string id)
        {
            if (id == "movement") PulseVisualCue(UIGuideSymbol.Move, UIInput.Forward, 5);
            else if (id.StartsWith("ability-"))
            {
                int type = switcher != null ? (int)switcher.Current : 0;
                PulseVisualCue(type == 0 ? UIGuideSymbol.Sticky : type == 1 ? UIGuideSymbol.Bouncy : UIGuideSymbol.Bomb, type);
            }
            else if (id == "first-cast") PulseVisualCue(UIGuideSymbol.Charge, UIInput.Charge);
            else PulseVisualCue(UIGuideSymbol.Journal, UIInput.Help);
            pulseIsTutorial = true;
        }
        private bool GuideOccluded(Camera camera, Vector3 point, Transform owner)
        {
            Vector3 direction = point - camera.transform.position;
            int count = Physics.RaycastNonAlloc(camera.transform.position, direction.normalized, guideHits, direction.magnitude - .15f, ~(1 << 5), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform hit = guideHits[i].transform;
                if (hit == null || hit == owner || hit.IsChildOf(owner) || (player != null && hit.IsChildOf(player.transform))) continue;
                return true;
            }
            return count == guideHits.Length;
        }
        private Vector2 GuidePosition(Vector3 viewport)
        {
            Rect rect = visualHud.rect;
            return new Vector2((viewport.x - .5f) * rect.width, (viewport.y - .5f) * rect.height);
        }
        private void UpdateVisualGuidance()
        {
            if (visualHud == null) return;
            ApplyHintPresentation();
            if (IsBlockingGameplay || !visualHud.gameObject.activeInHierarchy) return;
            Camera camera = Camera.main;
            tideCue.gameObject.SetActive(tide != null && tide.Deadline > 0 && !tide.IsFinished);
            if (tide != null)
            {
                bool grace = tide.State == BubbleTideState.GoalFlooded;
                if (grace && !showingTideGrace) tideGraceDuration = Mathf.Max(1, tide.GraceRemaining);
                showingTideGrace = grace;
                float remaining = grace ? tide.GraceRemaining : tide.TimeUntilGoalFlooded;
                tideArc.Progress = grace ? Mathf.Clamp01(remaining / tideGraceDuration) : 1 - tide.Progress;
                tideArc.color = grace || remaining < 10 ? UIDesignPalette.Coral : Config.teal;
                tideTime.text = Mathf.Max(0, Mathf.CeilToInt(remaining)) + " s";
            }
            goalCue.gameObject.SetActive(chapterGoal != null && camera != null && player != null);
            if (goalCue.gameObject.activeSelf)
            {
                Vector3 viewport = camera.WorldToViewportPoint(chapterGoal.position + Vector3.up * 2.7f);
                bool inView = viewport.z > 0 && viewport.x > .08f && viewport.x < .92f && viewport.y > .1f && viewport.y < .88f;
                bool clear = inView && !GuideOccluded(camera, chapterGoal.position + Vector3.up * 2.7f, chapterGoal);
                Vector2 position = GuidePosition(viewport), limit = visualHud.rect.size * .5f - new Vector2(100, 100);
                if (!clear)
                {
                    if (viewport.z <= 0) position = -position;
                    if (position.sqrMagnitude < .01f) position = Vector2.down;
                    position *= Mathf.Min(limit.x / Mathf.Max(.01f, Mathf.Abs(position.x)), limit.y / Mathf.Max(.01f, Mathf.Abs(position.y)));
                }
                float heading = Mathf.Atan2(position.y, position.x) * Mathf.Rad2Deg - 90;
                if (!clear)
                {
                    // Reserve the ability cluster and the first-action badge while preserving the bearing.
                    if (position.x > limit.x - 70 && position.y < -visualHud.rect.height * .5f + abilityCluster.rect.height * abilityCluster.localScale.x + 64)
                        position.y = Mathf.Min(limit.y, -visualHud.rect.height * .5f + abilityCluster.rect.height * abilityCluster.localScale.x + 64);
                    if (hudMessages != null && position.y < -visualHud.rect.height * .5f + hudMessages.rect.height + 76 &&
                        Mathf.Abs(position.x - hudMessages.anchoredPosition.x) < hudMessages.rect.width * .5f + 48)
                        position.y = -visualHud.rect.height * .5f + hudMessages.rect.height + 76;
                    if (tutorialRemaining > 0 && tutorialCue != null &&
                        position.y < -visualHud.rect.height * .5f + tutorialCue.anchoredPosition.y + tutorialCue.rect.height + 24 &&
                        Mathf.Abs(position.x - tutorialCue.anchoredPosition.x) < tutorialCue.rect.width * .5f + 48)
                        position.y = -visualHud.rect.height * .5f + tutorialCue.anchoredPosition.y + tutorialCue.rect.height + 24;
                    if (resourcePanel != null && resourcePanel.gameObject.activeInHierarchy &&
                        position.y > visualHud.rect.height * .5f - resourcePanel.rect.height - 72 &&
                        position.x < -visualHud.rect.width * .5f + resourcePanel.rect.width + 64)
                        position.y = visualHud.rect.height * .5f - resourcePanel.rect.height - 72;

                }
                goalCue.anchoredPosition = position; goalSymbol.Symbol = clear ? UIGuideSymbol.Goal : UIGuideSymbol.Arrow;
                goalSymbol.rectTransform.localRotation = clear ? Quaternion.identity : Quaternion.Euler(0, 0, heading);
                // A quiet direction cue; full destination detail is available in Help.
                goalHalo.color = new Color(Config.teal.r, Config.teal.g, Config.teal.b, UISettings.Current.highContrast ? 1 : .8f);
            }

            interactionCues.RemoveAll(cue => cue.owner == null || !cue.owner.gameObject.activeInHierarchy ||
                (cue.owner is Behaviour behaviour && !behaviour.isActiveAndEnabled) || Time.frameCount - cue.frame > 2);
            InteractionCue best = null; Vector3 bestView = Vector3.zero; float score = float.MaxValue;
            if (camera != null) foreach (InteractionCue cue in interactionCues)
            {
                Vector3 view = cue.pinned ? new Vector3(.5f, .35f, 1) : camera.WorldToViewportPoint(cue.point);
                if (view.z <= 0 || view.x < .08f || view.x > .92f || view.y < .14f || view.y > .86f || (!cue.pinned && GuideOccluded(camera, cue.point, cue.target))) continue;
                float candidate = cue.pinned ? -1 : ((Vector2)view - new Vector2(.5f, .5f)).sqrMagnitude + view.z * .001f;
                if (candidate < score) { score = candidate; best = cue; bestView = view; }
            }
            interactionCue.gameObject.SetActive(best != null);
            if (best != null)
            {
                interactionCue.anchoredPosition = GuidePosition(bestView);
                interactionSymbol.Symbol = best.symbol; SetGuideKey(interactionKey, best.action);
                secondaryKey.root.gameObject.SetActive(best.secondary >= 0);
                interactionKey.root.anchoredPosition = new Vector2(best.secondary >= 0 ? -44 : 0, -34);
                if (best.secondary >= 0) { SetGuideKey(secondaryKey, best.secondary); secondaryKey.root.anchoredPosition = new Vector2(44, -34); }
                if (chapterGoal != null && best.owner.transform == chapterGoal) goalCue.gameObject.SetActive(false);
            }

            tutorialCue.gameObject.SetActive(tutorialRemaining > 0 && (!pulseIsTutorial || UISettings.Current.tutorials));
            if (tutorialCue.gameObject.activeSelf)
            {
                tutorialKey.root.gameObject.SetActive(tutorialAction >= 0);
                if (tutorialAction >= 0) SetGuideKey(tutorialKey, tutorialAction);
                float width = tutorialAction < 0 ? 80 : tutorialKey.root.sizeDelta.x + 96;
                tutorialSymbol.rectTransform.anchoredPosition = new Vector2(tutorialAction < 0 ? 0 : -width * .5f + 36, 0);
                tutorialKey.root.anchoredPosition = new Vector2(width * .5f - tutorialKey.root.sizeDelta.x * .5f - 12, 0);
                tutorialCue.sizeDelta = new Vector2(width, 72);
                if (tutorialAction == UIInput.Forward && UIInput.Move().sqrMagnitude > .01f) tutorialRemaining = Mathf.Min(tutorialRemaining, .5f);
                tutorialRemaining -= Time.unscaledDeltaTime;
                float alpha = Mathf.Clamp01(tutorialRemaining / .4f);
                tutorialGroup.alpha = alpha;
            }
        }
        private void ClearVisualGuidance()
        {
            interactionCues.Clear(); tutorialRemaining = 0; pulseIsTutorial = false; visualHud = null;
            goalCue = interactionCue = tutorialCue = reticleCue = null;
            tideCue = null; tideArc = null; tideTime = null;
            goalSymbol = interactionSymbol = tutorialSymbol = null; objectivePanel = abilityPanel = null;
        }
    }
}

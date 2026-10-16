using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    public sealed class UIMenuDescriptionMotion : MonoBehaviour
    {
        private Image card;
        private UIConfig config;
        private Sequence response;
        public void Configure(Image target, UIConfig theme) { card = target; config = theme; }
        public void Respond()
        {
            Stop(); if (card == null) return;
            response = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            response.Append(card.DOColor(Color.Lerp(config.mist, new Color(.36f, .3f, .65f), .3f), .1f));
            response.Append(card.DOColor(config.mist, UISettings.Current.reducedMotion ? .05f : .22f));
            response.OnKill(() => response = null);
        }
        private void Stop() { if (response != null) response.Kill(); response = null; if (card != null) card.color = config.mist; }
        private void OnDisable() { Stop(); }
        private void OnDestroy() { Stop(); }
    }
    public sealed class UIMenuResonance : MonoBehaviour
    {
        private RectTransform character;
        private UIPearlGraphic orbit, glow;
        private Tween floatLoop, orbitLoop;
        private Sequence response;
        private UIConfig config;
        public void Configure(UIWidgets ui)
        {
            config = ui.Config;
            glow = Pearl(ui, "Character aura", UIPearlShape.Glow, 460, new Color(.38f, .7f, 1, .7f));
            Pearl(ui, "Character membrane", UIPearlShape.Bubble, 310, new Color(.36f, .85f, 1, .28f));
            orbit = Pearl(ui, "Character orbit", UIPearlShape.Orbit, 346, new Color(.63f, .67f, 1, .65f));
            Image art = ui.Surface("Character", transform, Color.white);
            art.sprite = config.menuArt; art.preserveAspect = true; art.raycastTarget = false;
            character = art.rectTransform; UIWidgets.Stretch(character, 8);
            Restart();
        }
        private UIPearlGraphic Pearl(UIWidgets ui, string name, UIPearlShape shape, float diameter, Color color)
        {
            UIPearlGraphic pearl = ui.Rect(name, transform).gameObject.AddComponent<UIPearlGraphic>();
            pearl.shape = shape; pearl.color = color; pearl.raycastTarget = false;
            pearl.rectTransform.anchorMin = pearl.rectTransform.anchorMax = Vector2.one * .5f;
            pearl.rectTransform.sizeDelta = Vector2.one * diameter; return pearl;
        }
        public void Respond(bool pressed = false)
        {
            if (response != null) response.Kill(); response = null;
            if (character == null || !isActiveAndEnabled) return;
            character.localRotation = Quaternion.identity; character.localScale = Vector3.one;
            glow.color = new Color(.68f, .61f, 1, .82f);
            response = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            response.Append(glow.DOColor(new Color(.38f, .7f, 1, .7f), UISettings.Current.reducedMotion ? .12f : .4f));
            if (!UISettings.Current.reducedMotion)
            {
                response.Insert(0, character.DOLocalRotate(new Vector3(0, 0, pressed ? -4 : -1.5f), .12f));
                response.Insert(.12f, character.DOLocalRotate(Vector3.zero, .26f).SetEase(Ease.OutSine));
                if (pressed)
                {
                    response.Insert(0, character.DOScale(.97f, .1f));
                    response.Insert(.1f, character.DOScale(1, .22f).SetEase(Ease.OutCubic));
                }
            }
            response.OnKill(() => response = null);
        }
        private void Restart()
        {
            Stop(); if (character == null) return;
            character.anchoredPosition = Vector2.zero; character.localRotation = Quaternion.identity; character.localScale = Vector3.one;
            if (UISettings.Current.reducedMotion || UISettings.Current.highContrast || !isActiveAndEnabled) return;
            floatLoop = character.DOAnchorPos(Vector2.up * 9, 3.4f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true).SetLink(gameObject).OnKill(() => floatLoop = null);
            orbit.Phase = 0;
            orbitLoop = DOTween.To(() => orbit.Phase, value => orbit.Phase = value, 360, 16).SetEase(Ease.Linear).SetLoops(-1, LoopType.Restart).SetUpdate(true).SetLink(gameObject).OnKill(() => orbitLoop = null);
        }
        private void Stop()
        {
            if (floatLoop != null) floatLoop.Kill(); floatLoop = null;
            if (orbitLoop != null) orbitLoop.Kill(); orbitLoop = null;
            if (response != null) response.Kill(); response = null;
        }
        private void OnEnable() { UISettings.Changed += Restart; Restart(); }
        private void OnDisable() { UISettings.Changed -= Restart; Stop(); }
        private void OnDestroy() { Stop(); }
    }
}

using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    // Each record remains outside pointer and keyboard navigation until its ink is visible.
    public sealed class UIRecordCardMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private CanvasGroup group;
        private RectTransform rule;
        private Sequence reveal;
        private Tween scaleMotion, ruleMotion;
        private bool ready, hovered, focused;

        public void Configure(UIWidgets ui, int index)
        {
            group = gameObject.AddComponent<CanvasGroup>();
            Image line = ui.Surface("Record edge response", transform, Color.black);
            line.raycastTarget = false;
            rule = line.rectTransform;
            rule.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            rule.anchorMin = new Vector2(0, 1);
            rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(.5f, 1);
            rule.anchoredPosition = Vector2.zero;
            rule.sizeDelta = new Vector2(-32, 2);

            if (UISettings.Current.reducedMotion)
            {
                SetReady();
                return;
            }

            ready = false;
            group.alpha = 0;
            group.interactable = false;
            group.blocksRaycasts = false;
            transform.localScale = Vector3.one * .985f;
            reveal = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            reveal.AppendInterval(.48f + index * .095f);
            reveal.Append(group.DOFade(1, .29f));
            reveal.Join(transform.DOScale(1, .35f).SetEase(Ease.OutCubic));
            reveal.OnComplete(SetReady);
        }

        private void Update()
        {
            if (!ready || EventSystem.current == null) return;
            GameObject current = EventSystem.current.currentSelectedGameObject;
            bool selected = current != null && current.transform.IsChildOf(transform);
            if (selected == focused) return;
            focused = selected;
            RefreshResponse();
        }

        private void SetReady()
        {
            if (group == null) return;
            group.alpha = 1;
            group.interactable = true;
            group.blocksRaycasts = true;
            transform.localScale = Vector3.one;
            ready = true;
            RefreshResponse();
        }

        private void RefreshResponse()
        {
            if (!ready || rule == null) return;
            if (scaleMotion != null) scaleMotion.Kill();
            if (ruleMotion != null) ruleMotion.Kill();
            bool active = hovered || focused;
            float targetScale = active && !UISettings.Current.reducedMotion ? 1.012f : 1;
            float targetHeight = active ? 5 : 2;
            if (UISettings.Current.reducedMotion)
            {
                transform.localScale = Vector3.one;
                rule.sizeDelta = new Vector2(-32, targetHeight);
                return;
            }
            scaleMotion = transform.DOScale(targetScale, .2f).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);
            ruleMotion = rule.DOSizeDelta(new Vector2(-32, targetHeight), .2f)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);
        }

        public void OnPointerEnter(PointerEventData eventData) { hovered = true; RefreshResponse(); }
        public void OnPointerExit(PointerEventData eventData) { hovered = false; RefreshResponse(); }

        private void OnDestroy()
        {
            if (reveal != null) reveal.Kill();
            if (scaleMotion != null) scaleMotion.Kill();
            if (ruleMotion != null) ruleMotion.Kill();
        }
    }

    // The title plate opens first; the production text follows like a second page.
    public sealed class UICreditsFolioMotion : MonoBehaviour
    {
        private RectTransform index, names;
        private CanvasGroup indexGroup, namesGroup;
        private Sequence motion;

        public void Configure(RectTransform indexPlate, RectTransform namesPlate)
        {
            index = indexPlate;
            names = namesPlate;
            indexGroup = index.gameObject.AddComponent<CanvasGroup>();
            namesGroup = names.gameObject.AddComponent<CanvasGroup>();
            if (UISettings.Current.reducedMotion)
            {
                SetFinal();
                return;
            }
            indexGroup.alpha = namesGroup.alpha = 0;
            index.localScale = Vector3.one * .965f;
            names.localScale = Vector3.one * .985f;
        }

        public void Play()
        {
            if (index == null || names == null) return;
            if (UISettings.Current.reducedMotion) { SetFinal(); return; }
            motion = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            motion.Insert(.12f, indexGroup.DOFade(1, .32f));
            motion.Insert(.12f, index.DOScale(1, .42f).SetEase(Ease.OutCubic));
            motion.Insert(.34f, namesGroup.DOFade(1, .42f));
            motion.Insert(.34f, names.DOScale(1, .48f).SetEase(Ease.OutCubic));
            motion.OnComplete(SetFinal);
        }

        private void SetFinal()
        {
            if (indexGroup != null) indexGroup.alpha = 1;
            if (namesGroup != null) namesGroup.alpha = 1;
            if (index != null) index.localScale = Vector3.one;
            if (names != null) names.localScale = Vector3.one;
        }

        private void OnDestroy() { if (motion != null) motion.Kill(); }
    }
}

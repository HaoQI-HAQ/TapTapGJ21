using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    // Keeps the entire line in Text for stable wrapping, then reveals its generated glyphs.
    public sealed class UICinematicSubtitleMotion : MonoBehaviour
    {
        private const float ExitDuration = .18f;

        private Text body;
        private Text speaker;
        private CanvasGroup rootInk;
        private UISubtitleGlyphFade glyphs;
        private Sequence entrance;
        private Tween exit;
        private float revealDuration;
        private bool isDismissing;

        public bool IsDismissing { get { return isDismissing; } }

        public void Configure(Text bodyText, Text speakerText)
        {
            body = bodyText;
            speaker = speakerText;
            if (rootInk == null) rootInk = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            if (body != null)
            {
                // Add before the caller's Outline/Shadow so their vertices inherit each glyph's fade.
                glyphs = body.GetComponent<UISubtitleGlyphFade>() ?? body.gameObject.AddComponent<UISubtitleGlyphFade>();
                glyphs.ShowAll();
            }
        }

        // Returns the time before the complete line is legible. The caller can add reading time.
        public float Present(string text)
        {
            CancelMotion();
            isDismissing = false;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (body == null) return 0;

            // Assign the whole sentence before animation, preserving line breaks and preferred height.
            body.text = text ?? string.Empty;
            revealDuration = glyphs != null ? glyphs.Prepare(body.text, body.supportRichText) : 0;
            if (ReducedMotion || revealDuration <= 0)
            {
                ShowComplete();
                return 0;
            }

            rootInk.alpha = 0;
            if (speaker != null) speaker.canvasRenderer.SetAlpha(1);
            entrance = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            entrance.Insert(0, rootInk.DOFade(1, .23f).SetEase(Ease.OutQuad));
            entrance.Insert(0, DOTween.To(() => -6f, value => glyphs.SetVerticalOffset(value), 0f, .32f)
                .SetEase(Ease.OutCubic));
            entrance.Insert(0, DOTween.To(() => 0f, value => glyphs.SetElapsed(value), revealDuration, revealDuration)
                .SetEase(Ease.Linear));
            entrance.OnComplete(ShowComplete);
            return revealDuration;
        }

        public void Dismiss(bool immediate)
        {
            if (!gameObject.activeSelf) { isDismissing = false; return; }
            CancelMotion();
            if (immediate || ReducedMotion || rootInk == null)
            {
                HideImmediately();
                return;
            }

            isDismissing = true;
            exit = rootInk.DOFade(0, ExitDuration).SetEase(Ease.InQuad).SetUpdate(true).SetLink(gameObject)
                .OnComplete(() => { exit = null; HideImmediately(); });
        }

        // Pausing or disabling subtitles must not restart an unfinished sentence on return.
        public void Suspend()
        {
            CancelMotion();
            isDismissing = false;
            if (glyphs != null) glyphs.ShowAll();
            if (rootInk != null) rootInk.alpha = 1;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        public void Restore()
        {
            CancelMotion();
            isDismissing = false;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            ShowComplete();
        }

        private static bool ReducedMotion { get { return UISettings.Current != null && UISettings.Current.reducedMotion; } }

        private void ShowComplete()
        {
            if (glyphs != null) glyphs.ShowAll();
            if (rootInk != null) rootInk.alpha = 1;
        }

        private void HideImmediately()
        {
            isDismissing = false;
            if (rootInk != null) rootInk.alpha = 0;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        private void CancelMotion()
        {
            if (entrance != null) { entrance.Kill(); entrance = null; }
            if (exit != null) { exit.Kill(); exit = null; }
        }

        private void OnEnable()
        {
            UISettings.Changed += SettingsChanged;
            SettingsChanged();
        }

        private void SettingsChanged()
        {
            if (!ReducedMotion || !gameObject.activeSelf) return;
            if (isDismissing) Dismiss(true);
            else
            {
                CancelMotion();
                ShowComplete();
            }
        }

        private void OnDisable()
        {
            UISettings.Changed -= SettingsChanged;
            CancelMotion();
            isDismissing = false;
        }

        private void OnDestroy() { CancelMotion(); }
    }

    // Runs before the text's Outline and Shadow. Only vertex color/position change;
    // the source string, preferred size, and wrapping remain constant throughout.
    internal sealed class UISubtitleGlyphFade : BaseMeshEffect
    {
        private const float GlyphInterval = .075f;
        private const float GlyphFade = .12f;
        private readonly List<float> arrivals = new List<float>();
        private float elapsed = float.PositiveInfinity;
        private float verticalOffset;

        public float Prepare(string text, bool richText)
        {
            arrivals.Clear();
            float next = .08f;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (richText && c == '<')
                {
                    int close = text.IndexOf('>', i + 1);
                    if (close > i) { i = close; continue; }
                }
                if (c == '\r' || c == '\n' || char.IsControl(c)) continue;
                if (char.IsLowSurrogate(c)) continue;

                arrivals.Add(next);
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
                next += char.IsWhiteSpace(c) ? .03f : GlyphInterval;
                if (c == '。' || c == '！' || c == '？' || c == '.' || c == '!' || c == '?') next += .18f;
                else if (c == '，' || c == '、' || c == '；' || c == ',' || c == ';' || c == ':' || c == '：') next += .10f;
            }
            elapsed = 0;
            verticalOffset = -6;
            Dirty();
            return arrivals.Count == 0 ? 0 : Mathf.Max(.23f, arrivals[arrivals.Count - 1] + GlyphFade);
        }

        public void SetElapsed(float value) { elapsed = value; Dirty(); }
        public void SetVerticalOffset(float value) { verticalOffset = value; Dirty(); }
        public void ShowAll() { elapsed = float.PositiveInfinity; verticalOffset = 0; Dirty(); }

        public override void ModifyMesh(VertexHelper helper)
        {
            if (!IsActive() || helper.currentVertCount == 0) return;
            int count = helper.currentVertCount / 4;
            UIVertex vertex = new UIVertex();
            for (int glyph = 0; glyph < count; glyph++)
            {
                float arrival = glyph < arrivals.Count ? arrivals[glyph] : 0;
                float opacity = float.IsPositiveInfinity(elapsed) ? 1 :
                    Mathf.SmoothStep(0, 1, Mathf.Clamp01((elapsed - arrival) / GlyphFade));
                float rise = verticalOffset - (1 - opacity) * 3;
                for (int corner = 0; corner < 4; corner++)
                {
                    int index = glyph * 4 + corner;
                    helper.PopulateUIVertex(ref vertex, index);
                    Color32 color = vertex.color;
                    color.a = (byte)Mathf.RoundToInt(color.a * opacity);
                    vertex.color = color;
                    vertex.position += new Vector3(0, rise, 0);
                    helper.SetUIVertex(vertex, index);
                }
            }
        }

        private void Dirty()
        {
            if (graphic != null) graphic.SetVerticesDirty();
        }
    }
}

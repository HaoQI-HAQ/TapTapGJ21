using System.Collections.Generic;
using PAO.UI.V2;
using UnityEngine;
using UnityEngine.UI;

namespace PAO
{
    /// <summary>
    /// 交互提示 UI：白色圆角方格 + 按键字母（默认 E）。
    ///
    /// 【用法】
    ///   不需要在场景里放 Canvas，也不用挂到任何物体上。
    ///   <see cref="LeverInteractable"/> 每帧调用 <see cref="Request"/> 即可；
    ///   第一个请求会自动创建一个隐藏的 Canvas，场景切换时跟着销毁。
    ///
    /// 【多个交互物】
    ///   所有请求会被放在同一个列表里，只显示离摄像机最近的那一个，
    ///   不会出现“两个拉杆同时冒出两块 E”的情况。
    ///
    /// 【为什么用运行时生成，而不是预制体】
    ///   关卡里可能有很多拉杆、门、开关，每处都摆一个 Canvas 很容易漏配、
    ///   也容易被别的 UI 挡住。运行时生成只有一份，排序固定，
    ///   项目里已有的暂停 / 设置面板会把它盖住（打开菜单时提示会自动消失）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionPrompt : MonoBehaviour
    {
        private const int SortingOrder = 1500;          // 高于 HUD（1000），低于过场（3000）
        private const float FadeInTime = 0.12f;
        private const float FadeOutTime = 0.10f;
        private const float ReferenceWidth = 1600f;     // 和 UIRuntime 的 Canvas 保持一致
        private const float ReferenceHeight = 900f;

        private sealed class PromptRequest
        {
            public Component Owner;
            public Vector3 WorldPoint;
            public string Key;
            public string Label;
            public int Frame;
        }

        private static InteractionPrompt s_Instance;

        private readonly List<PromptRequest> m_Requests = new List<PromptRequest>(4);

        private CanvasGroup m_Group;
        private RectTransform m_Root;
        private RectTransform m_Panel;
        private Image m_Plate;
        private Text m_Key;
        private Text m_Label;
        private Sprite m_PlateSprite;

        private float m_Alpha;
        private float m_ShownAt;
        private bool m_Visible;

        private static readonly Color Paper = new Color(0.965f, 0.957f, 0.925f, 1f);
        private static readonly Color Ink = new Color(0.075f, 0.11f, 0.145f, 1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Instance = null;
        }

        /// <summary>
        /// 请求显示一次交互提示。交互物每帧调用一次即可，停止调用后 2 帧自动消失。
        /// </summary>
        /// <param name="owner">谁请求的。同一个 owner 会覆盖上一帧的内容。</param>
        /// <param name="worldPoint">提示要挂在世界空间的哪个位置（一般是拉杆上方）。</param>
        /// <param name="key">方格里的字，例如 "E" / "A" / "Space"。</param>
        /// <param name="label">方格下方的小字，可留空。</param>
        public static void Request(Component owner, Vector3 worldPoint, string key, string label = "")
        {
            if (owner == null)
            {
                return;
            }

            if (s_Instance == null)
            {
                GameObject host = new GameObject("[Interaction Prompt]");
                s_Instance = host.AddComponent<InteractionPrompt>();
            }

            s_Instance.Push(owner, worldPoint, key, label);
        }

        /// <summary>交互物离开范围 / 被禁用时调用，立即清掉它的提示请求。</summary>
        public static void Release(Component owner)
        {
            if (s_Instance != null && owner != null)
            {
                s_Instance.Remove(owner);
            }
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            Build();
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }

            if (m_PlateSprite != null)
            {
                Texture2D texture = m_PlateSprite.texture;
                Destroy(m_PlateSprite);
                if (texture != null)
                {
                    Destroy(texture);
                }
                m_PlateSprite = null;
            }
        }

        private void Push(Component owner, Vector3 worldPoint, string key, string label)
        {
            PromptRequest request = null;
            for (int i = 0; i < m_Requests.Count; i++)
            {
                if (m_Requests[i].Owner == owner)
                {
                    request = m_Requests[i];
                    break;
                }
            }

            if (request == null)
            {
                request = new PromptRequest { Owner = owner };
                m_Requests.Add(request);
            }

            request.WorldPoint = worldPoint;
            request.Key = string.IsNullOrEmpty(key) ? "E" : key;
            request.Label = label;
            request.Frame = Time.frameCount;
        }

        private void Remove(Component owner)
        {
            for (int i = m_Requests.Count - 1; i >= 0; i--)
            {
                if (m_Requests[i].Owner == owner)
                {
                    m_Requests.RemoveAt(i);
                }
            }
        }

        private void LateUpdate()
        {
            if (m_Panel == null)
            {
                return;
            }

            // 交互物必须每帧“续约”，否则视为离开。多留 1 帧，避免脚本执行顺序造成闪烁。
            for (int i = m_Requests.Count - 1; i >= 0; i--)
            {
                PromptRequest request = m_Requests[i];
                if (request.Owner == null ||
                    !request.Owner.gameObject.activeInHierarchy ||
                    Time.frameCount - request.Frame > 1)
                {
                    m_Requests.RemoveAt(i);
                }
            }

            Camera camera = Camera.main;
            bool blocked = Time.timeScale <= 0f ||
                           MechanismCameraShot.IsAnyPlaying ||
                           (UIRuntime.Instance != null && UIInput.Blocked);

            PromptRequest best = null;
            Vector2 screenPoint = Vector2.zero;

            if (!blocked && camera != null && m_Requests.Count > 0)
            {
                float nearest = float.MaxValue;

                for (int i = 0; i < m_Requests.Count; i++)
                {
                    PromptRequest request = m_Requests[i];
                    Vector3 screen = camera.WorldToScreenPoint(request.WorldPoint);

                    if (screen.z <= 0.05f)
                    {
                        continue;
                    }

                    // 已经跑到屏幕外的不显示，免得贴着屏幕边缘一直挂一块 E
                    if (screen.x < -40f || screen.x > Screen.width + 40f ||
                        screen.y < -40f || screen.y > Screen.height + 40f)
                    {
                        continue;
                    }

                    float distance = (request.WorldPoint - camera.transform.position).sqrMagnitude;
                    if (distance < nearest)
                    {
                        nearest = distance;
                        best = request;
                        screenPoint = screen;
                    }
                }
            }

            if (best != null)
            {
                UpdateContent(best.Key, best.Label);

                Vector2 localPoint;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Root, screenPoint, null, out localPoint);

                float halfWidth = Mathf.Max(80f, Mathf.Max(1f, m_Root.rect.width) * 0.5f - 130f);
                float halfHeight = Mathf.Max(60f, Mathf.Max(1f, m_Root.rect.height) * 0.5f - 90f);
                localPoint.x = Mathf.Clamp(localPoint.x, -halfWidth, halfWidth);
                localPoint.y = Mathf.Clamp(localPoint.y, -halfHeight, halfHeight);

                if (!m_Visible)
                {
                    m_Visible = true;
                    m_ShownAt = Time.unscaledTime;
                    m_Panel.anchoredPosition = localPoint;
                    m_Panel.gameObject.SetActive(true);
                }
                else
                {
                    // 平滑跟随，避免拉杆转动 / 摄像机移动时 UI 抖动
                    float follow = 1f - Mathf.Exp(-22f * Time.unscaledDeltaTime);
                    m_Panel.anchoredPosition = Vector2.Lerp(m_Panel.anchoredPosition, localPoint, follow);
                }
            }
            else
            {
                m_Visible = false;
            }

            float targetAlpha = best != null ? 1f : 0f;
            float fadeSpeed = 1f / (best != null ? FadeInTime : FadeOutTime);
            m_Alpha = Mathf.MoveTowards(m_Alpha, targetAlpha, fadeSpeed * Time.unscaledDeltaTime);
            m_Group.alpha = m_Alpha;

            if (best == null && m_Alpha <= 0.001f && m_Panel.gameObject.activeSelf)
            {
                m_Panel.gameObject.SetActive(false);
            }

            if (m_Panel.gameObject.activeSelf)
            {
                float pop = Mathf.Clamp01((Time.unscaledTime - m_ShownAt) / 0.18f);
                pop = 1f - Mathf.Pow(1f - pop, 3f);
                float pulse = 1f + 0.025f * Mathf.Sin(Time.unscaledTime * 5f);
                float scale = Mathf.Lerp(0.86f, 1f, pop) * pulse;
                m_Panel.localScale = new Vector3(scale, scale, 1f);
            }
        }

        private void UpdateContent(string key, string label)
        {
            if (m_Key.text != key)
            {
                m_Key.text = key;
            }

            bool hasLabel = !string.IsNullOrEmpty(label);
            if (m_Label.gameObject.activeSelf != hasLabel)
            {
                m_Label.gameObject.SetActive(hasLabel);
            }

            if (hasLabel && m_Label.text != label)
            {
                m_Label.text = label;
            }

            // 有说明文字时，方格整体上移一点，视觉重心才在提示点上。
            // 字母是方格的子物体，会自动跟着走，这里不要再单独挪它。
            m_Plate.rectTransform.anchoredPosition = new Vector2(0f, hasLabel ? 16f : 0f);
        }

        private void Build()
        {
            GameObject canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            GameObject rootObject = new GameObject("Safe Area", typeof(RectTransform));
            rootObject.transform.SetParent(canvasObject.transform, false);
            m_Root = rootObject.GetComponent<RectTransform>();
            m_Root.anchorMin = Vector2.zero;
            m_Root.anchorMax = Vector2.one;
            m_Root.offsetMin = Vector2.zero;
            m_Root.offsetMax = Vector2.zero;

            GameObject panelObject = new GameObject("Prompt", typeof(RectTransform), typeof(CanvasGroup));
            panelObject.transform.SetParent(rootObject.transform, false);
            m_Panel = panelObject.GetComponent<RectTransform>();
            m_Panel.anchorMin = new Vector2(0.5f, 0.5f);
            m_Panel.anchorMax = new Vector2(0.5f, 0.5f);
            m_Panel.pivot = new Vector2(0.5f, 0.5f);
            m_Panel.sizeDelta = new Vector2(240f, 130f);

            m_Group = panelObject.GetComponent<CanvasGroup>();
            m_Group.alpha = 0f;
            m_Group.interactable = false;
            m_Group.blocksRaycasts = false;

            m_PlateSprite = CreatePlateSprite();

            GameObject plateObject = new GameObject("Key plate", typeof(RectTransform), typeof(Image), typeof(Shadow));
            plateObject.transform.SetParent(panelObject.transform, false);
            m_Plate = plateObject.GetComponent<Image>();
            m_Plate.sprite = m_PlateSprite;
            m_Plate.color = Paper;
            m_Plate.raycastTarget = false;
            m_Plate.preserveAspect = true;

            RectTransform plateRect = plateObject.GetComponent<RectTransform>();
            plateRect.anchorMin = new Vector2(0.5f, 0.5f);
            plateRect.anchorMax = new Vector2(0.5f, 0.5f);
            plateRect.pivot = new Vector2(0.5f, 0.5f);
            plateRect.sizeDelta = new Vector2(64f, 64f);
            plateRect.anchoredPosition = Vector2.zero;

            Shadow plateShadow = plateObject.GetComponent<Shadow>();
            plateShadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            plateShadow.effectDistance = new Vector2(0f, -3f);

            GameObject keyObject = new GameObject("Key", typeof(RectTransform), typeof(Text));
            keyObject.transform.SetParent(plateObject.transform, false);
            m_Key = keyObject.GetComponent<Text>();
            m_Key.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_Key.fontSize = 38;
            m_Key.fontStyle = FontStyle.Bold;
            m_Key.alignment = TextAnchor.MiddleCenter;
            m_Key.color = Ink;
            m_Key.raycastTarget = false;
            m_Key.resizeTextForBestFit = true;
            m_Key.resizeTextMinSize = 14;
            m_Key.resizeTextMaxSize = 38;
            m_Key.horizontalOverflow = HorizontalWrapMode.Overflow;
            m_Key.verticalOverflow = VerticalWrapMode.Overflow;
            m_Key.text = "E";

            RectTransform keyRect = keyObject.GetComponent<RectTransform>();
            keyRect.anchorMin = new Vector2(0.5f, 0.5f);
            keyRect.anchorMax = new Vector2(0.5f, 0.5f);
            keyRect.pivot = new Vector2(0.5f, 0.5f);
            keyRect.sizeDelta = new Vector2(56f, 56f);
            keyRect.anchoredPosition = Vector2.zero;

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Shadow));
            labelObject.transform.SetParent(panelObject.transform, false);
            m_Label = labelObject.GetComponent<Text>();
            m_Label.font = CreateLabelFont();
            m_Label.fontSize = 20;
            m_Label.alignment = TextAnchor.MiddleCenter;
            m_Label.color = Color.white;
            m_Label.raycastTarget = false;
            m_Label.resizeTextForBestFit = true;
            m_Label.resizeTextMinSize = 12;
            m_Label.resizeTextMaxSize = 20;
            m_Label.horizontalOverflow = HorizontalWrapMode.Overflow;
            m_Label.verticalOverflow = VerticalWrapMode.Overflow;

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(280f, 30f);
            labelRect.anchoredPosition = new Vector2(0f, -40f);

            Shadow labelShadow = labelObject.GetComponent<Shadow>();
            labelShadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            labelShadow.effectDistance = new Vector2(1f, -2f);

            labelObject.SetActive(false);
            panelObject.SetActive(false);
        }

        /// <summary>
        /// 生成白色圆角方格。用 SDF 画，边缘自带 1 像素抗锯齿；
        /// 运行时只生成一次，随后所有拉杆共用同一张 Sprite。
        /// </summary>
        private static Sprite CreatePlateSprite()
        {
            const int size = 96;
            const float radius = 18f;

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "Interaction Prompt Plate";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.DontSave;

            Color32[] pixels = new Color32[size * size];
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - half;
                    float py = y + 0.5f - half;

                    float qx = Mathf.Abs(px) - (half - 1f - radius);
                    float qy = Mathf.Abs(py) - (half - 1f - radius);
                    float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                               Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
                    float signedDistance = outside + inside - radius;
                    float alpha = Mathf.Clamp01(0.5f - signedDistance);

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprite.name = "Interaction Prompt Plate";
            sprite.hideFlags = HideFlags.DontSave;

            return sprite;
        }

        /// <summary>
        /// 方格下的说明文字可能是中文。优先用项目 UI 配置里的中文字体，
        /// 这样打包到别的机器上也不会掉字；没有配置时再找系统字体，
        /// 最后才退回内置字体（英文说明仍然正常）。
        /// </summary>
        private static Font CreateLabelFont()
        {
            UIConfig config = Resources.Load<UIConfig>("PAOUIConfig");
            if (config != null && config.font != null)
            {
                return config.font;
            }

            try
            {
                Font font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "PingFang SC", "Noto Sans CJK SC", "Arial" },
                    20);
                if (font != null)
                {
                    return font;
                }
            }
            catch
            {
                // 某些平台不允许动态字体，直接走兜底
            }

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}

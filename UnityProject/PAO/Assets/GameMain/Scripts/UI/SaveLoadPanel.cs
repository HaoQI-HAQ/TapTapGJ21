using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;   // TextAlignmentOptions

namespace PAO.UI
{
    /// <summary>
    /// 存档 / 读档面板：运行时用代码生成，**不需要改任何场景文件、不需要做预制体**。
    ///
    /// 【为什么用代码生成】
    /// 场景文件（.unity）是带自动分配 ID 的 YAML，手改或让脚本改都很容易踩坑
    /// （文件 ID 重复、引用错位、合并冲突）。这个面板结构简单，代码建更快更稳。
    ///
    /// 【布局】1920x1080 基准，三张横向卡片
    ///   ┌────────────────────────────────────────────┐
    ///   │                 存档 / 读档                 │
    ///   │ ┌────────┐ ┌────────┐ ┌────────┐          │
    ///   │ │ 记录一  │ │ 记录二  │ │ 记录三  │          │
    ///   │ │ 时间    │ │ 空记录  │ │ 空记录  │          │
    ///   │ │ 位置    │ │        │ │        │          │
    ///   │ │[存][读] │ │[存][读] │ │[存][读] │          │
    ///   │ └────────┘ └────────┘ └────────┘          │
    ///   │                  [返回]                     │
    ///   └────────────────────────────────────────────┘
    ///
    /// 【显隐】用 CanvasGroup.alpha，不用 SetActive —— 后者会连带停掉同物体上的组件，
    /// 在 Inspector 里看起来像"组件被关掉了"（这个坑在暂停面板上已经踩过）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/UI/Save Load Panel")]
    public sealed class SaveLoadPanel : MonoBehaviour
    {
        private const int kSortingOrder = 800;
        private static readonly Vector2 kReferenceResolution = new Vector2(1920f, 1080f);

        // 配色沿用暂停面板的浅色系，保持一致
        private static readonly Color kDim     = new Color(0.06f, 0.10f, 0.14f, 0.72f);
        private static readonly Color kPanel   = new Color(0.975f, 0.965f, 0.94f, 1f);
        private static readonly Color kCard    = new Color(0.93f, 0.95f, 0.94f, 1f);
        private static readonly Color kTeal    = new Color(0.34f, 0.68f, 0.73f, 1f);
        private static readonly Color kDark    = new Color(0.29f, 0.37f, 0.43f, 1f);
        private static readonly Color kMid     = new Color(0.42f, 0.52f, 0.56f, 1f);
        private static readonly Color kDisabled = new Color(0.72f, 0.76f, 0.78f, 1f);

        /// <summary>面板是否正在显示。</summary>
        public bool IsOpen { get; private set; }

        /// <summary>请求关闭时触发（「返回」按钮或 Esc）。</summary>
        public event Action Closed;

        /// <summary>读档请求：携带选中的存档数据，由外部决定切场景与传送。</summary>
        public event Action<SaveSlotData> LoadRequested;

        /// <summary>存档完成：携带刚写入的槽位。</summary>
        public event Action<int> SaveCompleted;

        private sealed class SlotUi
        {
            public int index;
            public Image card;
            public TMPro.TMP_Text nameLabel;
            public TMPro.TMP_Text timeLabel;
            public TMPro.TMP_Text posLabel;
            public Button saveButton;
            public Button loadButton;
            public TMPro.TMP_Text saveText;
            public TMPro.TMP_Text loadText;
        }

        private CanvasGroup m_Group;
        private TMPro.TMP_Text m_Title;
        private readonly List<SlotUi> m_Slots = new List<SlotUi>();

        private static Sprite s_RoundSprite;

        // ==================== 创建 ====================

        /// <summary>建好整个面板并返回实例。只在没有现成实例时调用。</summary>
        public static SaveLoadPanel Create(Transform parent = null)
        {
            var root = new GameObject("SaveLoad Panel", typeof(RectTransform));

            if (parent != null)
            {
                root.transform.SetParent(parent, false);
            }

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = kSortingOrder;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = kReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            root.AddComponent<GraphicRaycaster>();

            var group = root.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            var panel = root.AddComponent<SaveLoadPanel>();
            panel.BuildUi(root.transform);
            panel.ApplyVisible(false, true);

            return panel;
        }

        private void BuildUi(Transform root)
        {
            // ---------- 全屏遮罩（挡住底下的游戏与暂停面板）----------
            var dim = NewImage("Dim", root, kDim);
            Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            // ---------- 主面板 ----------
            var panel = NewImage("Panel", root, kPanel);
            RectTransform panelRect = panel.rectTransform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(1560f, 700f);

            // ---------- 标题 ----------
            m_Title = NewText("Title", panelRect, "存档 / 读档", 56f, kDark, TextAnchor.MiddleCenter);
            RectTransform titleRect = m_Title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -40f);
            titleRect.sizeDelta = new Vector2(-80f, 80f);

            // ---------- 三张卡片 ----------
            const float cardWidth = 460f;
            const float cardHeight = 400f;
            const float gap = 40f;
            float totalWidth = cardWidth * SaveSystem.DefaultSlotCount + gap * (SaveSystem.DefaultSlotCount - 1);
            float startX = -totalWidth * 0.5f + cardWidth * 0.5f;

            for (int i = 0; i < SaveSystem.DefaultSlotCount; i++)
            {
                SlotUi slot = BuildCard(panelRect, i, cardWidth, cardHeight, startX + i * (cardWidth + gap));
                m_Slots.Add(slot);
            }

            // ---------- 返回按钮 ----------
            Button back = NewButton("Back", panelRect, "返回", kMid, Color.white, 40f);
            RectTransform backRect = back.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0.5f, 0f);
            backRect.anchorMax = new Vector2(0.5f, 0f);
            backRect.pivot = new Vector2(0.5f, 0f);
            backRect.anchoredPosition = new Vector2(0f, 40f);
            backRect.sizeDelta = new Vector2(280f, 84f);
            back.onClick.AddListener(RequestClose);
        }

        private SlotUi BuildCard(RectTransform parent, int index, float width, float height, float x)
        {
            var slot = new SlotUi();
            slot.index = index;

            Image card = NewImage("Slot " + (index + 1), parent, kCard);
            slot.card = card;

            RectTransform rect = card.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, -10f);
            rect.sizeDelta = new Vector2(width, height);

            // 名字
            slot.nameLabel = NewText("Name", rect, SaveSystem.DefaultNameForSlot(index), 46f, kDark, TextAnchor.MiddleCenter);
            RectTransform nameRect = slot.nameLabel.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.anchoredPosition = new Vector2(0f, -28f);
            nameRect.sizeDelta = new Vector2(-40f, 60f);

            // 时间
            slot.timeLabel = NewText("Time", rect, "—", 30f, kMid, TextAnchor.MiddleCenter);
            RectTransform timeRect = slot.timeLabel.rectTransform;
            timeRect.anchorMin = new Vector2(0f, 1f);
            timeRect.anchorMax = new Vector2(1f, 1f);
            timeRect.pivot = new Vector2(0.5f, 1f);
            timeRect.anchoredPosition = new Vector2(0f, -100f);
            timeRect.sizeDelta = new Vector2(-40f, 44f);

            // 位置 / 进度
            slot.posLabel = NewText("Position", rect, "—", 26f, kMid, TextAnchor.UpperCenter);
            RectTransform posRect = slot.posLabel.rectTransform;
            posRect.anchorMin = new Vector2(0f, 0f);
            posRect.anchorMax = new Vector2(1f, 1f);
            posRect.pivot = new Vector2(0.5f, 1f);
            posRect.anchoredPosition = new Vector2(0f, -160f);
            posRect.sizeDelta = new Vector2(-56f, -280f);
            slot.posLabel.enableWordWrapping = true;

            // 存档 / 读档两个按钮
            Button save = NewButton("Save", rect, "存档", kTeal, Color.white, 32f);
            RectTransform saveRect = save.GetComponent<RectTransform>();
            saveRect.anchorMin = new Vector2(0f, 0f);
            saveRect.anchorMax = new Vector2(0.5f, 0f);
            saveRect.pivot = new Vector2(0.5f, 0f);
            saveRect.anchoredPosition = new Vector2(-8f, 28f);
            saveRect.sizeDelta = new Vector2(-28f, 76f);
            slot.saveButton = save;
            slot.saveText = save.GetComponentInChildren<TMPro.TMP_Text>();

            int captured = index;   // 闭包捕获
            save.onClick.AddListener(delegate { SaveToSlot(captured); });

            Button load = NewButton("Load", rect, "读档", kMid, Color.white, 32f);
            RectTransform loadRect = load.GetComponent<RectTransform>();
            loadRect.anchorMin = new Vector2(0.5f, 0f);
            loadRect.anchorMax = new Vector2(1f, 0f);
            loadRect.pivot = new Vector2(0.5f, 0f);
            loadRect.anchoredPosition = new Vector2(8f, 28f);
            loadRect.sizeDelta = new Vector2(-28f, 76f);
            slot.loadButton = load;
            slot.loadText = load.GetComponentInChildren<TMPro.TMP_Text>();
            load.onClick.AddListener(delegate { LoadFromSlot(captured); });

            return slot;
        }

        // ==================== 显隐 ====================

        private void ApplyVisible(bool visible, bool instant)
        {
            IsOpen = visible;

            if (m_Group == null)
            {
                m_Group = GetComponent<CanvasGroup>();
            }

            if (m_Group != null)
            {
                m_Group.alpha = visible ? 1f : 0f;
                m_Group.interactable = visible;
                m_Group.blocksRaycasts = visible;
            }

            if (visible)
            {
                RefreshSlots();
                SelectFirstButton();
            }
        }

        /// <summary>显示面板。</summary>
        public void Open()
        {
            if (IsOpen)
            {
                RefreshSlots();
                return;
            }

            gameObject.SetActive(true);
            ApplyVisible(true, false);
        }

        /// <summary>隐藏面板。</summary>
        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            ApplyVisible(false, false);
        }

        /// <summary>隐藏并通知外部。</summary>
        public void RequestClose()
        {
            bool wasOpen = IsOpen;
            Close();

            if (wasOpen && Closed != null)
            {
                Closed();
            }
        }

        private void Update()
        {
            // 面板显示期间用 Esc 关闭
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                PauseMenu.MarkOverlayEscapeHandled();
                RequestClose();
            }
        }

        private void SelectFirstButton()
        {
            UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null || m_Slots.Count == 0)
            {
                return;
            }

            Button first = m_Slots[0].loadButton != null ? m_Slots[0].loadButton : m_Slots[0].saveButton;
            if (first != null)
            {
                es.SetSelectedGameObject(first.gameObject);
            }
        }

        // ==================== 槽位刷新 ====================

        /// <summary>重新读一遍所有槽位并刷新显示。</summary>
        public void RefreshSlots()
        {
            for (int i = 0; i < m_Slots.Count; i++)
            {
                SlotUi slot = m_Slots[i];
                SaveSlotData data = SaveSystem.LoadSlot(slot.index);

                bool has = data != null;

                if (slot.nameLabel != null)
                {
                    slot.nameLabel.text = has ? data.displayName : "空记录";
                    slot.nameLabel.color = has ? kDark : kDisabled;
                }

                if (slot.timeLabel != null)
                {
                    slot.timeLabel.text = has ? data.SavedAtText : "—";
                    slot.timeLabel.color = has ? kMid : kDisabled;
                }

                if (slot.posLabel != null)
                {
                    slot.posLabel.text = has ? data.PositionText : "还没有存档";
                    slot.posLabel.color = has ? kMid : kDisabled;
                }

                // 读档按钮：空槽禁用
                if (slot.loadButton != null)
                {
                    slot.loadButton.interactable = has;
                }

                if (slot.loadText != null)
                {
                    slot.loadText.color = has ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                }
            }
        }

        // ==================== 存 / 读 ====================

        /// <summary>把当前状态写进指定槽位。</summary>
        public bool SaveToSlot(int slot)
        {
            Transform player = FindPlayerTransform();

            bool ok = SaveSystem.SaveCurrentState(slot, player, ProgressLabel());

            if (ok)
            {
                Debug.Log("[SaveLoadPanel] 已存档到「" + SaveSystem.DefaultNameForSlot(slot) + "」" +
                          (player != null ? "，位置 " + player.position : "（没找到玩家，只记场景）"), this);
            }
            else
            {
                Debug.LogError("[SaveLoadPanel] 存档失败，槽位 " + slot, this);
            }

            RefreshSlots();

            if (ok && SaveCompleted != null)
            {
                SaveCompleted(slot);
            }

            return ok;
        }

        /// <summary>请求读取指定槽位。</summary>
        public bool LoadFromSlot(int slot)
        {
            SaveSlotData data = SaveSystem.LoadSlot(slot);

            if (data == null)
            {
                Debug.LogWarning("[SaveLoadPanel] 槽位 " + slot + " 是空的，无法读取。", this);
                return false;
            }

            Debug.Log("[SaveLoadPanel] 读取「" + data.displayName + "」：" +
                      data.sceneName + " " + data.PositionText, this);

            // 先抛事件让外部处理（切场景 / 传送），再关面板
            if (LoadRequested != null)
            {
                LoadRequested(data);
            }

            SaveSystem.NotifyLoaded(data);
            RequestClose();
            return true;
        }

        /// <summary>存档时要写进 progress 字段的文字。目前用场景名。</summary>
        private static string ProgressLabel()
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            return scene;
        }

        /// <summary>在场景里找玩家。工程里没有自定义 Tag，所以按组件找。</summary>
        private static Transform FindPlayerTransform()
        {
            var pc = UnityEngine.Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Exclude);
            return pc != null ? pc.transform : null;
        }

        // ==================== 建 UI 的小工具 ====================

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            // 圆角贴图（有就用，没有就是纯色矩形，不影响功能）
            Sprite round = RoundSprite();
            if (round != null)
            {
                image.sprite = round;
                image.type = Image.Type.Sliced;
            }

            return image;
        }

        private static TMPro.TMP_Text NewText(string name, Transform parent, string content,
                                              float size, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = ToTmpAlignment(anchor);
            text.raycastTarget = false;
            text.richText = true;

            SaveLoadFont.Apply(text);
            return text;
        }

        private static Button NewButton(string name, Transform parent, string label,
                                        Color background, Color labelColor, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = background;
            image.raycastTarget = true;

            Sprite round = RoundSprite();
            if (round != null)
            {
                image.sprite = round;
                image.type = Image.Type.Sliced;
            }

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            // 按钮反馈：按下时压暗一点
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.5f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            TMPro.TMP_Text text = NewText("Label", go.transform, label, fontSize, labelColor, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            text.rectTransform.sizeDelta = new Vector2(-16f, -8f);

            return button;
        }

        private static TextAlignmentOptions ToTmpAlignment(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft:    return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter:  return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight:   return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft:   return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight:  return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft:    return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter:  return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight:   return TextAlignmentOptions.BottomRight;
                default:                      return TextAlignmentOptions.Center;
            }
        }

        /// <summary>按钮用的圆角贴图。找不到就返回 null（退回纯色矩形）。</summary>
        private static Sprite RoundSprite()
        {
            if (s_RoundSprite == null)
            {
                s_RoundSprite = Resources.Load<Sprite>("PAO/btn_round");
            }

            return s_RoundSprite;
        }
    }
}

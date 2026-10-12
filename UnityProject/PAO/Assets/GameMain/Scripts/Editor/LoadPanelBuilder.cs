using System.Collections.Generic;
using PAO.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.EditorTools
{
    /// <summary>
    /// 一键搭建「读取存档」面板。
    ///
    /// 为什么用编辑器脚本而不是手写场景 YAML：
    ///   物体 ID、父子关系、序列化引用全部交给 Unity 自己处理，
    ///   不会出现 ID 重复、引用错位、父子关系冲突这类问题。
    ///
    /// 用法：菜单 **PAO → UI → 构建读档面板**
    /// 幂等：重复执行会先删掉上次生成的根物体再重建，不会堆叠。
    /// </summary>
    public static class LoadPanelBuilder
    {
        private const string kPanelName = "Load Panel";
        private const string kSpriteFolder = "Assets/GameMain/UI/Sprites/";
        private const int kSlotCount = 3;

        // 已有菜单按钮的名字（面板打开时要禁用它们）
        private static readonly string[] kMenuButtonNames =
        {
            "Continue Button", "Start Button", "Settings Button", "Exit Button",
            "Loading Button", "Sound button", "Screen Button", "Operating Button",
            "Modifier Button", "restore default Button", "finish Button"
        };

        private static readonly Color kDim     = new Color(0.35f, 0.46f, 0.50f, 0.42f);
        private static readonly Color kPanel   = new Color(0.975f, 0.965f, 0.94f, 1f);
        private static readonly Color kCard    = new Color(0.93f, 0.95f, 0.94f, 1f);
        private static readonly Color kTeal    = new Color(0.34f, 0.68f, 0.73f, 1f);
        private static readonly Color kDark    = new Color(0.29f, 0.37f, 0.43f, 1f);
        private static readonly Color kMid     = new Color(0.42f, 0.52f, 0.56f, 1f);
        private static readonly Color kPill    = new Color(0.76f, 0.88f, 0.88f, 1f);
        private static readonly Color kLoadBtn = new Color(0.34f, 0.68f, 0.73f, 0.88f);

        [MenuItem("PAO/UI/构建读档面板", false, 20)]
        public static void BuildFromMenu()
        {
            Build(false);
        }

        /// <summary>
        /// 搭建面板。auto = true 时（自动构建）只写日志，不弹对话框。
        /// </summary>
        public static void Build(bool auto)
        {
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                if (!auto)
                {
                    EditorUtility.DisplayDialog("构建读档面板", "场景里找不到 Canvas，先打开 Menu 场景。", "好");
                }
                return;
            }

            Sprite round = LoadSprite("btn_round");
            Sprite ring  = LoadSprite("slot_ring");
            Sprite thumb = LoadSprite("slot_thumb");
            Sprite wave  = LoadSprite("slot_wave");

            if (round == null || ring == null || thumb == null || wave == null)
            {
                string msg = "缺少 Sprite：请确认 Assets/GameMain/UI/Sprites 下有\n" +
                             "btn_round / slot_ring / slot_thumb / slot_wave。";
                if (auto)
                {
                    Debug.LogError("[LoadPanelBuilder] " + msg);
                }
                else
                {
                    EditorUtility.DisplayDialog("构建读档面板", msg, "好");
                }
                return;
            }

            // ---- 幂等：先清掉旧的 ----
            GameObject old = FindDeep(canvas.transform, kPanelName);
            if (old != null)
            {
                Object.DestroyImmediate(old);
                Debug.Log("[LoadPanelBuilder] 已删除旧的 \"" + kPanelName + "\" 并重建。");
            }

            // ---- 根 ----
            GameObject root = NewUI(kPanelName, canvas.transform);
            Stretch(NewUI("Dim", root.transform), kDim, round, Image.Type.Sliced, true);

            Node title = NewNode("标题", root.transform, new Vector2(-760f, 470f), new Vector2(420f, 60f));
            SetText(title, "存档/读档", 40, kDark, TextAlignmentOptions.Left);

            Node arrow = NewNode("返回箭头", root.transform, new Vector2(-880f, 390f), new Vector2(60f, 48f));
            SetText(arrow, "←", 44, kDark, TextAlignmentOptions.Center);

            Node backLabel = NewNode("返回", root.transform, new Vector2(-790f, 390f), new Vector2(200f, 48f));
            SetText(backLabel, "返回", 34, kDark, TextAlignmentOptions.Left);

            Node backBtn = NewNode("返回按钮", root.transform, new Vector2(-820f, 390f), new Vector2(300f, 80f));
            backBtn.Image.color = new Color(1f, 1f, 1f, 0f);
            backBtn.Image.raycastTarget = true;
            Button backButton = backBtn.Go.AddComponent<Button>();
            backButton.transition = Selectable.Transition.None;
            backButton.targetGraphic = backBtn.Image;

            Node heading = NewNode("标题文字", root.transform, new Vector2(-780f, 250f), new Vector2(460f, 90f));
            SetText(heading, "记忆留痕", 64, kDark, TextAlignmentOptions.Left);

            Node underline = NewNode("下划线", root.transform, new Vector2(-800f, 196f), new Vector2(200f, 10f));
            underline.Image.color = kDark;

            Node waveNode = NewNode("波浪", root.transform, new Vector2(-760f, 190f), new Vector2(280f, 26f));
            waveNode.Image.sprite = wave;
            waveNode.Image.color = kTeal;
            waveNode.Image.type = Image.Type.Simple;

            Node pill = NewNode("说明底", root.transform, new Vector2(-770f, 120f), new Vector2(300f, 56f));
            pill.Image.sprite = round;
            pill.Image.type = Image.Type.Sliced;
            pill.Image.color = kPill;

            Node subLabel = NewNode("说明文字", root.transform, new Vector2(-770f, 122f), new Vector2(300f, 56f));
            SetText(subLabel, "读取/保存", 30, kDark, TextAlignmentOptions.Center);

            // ---- 槽位行 ----
            Node slotsRow = NewNode("槽位行", root.transform, new Vector2(-160f, -60f), new Vector2(1560f, 600f));

            var slotViews = new LoadPanel.SlotView[kSlotCount];
            string[] slotPos = { "-540,0", "-160,90", "240,-80" };

            for (int i = 0; i < kSlotCount; i++)
            {
                string[] xy = slotPos[i].Split(',');
                Vector2 pos = new Vector2(float.Parse(xy[0]), float.Parse(xy[1]));

                Node card = NewNode("槽位 " + (i + 1), slotsRow.Rect, pos, new Vector2(320f, 480f));
                card.Image.sprite = round;
                card.Image.type = Image.Type.Sliced;
                card.Image.color = kCard;
                Button cardButton = card.Go.AddComponent<Button>();
                cardButton.transition = Selectable.Transition.None;
                cardButton.targetGraphic = card.Image;

                // 选中框（全屏拉伸，画在最底层）
                Node sel = NewNode("选中框", card.Rect, Vector2.zero, Vector2.zero);
                StretchRect(sel.Rect);
                sel.Image.sprite = ring;
                sel.Image.type = Image.Type.Sliced;
                sel.Image.color = kTeal;
                sel.Image.raycastTarget = false;

                Node thumbNode = NewNode("缩略图", card.Rect, new Vector2(0f, 100f), new Vector2(240f, 190f));
                thumbNode.Image.sprite = thumb;
                thumbNode.Image.color = Color.white;

                Node nameNode = NewNode("名字", card.Rect, new Vector2(0f, -110f), new Vector2(300f, 70f));
                SetText(nameNode, "记录", 40, kDark, TextAlignmentOptions.Center);

                Node line = NewNode("下划条", card.Rect, new Vector2(0f, -165f), new Vector2(150f, 10f));
                line.Image.color = kDark;

                slotViews[i] = new LoadPanel.SlotView
                {
                    root = card.Go,
                    selection = sel.Go,
                    nameLabel = nameNode.Text,
                    thumbnail = thumbNode.Image,
                    button = cardButton
                };
            }

            // ---- 预览卡 ----
            Node preview = NewNode("预览卡", root.transform, new Vector2(560f, 130f), new Vector2(740f, 480f));
            preview.Image.sprite = round;
            preview.Image.type = Image.Type.Sliced;
            preview.Image.color = kPanel;

            Node preThumb = NewNode("预览缩略图", preview.Rect, new Vector2(-240f, 0f), new Vector2(240f, 190f));
            preThumb.Image.sprite = thumb;
            preThumb.Image.color = Color.white;

            Node preName = NewNode("预览标题", preview.Rect, new Vector2(80f, 150f), new Vector2(400f, 60f));
            SetText(preName, "记录预览", 40, kDark, TextAlignmentOptions.Left);

            Node timeLabel = NewNode("时间标签", preview.Rect, new Vector2(-180f, 60f), new Vector2(220f, 50f));
            SetText(timeLabel, "保存时间", 30, kDark, TextAlignmentOptions.Left);

            Node timeValue = NewNode("时间值", preview.Rect, new Vector2(120f, 60f), new Vector2(320f, 50f));
            SetText(timeValue, "—", 30, kMid, TextAlignmentOptions.Left);

            Node posLabel = NewNode("位置标签", preview.Rect, new Vector2(-180f, 0f), new Vector2(220f, 50f));
            SetText(posLabel, "恢复位置", 30, kDark, TextAlignmentOptions.Left);

            Node posValue = NewNode("位置值", preview.Rect, new Vector2(120f, 0f), new Vector2(320f, 50f));
            SetText(posValue, "—", 30, kMid, TextAlignmentOptions.Left);

            // ---- 读取按钮 ----
            Node loadBtn = NewNode("读取此记录", root.transform, new Vector2(560f, -230f), new Vector2(420f, 110f));
            loadBtn.Image.sprite = round;
            loadBtn.Image.type = Image.Type.Sliced;
            loadBtn.Image.color = kLoadBtn;
            Button loadButton = loadBtn.Go.AddComponent<Button>();
            loadButton.transition = Selectable.Transition.None;
            loadButton.targetGraphic = loadBtn.Image;

            Node loadLabel = NewNode("文字", loadBtn.Rect, Vector2.zero, Vector2.zero);
            StretchRect(loadLabel.Rect);
            SetText(loadLabel, "读取此记录", 38, Color.white, TextAlignmentOptions.Center);

            // ---- 收集主菜单按钮 ----
            var menuButtons = new List<MenuButton>();
            foreach (string n in kMenuButtonNames)
            {
                GameObject go = FindDeep(canvas.transform, n);
                if (go == null)
                {
                    continue;
                }

                MenuButton mb = go.GetComponent<MenuButton>();
                if (mb != null)
                {
                    menuButtons.Add(mb);
                }
            }

            // ---- 挂 LoadPanel 并接线 ----
            LoadPanel panel = root.AddComponent<LoadPanel>();
            panel.Configure(new LoadPanel.Config
            {
                root = root,
                slots = slotViews,
                previewName = preName.Text,
                previewTime = timeValue.Text,
                previewPosition = posValue.Text,
                previewThumbnail = preThumb.Image,
                loadButton = loadButton,
                backButton = backButton,
                menuButtons = menuButtons.ToArray(),
                defaultSlot = 0,
                emptySlotName = "空记录",
                hideBackButton = false
            });

            // ---- 接到 Loading Button ----
            GameObject loadingBtn = FindDeep(canvas.transform, "Loading Button");
            bool wired = false;
            if (loadingBtn != null)
            {
                LoadButtonOpener opener = loadingBtn.GetComponent<LoadButtonOpener>();
                if (opener == null)
                {
                    opener = loadingBtn.AddComponent<LoadButtonOpener>();
                }

                SerializedObject so = new SerializedObject(opener);
                SerializedProperty prop = so.FindProperty("m_Panel");
                if (prop != null)
                {
                    prop.objectReferenceValue = panel;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                wired = true;
            }

            // 刻意保持「激活」：未激活的物体在 Scene 视图里灰显、点不动，不方便你手动调 UI。
            // 运行时由 LoadPanel.Awake() 在 Application.isPlaying 时自己关掉，不会盖住主菜单。
            root.SetActive(true);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log(string.Format(
                "[LoadPanelBuilder] 完成：面板已建好，{0} 个槽位，主菜单按钮 {1} 个，Loading Button 接线 {2}。",
                kSlotCount, menuButtons.Count, wired ? "成功" : "失败（找不到 Loading Button）"));

            if (!auto)
            {
                EditorUtility.DisplayDialog("构建读档面板",
                    string.Format("面板已建好并保存。\n\n槽位：{0} 个\n主菜单按钮：{1} 个\nLoading Button 接线：{2}",
                        kSlotCount, menuButtons.Count, wired ? "成功" : "失败"),
                    "好");
            }
        }

        // ==================== 自动构建 ====================

        // 标记键带版本号：需要让面板重建一次时，把末尾的 v1 改成 v2 即可。
        private const string kAutoBuildKey = "PAO.LoadPanel.AutoBuilt.v2";

        /// <summary>
        /// 脚本编译完成后自动跑一次，省得还要人记得点菜单。
        /// 只跑一次（EditorPrefs 打标）；想重建走菜单 PAO → UI → 构建读档面板。
        /// 编辑器脚本更新时域重载会触发本方法，所以必须够轻、够安全。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void AutoBuildOnce()
        {
            if (EditorPrefs.GetBool(kAutoBuildKey, false))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                try
                {
                    // 运行中不能改场景（Unity 会拒绝存盘），直接跳过，等退出 Play 再说
                    if (EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        Debug.Log("[LoadPanelBuilder] 正在运行中，已跳过自动构建。" +
                                  "退出 Play 后会自动重试，或用菜单 PAO → UI → 构建读档面板。");
                        return;
                    }

                    // 只在 Menu 场景里自动建；其它场景不打扰
                    if (SceneManager.GetActiveScene().name != "Menu")
                    {
                        Debug.Log("[LoadPanelBuilder] 当前不是 Menu 场景，已跳过自动构建。" +
                                  "打开 Menu 场景后用菜单 PAO → UI → 构建读档面板。");
                        return;
                    }

                    if (Object.FindObjectOfType<Canvas>() == null)
                    {
                        return;
                    }

                    Build(true);
                    EditorPrefs.SetBool(kAutoBuildKey, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[LoadPanelBuilder] 自动构建失败：" + e.Message +
                                   "\n请用菜单 PAO → UI → 构建读档面板 手动执行。");
                }
            };
        }

        // ==================== helpers ====================

        private struct Node
        {
            public GameObject Go;
            public RectTransform Rect;
            public Image Image;
            public TextMeshProUGUI Text;
        }

        private static Sprite LoadSprite(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(kSpriteFolder + name + ".png");
        }

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.anchoredPosition3D = Vector3.zero;
            return go;
        }

        /// <summary>居中锚点、显式尺寸的节点。</summary>
        private static Node NewNode(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject go = NewUI(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.raycastTarget = false;

            var node = new Node { Go = go, Rect = rt, Image = img };

            // 中心对齐的文字子物体，尺寸跟随父级
            GameObject textGo = NewUI(name + " 文字", go.transform);
            var textRt = textGo.GetComponent<RectTransform>();
            StretchRect(textRt);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.raycastTarget = false;
            node.Text = tmp;
            return node;
        }

        /// <summary>Image + 拉伸填充（用于 Dim 这类铺满的底）。</summary>
        private static Image Stretch(GameObject go, Color color, Sprite sprite, Image.Type type, bool raycast)
        {
            var rt = go.GetComponent<RectTransform>();
            StretchRect(rt);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            img.type = type;
            img.raycastTarget = raycast;
            return img;
        }

        private static void StretchRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }

        private static void SetText(Node node, string text, float size, Color color, TextAlignmentOptions align)
        {
            if (node.Text == null)
            {
                return;
            }

            node.Text.text = text;
            node.Text.fontSize = size;
            node.Text.color = color;
            node.Text.alignment = align;
            node.Text.raycastTarget = false;
        }

        private static GameObject FindDeep(Transform root, string name)
        {
            if (root.name == name)
            {
                return root.gameObject;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                GameObject hit = FindDeep(root.GetChild(i), name);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }
    }
}

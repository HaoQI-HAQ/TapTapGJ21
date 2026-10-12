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
    /// 一键搭建「关卡选择」界面（Menu 场景）。
    ///
    /// 做三件事：
    ///   1. 建一个主菜单入口按钮「关卡选择」（放在右侧，和 Settings 竖排对齐）
    ///   2. 建全屏关卡选择面板：标题、关闭按钮、3x2 六个关卡按钮
    ///   3. 把六个 Map 场景加进 Build Settings（不加就切不过去）
    ///
    /// 用法：菜单 **PAO → UI → 构建关卡选择面板**
    /// 幂等：重复执行先删旧的入口按钮与面板再重建。
    /// </summary>
    public static class LevelSelectBuilder
    {
        private const string kRootName = "Level Select Panel";
        private const string kEntryName = "关卡选择 Button";
        private const string kSpriteFolder = "Assets/GameMain/UI/Sprites/";
        private const string kSceneFolder = "Assets/GameMain/Scenes/";
        private const string kAutoBuildKey = "PAO.LevelSelect.AutoBuilt.v1";

        private static readonly Color kPanelBg = new Color(0.975f, 0.965f, 0.94f, 1f);
        private static readonly Color kCardBg = new Color(1f, 1f, 1f, 0.86f);
        private static readonly Color kTextDark = new Color(0.29f, 0.37f, 0.43f, 1f);
        private static readonly Color kHighlight = new Color(0.34f, 0.68f, 0.73f, 0.9f);

        [MenuItem("PAO/UI/构建关卡选择面板", false, 22)]
        public static void BuildFromMenu()
        {
            Build(false);
        }

        public static void Build(bool auto)
        {
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Report(auto, "场景里找不到 Canvas。请打开 Menu 场景再执行。");
                return;
            }

            Sprite round = AssetDatabase.LoadAssetAtPath<Sprite>(kSpriteFolder + "btn_round.png");
            if (round == null)
            {
                Report(auto, "缺少 Sprite：Assets/GameMain/UI/Sprites/btn_round.png");
                return;
            }

            // ---------- 1. 收集主菜单按钮（面板打开时要禁用它们）----------
            var menuButtons = new List<MenuButton>();
            string[] menuButtonNames =
            {
                "Continue Button", "Start Button", "Settings Button", "Exit Button", "Loading Button"
            };
            foreach (string n in menuButtonNames)
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

            // ---------- 2. 幂等清理 ----------
            Transform oldPanel = canvas.transform.Find(kRootName);
            if (oldPanel != null)
            {
                Object.DestroyImmediate(oldPanel.gameObject);
                Debug.Log("[LevelSelectBuilder] 已删除旧的 \"" + kRootName + "\" 并重建。");
            }

            Transform oldEntry = canvas.transform.Find(kEntryName);
            if (oldEntry != null)
            {
                Object.DestroyImmediate(oldEntry.gameObject);
                Debug.Log("[LevelSelectBuilder] 已删除旧的 \"" + kEntryName + "\" 并重建。");
            }

            // ---------- 3. 全屏面板 ----------
            GameObject root = NewUI(kRootName, canvas.transform);
            rt(root).anchorMin = Vector2.zero;
            rt(root).anchorMax = Vector2.one;
            rt(root).pivot = new Vector2(0.5f, 0.5f);
            rt(root).anchoredPosition = Vector2.zero;
            rt(root).sizeDelta = Vector2.zero;
            Image bg = root.AddComponent<Image>();
            bg.color = kPanelBg;
            bg.raycastTarget = true;

            // 标题
            NewLabel("标题", root.transform, "关卡选择", 64,
                new Vector2(-800f, 440f), new Vector2(520f, 90f), TextAlignmentOptions.Left, kTextDark);

            // 关闭按钮（右上角）
            Button closeBtn = NewButton("关闭按钮", root.transform, new Vector2(800f, 440f),
                new Vector2(160f, 80f), "关闭", 36, round, new Color(1f, 1f, 1f, 0.35f));

            // ---------- 4. 六个关卡按钮（3x2）----------
            var levels = new List<LevelSelectPanel.LevelEntry>();
            var levelButtons = new List<Button>();

            for (int i = 0; i < 6; i++)
            {
                string sceneName = "Map" + i;

                // 位置：3 列 x 2 行
                int col = i % 3;
                int row = i / 3;
                float x = -440f + col * 440f;
                float y = 130f - row * 400f;

                // 卡片本体：可点击 + 悬停底图
                GameObject card = NewUI(sceneName, root.transform);
                rt(card).anchorMin = new Vector2(0.5f, 0.5f);
                rt(card).anchorMax = new Vector2(0.5f, 0.5f);
                rt(card).pivot = new Vector2(0.5f, 0.5f);
                rt(card).anchoredPosition = new Vector2(x, y);
                rt(card).sizeDelta = new Vector2(360f, 340f);

                Image cardBg = card.AddComponent<Image>();
                cardBg.sprite = round;
                cardBg.type = Image.Type.Sliced;
                cardBg.color = kCardBg;
                cardBg.raycastTarget = true;

                Button cardButton = card.AddComponent<Button>();
                cardButton.transition = Selectable.Transition.None;
                cardButton.targetGraphic = cardBg;

                // 缩略图占位（用 slot_thumb）
                Sprite thumb = AssetDatabase.LoadAssetAtPath<Sprite>(kSpriteFolder + "slot_thumb.png");
                if (thumb != null)
                {
                    GameObject thumbGo = NewUI("缩略图", card.transform);
                    rt(thumbGo).anchorMin = new Vector2(0.5f, 0.5f);
                    rt(thumbGo).anchorMax = new Vector2(0.5f, 0.5f);
                    rt(thumbGo).pivot = new Vector2(0.5f, 0.5f);
                    rt(thumbGo).anchoredPosition = new Vector2(0f, 52f);
                    rt(thumbGo).sizeDelta = new Vector2(240f, 190f);
                    Image thumbImg = thumbGo.AddComponent<Image>();
                    thumbImg.sprite = thumb;
                    thumbImg.color = Color.white;
                    thumbImg.raycastTarget = false;
                }

                // 关卡名
                TMP_Text label = NewLabel("名字", card.transform, sceneName, 48,
                    new Vector2(0f, -110f), new Vector2(300f, 70f), TextAlignmentOptions.Center, kTextDark);

                // 悬停高亮层：MenuButton 控制它的 alpha
                GameObject hlGo = NewUI("High Light", card.transform);
                rt(hlGo).anchorMin = Vector2.zero;
                rt(hlGo).anchorMax = Vector2.one;
                rt(hlGo).pivot = new Vector2(0.5f, 0.5f);
                rt(hlGo).anchoredPosition = Vector2.zero;
                rt(hlGo).sizeDelta = Vector2.zero;
                Image hl = hlGo.AddComponent<Image>();
                hl.sprite = round;
                hl.type = Image.Type.Sliced;
                hl.color = new Color(kHighlight.r, kHighlight.g, kHighlight.b, 0f);
                hl.raycastTarget = false;

                // 悬停时放大关卡名
                MenuButton mb = card.AddComponent<MenuButton>();
                SerializedObject so = new SerializedObject(mb);
                SetProp(so, "m_Background", hl);
                SetProp(so, "m_Text", label.rectTransform);
                SetProp(so, "m_ActiveAlpha", 0.9f);
                SetProp(so, "m_TextScale", 1.08f);
                so.ApplyModifiedPropertiesWithoutUndo();

                levels.Add(new LevelSelectPanel.LevelEntry
                {
                    root = card,
                    label = label,
                    button = cardButton,
                    sceneName = sceneName
                });
                levelButtons.Add(cardButton);
            }

            // 关卡按钮导航：左右 + 上下，按网格走
            for (int i = 0; i < levelButtons.Count; i++)
            {
                Navigation nav = levelButtons[i].navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnRight = levelButtons[(i + 1) % levelButtons.Count];
                nav.selectOnLeft = levelButtons[(i - 1 + levelButtons.Count) % levelButtons.Count];
                nav.selectOnDown = levelButtons[(i + 3) % levelButtons.Count];
                nav.selectOnUp = levelButtons[(i - 3 + levelButtons.Count) % levelButtons.Count];
                levelButtons[i].navigation = nav;
            }

            // ---------- 5. 挂控制器并接线 ----------
            LevelSelectPanel panel = root.AddComponent<LevelSelectPanel>();
            panel.Configure(new LevelSelectPanel.Config
            {
                root = root,
                closeButton = closeBtn,
                levels = levels.ToArray(),
                menuButtons = menuButtons.ToArray(),
                hideCloseButton = false
            });

            // ---------- 6. 主菜单入口按钮 ----------
            // 放在右侧，和 Settings Button（694, -106）竖排对齐
            GameObject entry = NewUI(kEntryName, canvas.transform);
            rt(entry).anchorMin = new Vector2(0.5f, 0.5f);
            rt(entry).anchorMax = new Vector2(0.5f, 0.5f);
            rt(entry).pivot = new Vector2(0.5f, 0.5f);
            rt(entry).anchoredPosition = new Vector2(694f, -226f);
            rt(entry).sizeDelta = new Vector2(340f, 100f);

            GameObject entryBgGo = NewUI("Background", entry.transform);
            rt(entryBgGo).anchorMin = Vector2.zero;
            rt(entryBgGo).anchorMax = Vector2.one;
            rt(entryBgGo).pivot = new Vector2(0.5f, 0.5f);
            rt(entryBgGo).anchoredPosition = Vector2.zero;
            rt(entryBgGo).sizeDelta = Vector2.zero;
            Image entryBg = entryBgGo.AddComponent<Image>();
            entryBg.sprite = round;
            entryBg.type = Image.Type.Sliced;
            entryBg.color = new Color(kHighlight.r, kHighlight.g, kHighlight.b, 0f);
            entryBg.raycastTarget = false;

            Image entryHit = entry.AddComponent<Image>();
            entryHit.color = new Color(1f, 1f, 1f, 0f);
            entryHit.raycastTarget = true;

            Button entryButton = entry.AddComponent<Button>();
            entryButton.transition = Selectable.Transition.None;
            entryButton.targetGraphic = entryHit;

            GameObject entryTextGo = NewUI("文字", entry.transform);
            rt(entryTextGo).anchorMin = Vector2.zero;
            rt(entryTextGo).anchorMax = Vector2.one;
            rt(entryTextGo).pivot = new Vector2(0.5f, 0.5f);
            rt(entryTextGo).anchoredPosition = Vector2.zero;
            rt(entryTextGo).sizeDelta = Vector2.zero;
            TMP_Text entryText = entryTextGo.AddComponent<TextMeshProUGUI>();
            entryText.text = "关卡选择";
            entryText.fontSize = 40;
            entryText.color = kTextDark;
            entryText.alignment = TextAlignmentOptions.Center;
            entryText.raycastTarget = false;

            MenuButton entryMb = entry.AddComponent<MenuButton>();
            SerializedObject entrySo = new SerializedObject(entryMb);
            SetProp(entrySo, "m_Background", entryBg);
            SetProp(entrySo, "m_Text", entryText.rectTransform);
            SetProp(entrySo, "m_ActiveAlpha", 0.9f);
            SetProp(entrySo, "m_TextScale", 1.12f);
            entrySo.ApplyModifiedPropertiesWithoutUndo();

            WireOpen(entryButton, panel);

            // ---------- 7. 面板在场景里保持未激活（运行时由 Open 点亮）----------
            root.SetActive(false);

            // ---------- 8. 把六个 Map 场景加进 Build Settings ----------
            int added = EnsureScenesInBuildSettings();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            string summary = string.Format(
                "关卡选择界面已建好并保存。\n\n" +
                "关卡按钮：{0} 个（Map0 ~ Map5）\n" +
                "入口按钮：{1}（主菜单右侧）\n" +
                "新加入 Build Settings 的场景：{2} 个\n" +
                "主菜单按钮：{3} 个会在面板打开时禁用",
                levels.Count, kEntryName, added, menuButtons.Count);

            Debug.Log("[LevelSelectBuilder] " + summary.Replace("\n", " "));
            Report(auto, summary);
        }

        // ==================== Build Settings ====================

        /// <summary>确保六个 Map 场景都在 Build Settings 里，返回新加的数量。</summary>
        private static int EnsureScenesInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int added = 0;

            for (int i = 0; i < 6; i++)
            {
                string path = kSceneFolder + "Map" + i + ".unity";

                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    Debug.LogWarning("[LevelSelectBuilder] 找不到场景文件：" + path);
                    continue;
                }

                bool present = false;
                for (int s = 0; s < scenes.Count; s++)
                {
                    if (scenes[s].path == path)
                    {
                        present = true;
                        break;
                    }
                }

                if (!present)
                {
                    scenes.Add(new EditorBuildSettingsScene(path, true));
                    added++;
                }
            }

            if (added > 0)
            {
                EditorBuildSettings.scenes = scenes.ToArray();
                Debug.Log("[LevelSelectBuilder] 已把 " + added + " 个 Map 场景加入 Build Settings。");
            }

            return added;
        }

        // ==================== 自动构建 ====================

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
                    if (EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        Debug.Log("[LevelSelectBuilder] 运行中，已跳过自动构建。");
                        return;
                    }

                    Scene active = SceneManager.GetActiveScene();
                    if (active.name != "Menu")
                    {
                        Debug.Log("[LevelSelectBuilder] 当前是 \"" + active.name +
                                  "\"，不是 Menu，已跳过。打开 Menu 后用菜单 PAO → UI → 构建关卡选择面板。");
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
                    Debug.LogError("[LevelSelectBuilder] 自动构建失败：" + e.Message +
                                   "\n请用菜单 PAO → UI → 构建关卡选择面板 手动执行。");
                }
            };
        }

        // ==================== helpers ====================

        private static RectTransform rt(GameObject go)
        {
            return go.GetComponent<RectTransform>();
        }

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");
            rt(go).localScale = Vector3.one;
            return go;
        }

        private static TMP_Text NewLabel(string name, Transform parent, string text, float size,
            Vector2 pos, Vector2 sizeDelta, TextAlignmentOptions align, Color color)
        {
            GameObject go = NewUI(name, parent);
            rt(go).anchorMin = new Vector2(0.5f, 0.5f);
            rt(go).anchorMax = new Vector2(0.5f, 0.5f);
            rt(go).pivot = new Vector2(0.5f, 0.5f);
            rt(go).anchoredPosition = pos;
            rt(go).sizeDelta = sizeDelta;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>简单文字按钮：圆角底 + 居中文字，返回 Button。</summary>
        private static Button NewButton(string name, Transform parent, Vector2 pos, Vector2 size,
            string label, float fontSize, Sprite round, Color bgColor)
        {
            GameObject go = NewUI(name, parent);
            rt(go).anchorMin = new Vector2(0.5f, 0.5f);
            rt(go).anchorMax = new Vector2(0.5f, 0.5f);
            rt(go).pivot = new Vector2(0.5f, 0.5f);
            rt(go).anchoredPosition = pos;
            rt(go).sizeDelta = size;

            Image img = go.AddComponent<Image>();
            img.sprite = round;
            img.type = Image.Type.Sliced;
            img.color = bgColor;
            img.raycastTarget = true;

            Button b = go.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = img;

            NewLabel("文字", go.transform, label, fontSize,
                Vector2.zero, Vector2.zero, TextAlignmentOptions.Center, kTextDark);

            // 让文字铺满按钮
            Transform t = go.transform.Find("文字");
            if (t != null)
            {
                RectTransform tr = t.GetComponent<RectTransform>();
                tr.anchorMin = Vector2.zero;
                tr.anchorMax = Vector2.one;
                tr.anchoredPosition = Vector2.zero;
                tr.sizeDelta = Vector2.zero;
            }

            return b;
        }

        private static void WireOpen(Button button, LevelSelectPanel panel)
        {
            if (button == null || panel == null)
            {
                return;
            }

            SerializedObject so = new SerializedObject(button);
            SerializedProperty calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (calls == null)
            {
                Debug.LogWarning("[LevelSelectBuilder] 找不到 m_OnClick，入口按钮未绑定。");
                return;
            }

            int index = calls.arraySize;
            calls.InsertArrayElementAtIndex(index);

            SerializedProperty call = calls.GetArrayElementAtIndex(index);
            call.FindPropertyRelative("m_Target").objectReferenceValue = panel;
            call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = "PAO.UI.LevelSelectPanel, Assembly-CSharp";
            call.FindPropertyRelative("m_MethodName").stringValue = "Open";
            call.FindPropertyRelative("m_Mode").intValue = 1;
            call.FindPropertyRelative("m_CallState").intValue = 2;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetProp(SerializedObject so, string name, UnityEngine.Object value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null)
            {
                p.objectReferenceValue = value;
            }
        }

        private static void SetProp(SerializedObject so, string name, float value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null)
            {
                p.floatValue = value;
            }
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

        private static void Report(bool auto, string message)
        {
            if (auto)
            {
                Debug.Log("[LevelSelectBuilder] " + message.Replace("\n", " "));
            }
            else
            {
                EditorUtility.DisplayDialog("构建关卡选择面板", message, "好");
            }
        }
    }
}

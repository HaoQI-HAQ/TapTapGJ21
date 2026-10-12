using PAO.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.EditorTools
{
    /// <summary>
    /// 一键搭建「暂停」界面（Control UI 场景）。
    ///
    /// 用编辑器脚本而不是手写场景 YAML：物体 ID、父子关系、序列化引用都交给 Unity，
    /// 不会出现 ID 重复、引用错位、父子冲突。
    ///
    /// 用法：菜单 **PAO → UI → 构建暂停界面**
    /// 幂等：重复执行先删旧的根物体再重建。
    /// </summary>
    public static class PausePanelBuilder
    {
        private const string kRootName = "Pause Panel";
        private const string kSpriteFolder = "Assets/GameMain/UI/Sprites/";
        private const string kAutoBuildKey = "PAO.PausePanel.AutoBuilt.v1";

        private static readonly string[] kMenuLabels = { "地图", "重开", "读档", "设置", "主菜单" };

        // 文字按钮：常态只有文字，悬停/手柄选中时浮出一块青色底
        private static readonly Color kNormalText = new Color(0.29f, 0.37f, 0.43f, 1f);
        private static readonly Color kActiveText = new Color(0.12f, 0.43f, 0.49f, 1f);

        // 背景天蓝
        private static readonly Color kBackground = new Color(0.975f, 0.965f, 0.94f, 0.96f);
        private static readonly Color kHighlight = new Color(0.34f, 0.68f, 0.73f, 1f);
        private static readonly Color kSubText = new Color(0.42f, 0.52f, 0.56f, 1f);

        [MenuItem("PAO/UI/构建暂停界面", false, 21)]
        public static void BuildFromMenu()
        {
            Build(false);
        }

        public static void Build(bool auto)
        {
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Report(auto, "场景里找不到 Canvas。请确认打开的是 Control UI 场景。");
                return;
            }

            Sprite round = AssetDatabase.LoadAssetAtPath<Sprite>(kSpriteFolder + "btn_round.png");
            if (round == null)
            {
                Report(auto, "缺少 Sprite：Assets/GameMain/UI/Sprites/btn_round.png");
                return;
            }

            // ---- 画布缩放对齐 Menu 场景（1920x1080，按宽度匹配）----
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0f;
            }

            // ---- 幂等：先删旧的 ----
            Transform old = canvas.transform.Find(kRootName);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
                Debug.Log("[PausePanelBuilder] 已删除旧的 \"" + kRootName + "\" 并重建。");
            }

            // ---- 根 —— 同时当背景板（天蓝纯色）----
            GameObject root = NewUI(kRootName, canvas.transform);
            Stretch(root.GetComponent<RectTransform>());
            Image bg = root.AddComponent<Image>();
            bg.color = kBackground;
            bg.raycastTarget = true;

            // ---- 左上角：编号 + 标题 ----
            TMP_Text title = NewLabel("标题", root.transform, "05  暂停", 64,
                new Vector2(-800f, 440f), new Vector2(500f, 90f), TextAlignmentOptions.Left, kNormalText);

            TMP_Text backLabel = NewLabel("返回", root.transform, "返回", 40,
                new Vector2(-790f, 350f), new Vector2(300f, 60f), TextAlignmentOptions.Left, kSubText);

            // 返回箭头（纯文字）
            NewLabel("返回箭头", root.transform, "←", 46,
                new Vector2(-880f, 350f), new Vector2(60f, 60f), TextAlignmentOptions.Center, kSubText);

            // ---- 副标题 ----
            NewLabel("副标题箭头", root.transform, "↖", 54,
                new Vector2(-880f, 240f), new Vector2(70f, 70f), TextAlignmentOptions.Center, kSubText);

            NewLabel("副标题", root.transform, "暂停的世界", 46,
                new Vector2(-700f, 240f), new Vector2(360f, 70f), TextAlignmentOptions.Left, kNormalText);

            // ---- 右侧竖排菜单 ----
            // 「继续」是高亮那颗，单独做成有青色底的按钮
            var menuButtons = new System.Collections.Generic.List<MenuButton>();

            MenuButton continueBtn = NewMenuButton("继续", root.transform, new Vector2(700f, 370f),
                new Vector2(640f, 110f), 56, true, round);
            menuButtons.Add(continueBtn);

            // 五个次级条目
            for (int i = 0; i < kMenuLabels.Length; i++)
            {
                float y = 250f - i * 100f;
                MenuButton b = NewMenuButton(kMenuLabels[i], root.transform, new Vector2(700f, y),
                    new Vector2(640f, 90f), 44, false, round);
                menuButtons.Add(b);
            }

            // ---- 「最近保存」小字 ----
            TMP_Text lastSave = NewLabel("最近保存", root.transform, "最近保存", 28,
                new Vector2(700f, -330f), new Vector2(640f, 50f), TextAlignmentOptions.Center, kSubText);

            // ---- 挂 PauseMenu 并接线 ----
            PauseMenu menu = root.AddComponent<PauseMenu>();

            // 按钮回调：继续 / 地图 / 重开 / 读档 / 设置 / 主菜单
            Wire(menuButtons[0].GetComponent<Button>(), menu, "Resume");
            Wire(menuButtons[1].GetComponent<Button>(), menu, "OpenMap");
            Wire(menuButtons[2].GetComponent<Button>(), menu, "RestartLevel");
            Wire(menuButtons[3].GetComponent<Button>(), menu, "OpenLoadPanel");
            Wire(menuButtons[4].GetComponent<Button>(), menu, "OpenSettingsPanel");
            Wire(menuButtons[5].GetComponent<Button>(), menu, "GoToMainMenu");

            // 竖排导航：上下依次走，首尾环绕
            for (int i = 0; i < menuButtons.Count; i++)
            {
                Button b = menuButtons[i].GetComponent<Button>();
                Navigation nav = b.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnDown = menuButtons[(i + 1) % menuButtons.Count].GetComponent<Button>();
                nav.selectOnUp = menuButtons[(i - 1 + menuButtons.Count) % menuButtons.Count].GetComponent<Button>();
                b.navigation = nav;
            }

            // 场景里如果已经有读档/设置面板，顺手接上
            LoadPanel loadPanel = Object.FindObjectOfType<LoadPanel>(true);
            SettingsPanel settingsPanel = Object.FindObjectOfType<SettingsPanel>(true);
            SetPrivate(menu, "m_LoadPanel", loadPanel);
            SetPrivate(menu, "m_SettingsPanel", settingsPanel);
            SetPrivate(menu, "m_LastSaveLabel", lastSave);

            SerializedObject pauseSettings = new SerializedObject(menu);
            pauseSettings.FindProperty("m_PauseTimeOnEnable").boolValue = false;
            pauseSettings.FindProperty("m_PauseTimeOnOpen").boolValue = true;
            pauseSettings.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(false);

            // 手柄/键盘进来就有落点
            EventSystem es = Object.FindObjectOfType<EventSystem>();
            if (es != null)
            {
                es.firstSelectedGameObject = null;
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            string summary = string.Format(
                "暂停界面已建好并保存。\n\n按钮：{0} 个\n读档面板：{1}\n设置面板：{2}\n画布缩放：1920x1080 按宽度匹配",
                menuButtons.Count,
                loadPanel != null ? "已接上" : "场景里没有，按钮禁用",
                settingsPanel != null ? "已接上" : "场景里没有，按钮禁用");

            Debug.Log("[PausePanelBuilder] " + summary.Replace("\n", " "));
            Report(auto, summary);
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
                        Debug.Log("[PausePanelBuilder] 运行中，已跳过自动构建。退出 Play 后重试，或用菜单。");
                        return;
                    }

                    Scene active = SceneManager.GetActiveScene();
                    if (active.name != "Control UI")
                    {
                        Debug.Log("[PausePanelBuilder] 当前场景是 \"" + active.name +
                                  "\"，不是 Control UI，已跳过。打开该场景后按 Ctrl+R，或用菜单 PAO → UI → 构建暂停界面。");
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
                    Debug.LogError("[PausePanelBuilder] 自动构建失败：" + e.Message +
                                   "\n请用菜单 PAO → UI → 构建暂停界面 手动执行。");
                }
            };
        }

        // ==================== helpers ====================

        private static void Report(bool auto, string message)
        {
            if (auto)
            {
                Debug.Log("[PausePanelBuilder] " + message.Replace("\n", " "));
            }
            else
            {
                EditorUtility.DisplayDialog("构建暂停界面", message, "好");
            }
        }

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");
            var rt = go.GetComponent<RectTransform>();
            rt.localScale = Vector3.one;
            return go;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }

        /// <summary>居中锚点、显式尺寸的纯文字标签。</summary>
        private static TMP_Text NewLabel(string name, Transform parent, string text, float size,
            Vector2 pos, Vector2 sizeDelta, TextAlignmentOptions align, Color color)
        {
            GameObject go = NewUI(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeDelta;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>
        /// 文字按钮：根物体挂一个透明 Image 作点击区 + MenuButton，
        /// 里面一个可伸缩的文字子物体（MenuButton 悬停时放大它）。
        /// withHighlight = true 时额外给一层圆角底（「继续」用）。
        /// </summary>
        private static MenuButton NewMenuButton(string label, Transform parent, Vector2 pos, Vector2 size,
            float fontSize, bool withHighlight, Sprite round)
        {
            GameObject go = NewUI(label + " Button", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            // 底图：MenuButton 控制它的 alpha（0 = 常态不可见）
            GameObject bgGo = NewUI("Background", go.transform);
            Stretch(bgGo.GetComponent<RectTransform>());
            Image bg = bgGo.AddComponent<Image>();
            bg.sprite = round;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(kHighlight.r, kHighlight.g, kHighlight.b, withHighlight ? 1f : 0f);
            bg.raycastTarget = false;

            // 点击区（透明，但可接收射线）
            Image hit = go.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;

            Button button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;

            // 文字
            GameObject textGo = NewUI("文字", go.transform);
            var textRt = textGo.GetComponent<RectTransform>();
            Stretch(textRt);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = fontSize;
            tmp.color = kNormalText;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            // MenuButton：悬停/手柄选中时把底图淡入、文字放大
            MenuButton mb = go.AddComponent<MenuButton>();

            // MenuButton 的字段是 private SerializeField，用 SerializedObject 赋值
            SerializedObject so = new SerializedObject(mb);
            SetProp(so, "m_Background", bg);
            SetProp(so, "m_Text", textRt);
            SetProp(so, "m_ActiveAlpha", withHighlight ? 1f : 0.9f);
            SetProp(so, "m_TextScale", 1.06f);
            so.ApplyModifiedPropertiesWithoutUndo();

            return mb;
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

        private static void SetPrivate(Component target, string field, UnityEngine.Object value)
        {
            if (target == null)
            {
                return;
            }

            SerializedObject so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p != null)
            {
                p.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void Wire(Button button, PauseMenu menu, string method)
        {
            if (button == null || menu == null)
            {
                return;
            }

            SerializedObject so = new SerializedObject(button);
            SerializedProperty calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (calls == null)
            {
                Debug.LogWarning("[PausePanelBuilder] 找不到 m_OnClick，按钮回调未绑定：" + method);
                return;
            }

            int index = calls.arraySize;
            calls.InsertArrayElementAtIndex(index);

            SerializedProperty call = calls.GetArrayElementAtIndex(index);
            call.FindPropertyRelative("m_Target").objectReferenceValue = menu;
            call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = "PAO.UI.PauseMenu, Assembly-CSharp";
            call.FindPropertyRelative("m_MethodName").stringValue = method;
            call.FindPropertyRelative("m_Mode").intValue = 1;      // Void
            call.FindPropertyRelative("m_CallState").intValue = 2; // RuntimeOnly

            SerializedProperty args = call.FindPropertyRelative("m_Arguments");
            if (args != null)
            {
                SerializedProperty objArg = args.FindPropertyRelative("m_ObjectArgument");
                if (objArg != null) { objArg.objectReferenceValue = null; }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

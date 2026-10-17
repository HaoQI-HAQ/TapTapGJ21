using System.Collections.Generic;
using PAO;
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
    /// 给玩法场景装上暂停面板与 HUD。
    ///
    /// 两个界面都做成 Prefab 放在 Assets/GameMain/UI/，本脚本负责：
    ///   1. 场景原本没有 UI 基础设施 → 补上 Canvas（1920x1080 缩放）+ EventSystem
    ///   2. 生成 / 复用 Pause Panel.prefab 与 Level HUD.prefab
    ///   3. 把两个预制体实例化进场景，并确保场景里有 BubbleTypeSwitcher
    ///
    /// 按 Esc 由 HUD 里的 BubbleTypeHud 收，开关 Pause Panel 并暂停/恢复时间。
    ///
    /// 【目标场景】
    /// 场景名从参数进来，所以任何玩法场景都能用同一套流程。
    /// 目前两个入口：
    ///   · **PAO → UI → 在 cs 场景装暂停与 HUD**（默认，走 kTargetScene）
    ///   · **PAO → UI → 在 shiliuye map 装暂停与 HUD**
    ///
    /// 自动钩子**只服务 cs**（见 AutoBuildTick 里的说明）——
    /// 别的关卡要装就显式点菜单，免得场景被悄悄改动。
    ///
    /// 幂等：重复执行会复用已有预制体、并替换场景里的旧实例（不会堆叠）。
    /// </summary>
    public static class PauseHudSetup
    {
        private const string kPrefabFolder = "Assets/GameMain/UI/";
        private const string kPausePrefabPath = kPrefabFolder + "Pause Panel.prefab";
        private const string kHudPrefabPath = kPrefabFolder + "Level HUD.prefab";
        private const string kSpriteFolder = "Assets/GameMain/UI/Sprites/";
        private const string kTargetScene = "cs";
        private const string kCanvasName = "HUD Canvas";

        private static readonly string[] kIconNames = { "icon_sticky", "icon_bouncy", "icon_bomb" };

        private static readonly Vector2[] kCirclePos =
        {
            new Vector2(0f, 150f),
            new Vector2(-150f, -70f),
            new Vector2(150f, -70f)
        };

        private const float kCircleSize = 150f;

        // ==================== 菜单 ====================

        [MenuItem("PAO/UI/在 cs 场景装暂停与 HUD", false, 24)]
        public static void BuildFromMenu()
        {
            Build(false, false, kTargetScene);
        }

        [MenuItem("PAO/UI/在 shiliuye map 装暂停与 HUD", false, 26)]
        public static void BuildShiliuyeFromMenu()
        {
            Build(false, false, "shiliuye map");
        }

        [MenuItem("PAO/UI/重建两个界面预制体", false, 25)]
        public static void RebuildPrefabsFromMenu()
        {
            Build(false, true, kTargetScene);
        }

        /// <summary>
        /// 在 <paramref name="targetSceneName"/> 里装暂停面板与 HUD。
        ///
        /// 【为什么要带场景名参数】
        /// 早先这个方法把 "cs" 写死在两个地方：入口的校验、以及一串日志文案。
        /// 结果别的关卡（比如 shiliuye map）没法复用这套安装流程 ——
        /// 明明 HUD 与暂停界面是每个玩法场景都要的，却只能装进 cs。
        /// 现在场景名从参数进来，cs 走默认值，行为完全不变。
        /// </summary>
        public static void Build(bool auto, bool forceRebuildPrefabs, string targetSceneName)
        {
            // ---------- 0. 前置 ----------
            Sprite round = AssetDatabase.LoadAssetAtPath<Sprite>(kSpriteFolder + "btn_round.png");
            Sprite ring = AssetDatabase.LoadAssetAtPath<Sprite>(kSpriteFolder + "bubble_ring.png");
            if (round == null || ring == null)
            {
                Report(auto, "缺少 Sprite：需要 btn_round.png 与 bubble_ring.png 在 " + kSpriteFolder);
                return;
            }

            // 必须在目标场景里操作
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != targetSceneName)
            {
                Report(auto, "当前场景是 \"" + scene.name + "\"，请先打开 " + targetSceneName + ".unity 再执行。");
                return;
            }

            // ---------- 1. 预制体 ----------
            GameObject pausePrefab = EnsurePausePrefab(round, forceRebuildPrefabs);
            GameObject hudPrefab = EnsureHudPrefab(ring, forceRebuildPrefabs);

            if (pausePrefab == null || hudPrefab == null)
            {
                Report(auto, "预制体生成失败，请查看 Console。");
                return;
            }

            // ---------- 2. UI 基础设施 ----------
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasGo = new GameObject(kCanvasName);
                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0f;

                canvasGo.AddComponent<GraphicRaycaster>();
                Debug.Log("[PauseHudSetup] " + scene.name + " 原本没有 Canvas，已创建 \"" + kCanvasName + "\"。");
            }

            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                GameObject esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
                Debug.Log("[PauseHudSetup] " + scene.name + " 原本没有 EventSystem，已创建。");
            }

            // ---------- 3. 泡泡类型切换器 ----------
            if (Object.FindObjectOfType<BubbleTypeSwitcher>(true) == null)
            {
                var switcherGo = new GameObject("Bubble Type Switcher");
                switcherGo.AddComponent<BubbleTypeSwitcher>();
                Debug.Log("[PauseHudSetup] " + scene.name + " 没有 BubbleTypeSwitcher，已创建。");
            }

            // ---------- 4. 实例化（先清旧的）----------
            int replaced = 0;
            replaced += ReplaceInstance(canvas.transform, "Pause Panel", pausePrefab);
            replaced += ReplaceInstance(canvas.transform, "Level HUD", hudPrefab);

            // ---------- 5. 接线：HUD -> PauseMenu ----------
            GameObject hudInstance = FindDeep(canvas.transform, "Level HUD");
            PauseMenu pauseMenu = Object.FindObjectOfType<PauseMenu>(true);

            if (hudInstance != null && pauseMenu != null)
            {
                BubbleTypeHud hud = hudInstance.GetComponent<BubbleTypeHud>();
                if (hud != null)
                {
                    SerializedObject so = new SerializedObject(hud);
                    SerializedProperty p = so.FindProperty("m_PauseMenu");
                    if (p != null)
                    {
                        p.objectReferenceValue = pauseMenu;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }

            // 暂停面板常态隐藏，按 Esc 才出现
            if (pauseMenu != null)
            {
                SerializedObject pauseSo = new SerializedObject(pauseMenu);
                SetBool(pauseSo, "m_PauseTimeOnEnable", false);
                SetBool(pauseSo, "m_PauseTimeOnOpen", true);
                pauseSo.ApplyModifiedPropertiesWithoutUndo();

                pauseMenu.gameObject.SetActive(false);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();

            string summary = string.Format(
                scene.name + " 已装好。\n\n" +
                "预制体：\n  {0}\n  {1}\n\n" +
                "场景内新增：Canvas + EventSystem + 两个界面实例\n" +
                "替换的旧实例：{2} 个\n" +
                "接线：HUD → PauseMenu {3}\n\n" +
                "进 Play 后：右下角三个类型圆，按 Esc 弹出暂停面板",
                kPausePrefabPath, kHudPrefabPath, replaced,
                (hudInstance != null && pauseMenu != null) ? "成功" : "失败（见 Console）");

            Debug.Log("[PauseHudSetup] " + summary.Replace("\n", " "));
            Report(auto, summary);
        }

        // ==================== 预制体 ====================

        /// <summary>已有就用现成的；force 为 true 时重建。</summary>
        private static GameObject EnsurePausePrefab(Sprite round, bool force)
        {
            if (!force)
            {
                GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(kPausePrefabPath);
                if (existing != null)
                {
                    return existing;
                }
            }

            GameObject root = BuildPausePanelTree(null, round);
            GameObject prefab = SavePrefab(root, kPausePrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[PauseHudSetup] 已生成预制体 " + kPausePrefabPath);
            return prefab;
        }

        private static GameObject EnsureHudPrefab(Sprite ring, bool force)
        {
            if (!force)
            {
                GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(kHudPrefabPath);
                if (existing != null)
                {
                    return existing;
                }
            }

            GameObject root = BuildHudTree(null, ring);
            GameObject prefab = SavePrefab(root, kHudPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[PauseHudSetup] 已生成预制体 " + kHudPrefabPath);
            return prefab;
        }

        private static GameObject SavePrefab(GameObject root, string path)
        {
            if (!AssetDatabase.IsValidFolder(kPrefabFolder.TrimEnd('/')))
            {
                AssetDatabase.CreateFolder("Assets/GameMain", "UI");
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return prefab;
        }

        // ==================== 暂停面板 ====================

        /// <summary>
        /// 暂停面板的完整结构。parent 为空时是「预制体内容」，
        /// 根节点自己做全屏拉伸。
        /// </summary>
        private static GameObject BuildPausePanelTree(Transform parent, Sprite round)
        {
            GameObject root = NewUI("Pause Panel", parent);
            Stretch(root);

            Image bg = root.AddComponent<Image>();
            bg.color = new Color(0.62f, 0.88f, 0.97f, 1f);
            bg.raycastTarget = true;

            NewLabel("标题", root.transform, "05  暂停", 64,
                new Vector2(-800f, 440f), new Vector2(500f, 90f), TextAlignmentOptions.Left, kTextDark);

            NewLabel("返回箭头", root.transform, "←", 46,
                new Vector2(-880f, 350f), new Vector2(60f, 60f), TextAlignmentOptions.Center, kSubText);

            NewLabel("返回", root.transform, "返回", 40,
                new Vector2(-790f, 350f), new Vector2(300f, 60f), TextAlignmentOptions.Left, kSubText);

            NewLabel("副标题箭头", root.transform, "↖", 54,
                new Vector2(-880f, 240f), new Vector2(70f, 70f), TextAlignmentOptions.Center, kSubText);

            NewLabel("副标题", root.transform, "暂停的世界", 46,
                new Vector2(-700f, 240f), new Vector2(360f, 70f), TextAlignmentOptions.Left, kTextDark);

            // 右侧竖排 6 个按钮：继续（常亮高亮）+ 五个次级
            var buttons = new List<MenuButton>();

            buttons.Add(NewMenuButton("继续", root.transform, new Vector2(700f, 370f),
                new Vector2(640f, 110f), 56, true, round));

            // 竖排次级条目。顺序只影响视觉排版和上下导航顺序，不再决定接哪个回调
            // （回调按按钮名字接，见下面 WireByName）
            string[] secondary = { "地图", "重开", "存档", "读档", "设置", "主菜单" };
            for (int i = 0; i < secondary.Length; i++)
            {
                float y = 250f - i * 100f;
                buttons.Add(NewMenuButton(secondary[i], root.transform, new Vector2(700f, y),
                    new Vector2(640f, 90f), 44, false, round));
            }

            TMP_Text lastSave = NewLabel("最近保存", root.transform, "最近保存", 28,
                new Vector2(700f, -330f), new Vector2(640f, 50f), TextAlignmentOptions.Center, kSubText);

            PauseMenu menu = root.AddComponent<PauseMenu>();

            // 按钮回调：按**按钮名字**接，不按位置。
            //
            // 【为什么要这样】
            // 原来写法是 methods[i] 对应 buttons[i]，靠位置对齐。但按钮的实际顺序
            // 取决于 Objects 的层级顺序，一旦建按钮的顺序和数组顺序不一致
            // （加了一个「存档」就会这样），回调就会整体错位 ——
            // 表现为"点存档却弹读档"，而且**不会报任何编译错误**，极难发现。
            // 按名字接就没有这个隐患。
            WireByName(buttons, "继续", menu, "Resume");
            WireByName(buttons, "地图", menu, "OpenMap");
            WireByName(buttons, "重开", menu, "RestartLevel");
            WireByName(buttons, "存档", menu, "OpenSavePanel");
            WireByName(buttons, "读档", menu, "OpenLoadPanel");
            WireByName(buttons, "设置", menu, "OpenSettingsPanel");
            WireByName(buttons, "主菜单", menu, "GoToMainMenu");

            // 竖排导航环绕。
            // 排序基准是按钮的视觉高度（anchoredPosition.y），不是列表顺序 ——
            // 列表顺序取决于建按钮的先后，和玩家看到的上下顺序不一定一致。
            var navOrder = new List<MenuButton>(buttons);
            navOrder.Sort(delegate (MenuButton a, MenuButton b)
            {
                float ay = a != null ? a.GetComponent<RectTransform>().anchoredPosition.y : 0f;
                float by = b != null ? b.GetComponent<RectTransform>().anchoredPosition.y : 0f;
                return by.CompareTo(ay);   // 从高到低
            });

            for (int i = 0; i < navOrder.Count; i++)
            {
                Button b = navOrder[i].GetComponent<Button>();
                Navigation nav = b.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnDown = navOrder[(i + 1) % navOrder.Count].GetComponent<Button>();
                nav.selectOnUp = navOrder[(i - 1 + navOrder.Count) % navOrder.Count].GetComponent<Button>();
                b.navigation = nav;
            }

            SerializedObject so = new SerializedObject(menu);
            SetBool(so, "m_PauseTimeOnEnable", false);
            SetBool(so, "m_PauseTimeOnOpen", true);
            SetObject(so, "m_LastSaveLabel", lastSave);
            so.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        // ==================== HUD ====================

        private static GameObject BuildHudTree(Transform parent, Sprite ring)
        {
            GameObject root = NewUI("Level HUD", parent);

            RectTransform rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-30f, 30f);
            rt.sizeDelta = new Vector2(540f, 540f);

            var slots = new List<BubbleTypeHud.TypeSlot>();

            for (int i = 0; i < BubbleTypeSwitcher.TypeCount; i++)
            {
                BubbleType type = (BubbleType)i;
                string label = BubbleTypeSwitcher.DisplayNameOf(type);

                GameObject circle = NewUI(label, root.transform);
                SetRect(circle, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    kCirclePos[i], new Vector2(kCircleSize, kCircleSize));

                Image hit = circle.AddComponent<Image>();
                hit.color = new Color(1f, 1f, 1f, 0f);
                hit.raycastTarget = true;

                Button button = circle.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.targetGraphic = hit;

                GameObject ringGo = NewUI("Ring", circle.transform);
                Stretch(ringGo);
                Image ringImage = ringGo.AddComponent<Image>();
                ringImage.sprite = ring;
                ringImage.type = Image.Type.Simple;
                ringImage.color = BubbleTypeSwitcher.ColorOf(type);
                ringImage.raycastTarget = false;

                Sprite iconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(kSpriteFolder + kIconNames[i] + ".png");
                Image iconImage = null;
                if (iconSprite != null)
                {
                    GameObject iconGo = NewUI("Icon", circle.transform);
                    SetRect(iconGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        Vector2.zero, new Vector2(kCircleSize * 0.55f, kCircleSize * 0.55f));
                    iconImage = iconGo.AddComponent<Image>();
                    iconImage.sprite = iconSprite;
                    iconImage.color = Color.white;
                    iconImage.raycastTarget = false;
                }

                GameObject labelGo = NewUI("Label", circle.transform);
                SetRect(labelGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, -kCircleSize * 0.72f), new Vector2(kCircleSize * 1.6f, 46f));
                var tmp = labelGo.AddComponent<TextMeshProUGUI>();
                tmp.text = label;
                tmp.fontSize = 30;
                tmp.color = new Color(0.15f, 0.15f, 0.15f, 1f);
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.raycastTarget = false;

                slots.Add(new BubbleTypeHud.TypeSlot
                {
                    root = circle,
                    ring = ringImage,
                    icon = iconImage,
                    label = tmp,
                    button = button
                });
            }

            for (int i = 0; i < slots.Count; i++)
            {
                Navigation nav = slots[i].button.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnRight = slots[(i + 1) % slots.Count].button;
                nav.selectOnLeft = slots[(i - 1 + slots.Count) % slots.Count].button;
                slots[i].button.navigation = nav;
            }

            BubbleTypeHud hud = root.AddComponent<BubbleTypeHud>();
            hud.Configure(slots.ToArray(), null);   // PauseMenu 运行时自动查找

            return root;
        }

        // ==================== 通用 ====================

        private static readonly Color kTextDark = new Color(0.20f, 0.20f, 0.20f, 1f);
        private static readonly Color kSubText = new Color(0.35f, 0.52f, 0.56f, 1f);
        private static readonly Color kHighlight = new Color(0.45f, 0.83f, 0.87f, 1f);

        /// <summary>把场景里同名旧实例换成新的预制体实例，返回替换数量。</summary>
        private static int ReplaceInstance(Transform parent, string name, GameObject prefab)
        {
            int count = 0;
            GameObject old = FindDeep(parent, name);
            while (old != null)
            {
                Object.DestroyImmediate(old);
                count++;
                old = FindDeep(parent, name);
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            return count;
        }

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            go.layer = LayerMask.NameToLayer("UI");
            go.GetComponent<RectTransform>().localScale = Vector3.one;
            return go;
        }

        private static void Stretch(GameObject go)
        {
            SetRect(go, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        }

        private static void SetRect(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
        }

        private static TMP_Text NewLabel(string name, Transform parent, string text, float size,
            Vector2 pos, Vector2 sizeDelta, TextAlignmentOptions align, Color color)
        {
            GameObject go = NewUI(name, parent);
            SetRect(go, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, sizeDelta);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>文字按钮：透明点击区 + 可淡入的圆角底 + 可放大的文字。</summary>
        private static MenuButton NewMenuButton(string label, Transform parent, Vector2 pos, Vector2 size,
            float fontSize, bool withHighlight, Sprite round)
        {
            GameObject go = NewUI(label + " Button", parent);
            SetRect(go, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);

            GameObject bgGo = NewUI("Background", go.transform);
            Stretch(bgGo);
            Image bg = bgGo.AddComponent<Image>();
            bg.sprite = round;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(kHighlight.r, kHighlight.g, kHighlight.b, withHighlight ? 1f : 0f);
            bg.raycastTarget = false;

            Image hit = go.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;

            Button button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;

            GameObject textGo = NewUI("文字", go.transform);
            Stretch(textGo);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = fontSize;
            tmp.color = kTextDark;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            MenuButton mb = go.AddComponent<MenuButton>();
            SerializedObject so = new SerializedObject(mb);
            SetObject(so, "m_Background", bg);
            SetObject(so, "m_Text", textGo.GetComponent<RectTransform>());
            SetFloat(so, "m_ActiveAlpha", withHighlight ? 1f : 0.9f);
            SetFloat(so, "m_TextScale", 1.06f);
            so.ApplyModifiedPropertiesWithoutUndo();

            return mb;
        }

        /// <summary>
        /// 按按钮名字接回调（名字形如「存档 Button」）。
        ///
        /// 【为什么不用位置】
        /// 位置接线要求"建按钮的顺序"和"方法数组的顺序"严格一致，
        /// 任何一处增删都会整体错位，而且**不报编译错误** ——
        /// 表现就是"点存档却弹读档"。按名字匹配从根上避免这个问题。
        /// </summary>
        private static void WireByName(List<MenuButton> buttons, string label, PauseMenu menu, string method)
        {
            if (buttons == null)
            {
                return;
            }

            for (int i = 0; i < buttons.Count; i++)
            {
                MenuButton mb = buttons[i];
                if (mb == null)
                {
                    continue;
                }

                if (mb.gameObject.name.StartsWith(label, System.StringComparison.Ordinal))
                {
                    Wire(mb.GetComponent<Button>(), menu, method);
                    return;
                }
            }

            Debug.LogWarning("[PauseHudSetup] 没找到「" + label + "」按钮，回调 " + method + " 未绑定。");
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
                Debug.LogWarning("[PauseHudSetup] 找不到 m_OnClick，回调未绑定：" + method);
                return;
            }

            int index = calls.arraySize;
            calls.InsertArrayElementAtIndex(index);

            SerializedProperty call = calls.GetArrayElementAtIndex(index);
            call.FindPropertyRelative("m_Target").objectReferenceValue = menu;
            call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = "PAO.UI.PauseMenu, Assembly-CSharp";
            call.FindPropertyRelative("m_MethodName").stringValue = method;
            call.FindPropertyRelative("m_Mode").intValue = 1;
            call.FindPropertyRelative("m_CallState").intValue = 2;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(SerializedObject so, string name, bool value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) { p.boolValue = value; }
        }

        private static void SetFloat(SerializedObject so, string name, float value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) { p.floatValue = value; }
        }

        private static void SetObject(SerializedObject so, string name, UnityEngine.Object value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) { p.objectReferenceValue = value; }
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
                Debug.Log("[PauseHudSetup] " + message.Replace("\n", " "));
            }
            else
            {
                EditorUtility.DisplayDialog("暂停与 HUD", message, "好");
            }
        }

        // ==================== 自动构建 ====================

        private static bool s_Hooked;

        /// <summary>
        /// 自动安装。挂一个 EditorApplication.update 常驻回调，
        /// 只要「预制体还不存在」并且当前是目标场景就重试 —— 直到成功为止。
        ///
        /// 为什么不用「试一次 + EditorPrefs 标记」：
        /// 之前那次失败（Sprite 还没导入完）会把机会用掉，之后哪怕 Sprites 修好了、
        /// 也不会再重试，只能手动点菜单。而用户看不到菜单执行结果，就表现为
        /// 「按 Esc 没反应」却查不出原因。改成以「预制体文件是否存在」为判据更可靠。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void HookAutoBuild()
        {
            if (s_Hooked)
            {
                return;
            }

            s_Hooked = true;
            EditorApplication.update += AutoBuildTick;
        }

        private static void AutoBuildTick()
        {
            // 运行中 / 编译中都不动场景
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                return;
            }

            // 只在目标场景里装
            if (SceneManager.GetActiveScene().name != kTargetScene)
            {
                return;
            }

            bool prefabsReady =
                AssetDatabase.LoadAssetAtPath<GameObject>(kPausePrefabPath) != null &&
                AssetDatabase.LoadAssetAtPath<GameObject>(kHudPrefabPath) != null;

            bool sceneReady = Object.FindObjectOfType<Canvas>() != null;

            if (prefabsReady && sceneReady)
            {
                // 装好了，摘掉每帧回调
                EditorApplication.update -= AutoBuildTick;
                return;
            }

            try
            {
                // 自动钩子**只服务 cs** —— 这里硬用 kTargetScene，不走参数。
                //
                // 为什么不给每个新关卡都自动装：自动装会在你打开场景时
                // 悄悄改场景内容并保存。cs 是既有流程、这么干已经习惯了；
                // 但别的关卡（比如 shiliuye map）应该由人显式点菜单，
                // 免得"我什么都没动，场景怎么变了"。
                Build(true, false, kTargetScene);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[PauseHudSetup] 自动安装失败：" + e.Message);
            }
        }
    }
}

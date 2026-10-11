using System.Collections.Generic;
using PAO.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PAO.EditorTools
{
    /// <summary>
    /// 把关卡选择面板改成「蜿蜒路径 + 可左右滑动」的布局。
    ///
    /// 布局要点：
    ///   · 六个关卡节点排在一段水平正弦曲线上，弯折明显
    ///   · 相邻节点之间用一段带弧度的细线连起来（双向交替，形成蜿蜒感）
    ///   · 当前节点居中、放大、填实内圈，下方标「当前」
    ///   · 左右各一个大圆箭头按钮，另有「开始」与「关闭」
    ///
    /// 交互由 LevelPathPanel 负责：左右滑动 / 拖动 / 箭头 / A·D / ←·→ / 滚轮 切换，
    /// Enter·空格 / 点当前节点 / 点「开始」进入关卡，Esc 关闭。
    ///
    /// 用法：菜单 **PAO → UI → 构建关卡路径布局（Level Select）**
    /// 幂等：重复执行会重建关卡面板，并把旧的 LevelSelectPanel 自动禁用。
    /// </summary>
    public static class LevelPathSetup
    {
        private const string kPanelName = "Level Select Panel";
        private const string kEntryName = "关卡选择 Button";
        private const string kSpriteFolder = "Assets/GameMain/UI/Sprites/";
        private const string kSceneFolder = "Assets/GameMain/Scenes/";
        private const string kTargetScene = "Menu";
        private const string kAutoBuildKey = "PAO.LevelPath.AutoBuilt.v1";

        // 与 LevelPathPanel 的默认值保持一致；构建器会把这些写进组件，
        // 保证「所见即所得」——编辑器里摆的位置就是运行时的位置。
        private const float kSpacing = 360f;
        private const float kAmplitude = 150f;
        private const float kWaveLength = 2.6f;
        private const float kVerticalOffset = 0f;

        private const float kNodeSize = 150f;
        private const float kInnerSize = 64f;
        private const float kSoftSize = 230f;

        private static readonly Color kPanelBg = new Color(0.97f, 0.99f, 1f, 1f);
        private static readonly Color kTextDark = new Color(0.20f, 0.20f, 0.20f, 1f);
        private static readonly Color kTextTeal = new Color(0.16f, 0.59f, 0.66f, 1f);
        private static readonly Color kTextFaint = new Color(0.25f, 0.45f, 0.50f, 0.85f);
        private static readonly Color kDiscIdle = new Color(0.72f, 0.87f, 0.92f, 0.85f);
        private static readonly Color kDiscFocus = new Color(0.55f, 0.85f, 0.90f, 0.95f);
        private static readonly Color kArrowBg = new Color(0.68f, 0.86f, 0.90f, 0.85f);
        private static readonly Color kStartBg = new Color(0.62f, 0.85f, 0.89f, 0.95f);
        private static readonly Color kLineColor = new Color(0.62f, 0.80f, 0.86f, 0.85f);

        [MenuItem("PAO/UI/构建关卡路径布局（Level Select）", false, 26)]
        public static void BuildFromMenu()
        {
            Build(false);
        }

        public static void Build(bool auto)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != kTargetScene)
            {
                Report(auto, "当前场景是 \"" + scene.name + "\"，请先打开 " + kTargetScene + ".unity。");
                return;
            }

            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Report(auto, "场景里找不到 Canvas。");
                return;
            }

            Sprite disc = AssetDatabase.LoadAssetAtPath<Sprite>(kSpriteFolder + "slot_ring.png");
            Sprite soft = AssetDatabase.LoadAssetAtPath<Sprite>(kSpriteFolder + "soft_circle.png");
            Sprite connUp = AssetDatabase.LoadAssetAtPath<Sprite>(kSpriteFolder + "path_connector.png");
            Sprite connDown = AssetDatabase.LoadAssetAtPath<Sprite>(kSpriteFolder + "path_connector_down.png");
            Sprite round = AssetDatabase.LoadAssetAtPath<Sprite>(kSpriteFolder + "btn_round.png");

            if (disc == null || soft == null || connUp == null || connDown == null || round == null)
            {
                Report(auto, "缺少 Sprite。需要 slot_ring / soft_circle / path_connector / " +
                             "path_connector_down / btn_round 都在 " + kSpriteFolder);
                return;
            }

            // ---------- 收集主菜单按钮 ----------
            var menuButtons = new List<MenuButton>();
            foreach (string n in new[] { "Start Button", "Settings Button", "Exit Button",
                                         "Loading Button", "Continue Button", kEntryName })
            {
                GameObject go = FindDeep(canvas.transform, n);
                if (go == null) { continue; }
                MenuButton mb = go.GetComponent<MenuButton>();
                if (mb != null) { menuButtons.Add(mb); }
            }

            // ---------- 幂等：删掉旧面板与旧入口 ----------
            foreach (string n in new[] { kPanelName, kEntryName })
            {
                GameObject old = FindDeep(canvas.transform, n);
                while (old != null)
                {
                    Object.DestroyImmediate(old);
                    Debug.Log("[LevelPathSetup] 已删除旧的 \"" + n + "\"。");
                    old = FindDeep(canvas.transform, n);
                }
            }

            // 旧控制器（网格版）会和新面板抢同一件事，禁用掉而不是删（保留可回退）
            int disabled = 0;
            foreach (LevelSelectPanel legacy in Object.FindObjectsOfType<LevelSelectPanel>(true))
            {
                legacy.enabled = false;
                disabled++;
            }
            if (disabled > 0)
            {
                Debug.Log("[LevelPathSetup] 已禁用 " + disabled + " 个旧的 LevelSelectPanel。");
            }

            // ---------- 面板根 ----------
            GameObject root = NewUI(kPanelName, canvas.transform);
            Stretch(root);
            Image bg = root.AddComponent<Image>();
            bg.color = kPanelBg;
            bg.raycastTarget = true;

            NewLabel("标题", root.transform, "关卡选择", 64,
                new Vector2(-800f, 440f), new Vector2(520f, 90f), TMPro.TextAlignmentOptions.Left, kTextDark);

            // ---------- 路径层与容器 ----------
            // Track 是所有会一起左右滑动的东西的父物体。
            // 连接线必须是 Track 的子物体 —— 否则滑动时节点走了、线留在原地，
            // 看起来就像线被"钉"在屏幕上。
            GameObject track = NewUI("Track", root.transform);
            Stretch(track);

            GameObject lineLayer = NewUI("Path Lines", track.transform);
            Stretch(lineLayer);

            int count = 6;

            // 连线：相邻节点之间一段带弧度的细线，上下交替。
            // 放在 node 之前创建 → 渲染顺序在后 → 节点会盖住线的端点。
            for (int i = 0; i < count - 1; i++)
            {
                Vector2 a = NodeLocalPosition(i, count);
                Vector2 b = NodeLocalPosition(i + 1, count);
                Vector2 mid = (a + b) * 0.5f;

                GameObject seg = NewUI("Link " + i, lineLayer.transform);
                SetRect(seg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    mid, new Vector2(kSpacing + 20f, 90f));

                Image img = seg.AddComponent<Image>();
                // 偶数段向上拱、奇数段向下拱 → 视觉上是一条来回摆动的曲线
                img.sprite = (i % 2 == 0) ? connUp : connDown;
                img.color = kLineColor;
                img.raycastTarget = false;
            }

            // ---------- 六个关卡节点 ----------
            var nodes = new List<LevelPathPanel.Node>();
            var nodeButtons = new List<Button>();

            for (int i = 0; i < count; i++)
            {
                string sceneName = "Map" + i;
                string title = "第 " + (i + 1) + " 关";

                GameObject node = NewUI(sceneName, track.transform);
                SetRect(node, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    NodeLocalPosition(i, count), new Vector2(kNodeSize, kNodeSize));

                // 柔光底
                GameObject softGo = NewUI("Soft", node.transform);
                SetRect(softGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(kSoftSize, kSoftSize));
                Image softImg = softGo.AddComponent<Image>();
                softImg.sprite = soft;
                softImg.color = new Color(1f, 1f, 1f, 0.32f);
                softImg.raycastTarget = false;

                // 点击区
                Image hit = node.AddComponent<Image>();
                hit.color = new Color(1f, 1f, 1f, 0f);
                hit.raycastTarget = true;

                Button button = node.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.targetGraphic = hit;

                // 外圈
                GameObject discGo = NewUI("Disc", node.transform);
                SetRect(discGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(kNodeSize, kNodeSize));
                Image discImg = discGo.AddComponent<Image>();
                discImg.sprite = disc;
                discImg.color = kDiscIdle;
                discImg.raycastTarget = false;

                // 内圈实心圆（只有当前节点显示）
                GameObject innerGo = NewUI("Inner", node.transform);
                SetRect(innerGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(kInnerSize, kInnerSize));
                Image innerImg = innerGo.AddComponent<Image>();
                innerImg.sprite = soft;
                innerImg.color = kTextTeal;
                innerImg.raycastTarget = false;
                innerImg.enabled = false;   // 由控制器按当前节点开关

                // 关卡名（在圆下方）
                TMPro.TMP_Text label = NewLabel("Label", node.transform, title, 32,
                    new Vector2(0f, -160f), new Vector2(360f, 50f),
                    TMPro.TextAlignmentOptions.Center, kTextFaint);

                // 「当前」标记
                GameObject badgeGo = NewUI("Current Badge", node.transform);
                SetRect(badgeGo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, -218f), new Vector2(300f, 46f));
                var badge = badgeGo.AddComponent<TMPro.TextMeshProUGUI>();
                badge.text = "当前";
                badge.fontSize = 30;
                badge.color = kTextTeal;
                badge.alignment = TMPro.TextAlignmentOptions.Center;
                badge.raycastTarget = false;

                nodes.Add(new LevelPathPanel.Node
                {
                    root = node,
                    disc = discImg,
                    inner = innerImg,
                    label = label,
                    currentBadge = badgeGo,
                    button = button,
                    sceneName = sceneName
                });
                nodeButtons.Add(button);
            }

            // ---------- 左右箭头 ----------
            Button prev = NewRoundButton("左箭头", root.transform, new Vector2(-860f, -20f),
                new Vector2(110f, 110f), "‹", 76, soft, kArrowBg);
            Button next = NewRoundButton("右箭头", root.transform, new Vector2(860f, -20f),
                new Vector2(110f, 110f), "›", 76, soft, kArrowBg);

            // ---------- 开始 / 关闭 ----------
            Button start = NewRoundButton("开始", root.transform, new Vector2(0f, -420f),
                new Vector2(340f, 100f), "开始", 42, round, kStartBg);

            Button close = NewRoundButton("关闭", root.transform, new Vector2(800f, 440f),
                new Vector2(180f, 80f), "关闭", 36, round, kArrowBg);

            // ---------- 挂控制器 ----------
            LevelPathPanel panel = root.AddComponent<LevelPathPanel>();
            panel.Configure(new LevelPathPanel.Config
            {
                root = root,
                nodes = nodes.ToArray(),
                track = track.GetComponent<RectTransform>(),
                lineLayer = lineLayer.GetComponent<RectTransform>(),
                prevButton = prev,
                nextButton = next,
                startButton = start,
                closeButton = close,
                menuButtons = menuButtons.ToArray(),
                defaultIndex = 0
            });

            // 把构建时用的形状参数写进组件，编辑器摆位 == 运行时位置
            SerializedObject so = new SerializedObject(panel);
            SetFloat(so, "m_Spacing", kSpacing);
            SetFloat(so, "m_Amplitude", kAmplitude);
            SetFloat(so, "m_WaveLength", kWaveLength);
            SetFloat(so, "m_VerticalOffset", kVerticalOffset);
            so.ApplyModifiedPropertiesWithoutUndo();

            // ---------- 入口按钮 ----------
            GameObject entry = NewUI(kEntryName, canvas.transform);
            SetRect(entry, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(694f, -226f), new Vector2(340f, 100f));

            GameObject entryBgGo = NewUI("Background", entry.transform);
            Stretch(entryBgGo);
            Image entryBg = entryBgGo.AddComponent<Image>();
            entryBg.sprite = round;
            entryBg.type = Image.Type.Sliced;
            entryBg.color = new Color(0.45f, 0.83f, 0.87f, 0f);
            entryBg.raycastTarget = false;

            Image entryHit = entry.AddComponent<Image>();
            entryHit.color = new Color(1f, 1f, 1f, 0f);
            entryHit.raycastTarget = true;

            Button entryButton = entry.AddComponent<Button>();
            entryButton.transition = Selectable.Transition.None;
            entryButton.targetGraphic = entryHit;

            GameObject entryTextGo = NewUI("文字", entry.transform);
            Stretch(entryTextGo);
            var entryText = entryTextGo.AddComponent<TMPro.TextMeshProUGUI>();
            entryText.text = "关卡选择";
            entryText.fontSize = 40;
            entryText.color = kTextDark;
            entryText.alignment = TMPro.TextAlignmentOptions.Center;
            entryText.raycastTarget = false;

            MenuButton entryMb = entry.AddComponent<MenuButton>();
            SerializedObject entrySo = new SerializedObject(entryMb);
            SetObject(entrySo, "m_Background", entryBg);
            SetObject(entrySo, "m_Text", entryTextGo.GetComponent<RectTransform>());
            SetFloat(entrySo, "m_ActiveAlpha", 0.9f);
            SetFloat(entrySo, "m_TextScale", 1.12f);
            entrySo.ApplyModifiedPropertiesWithoutUndo();

            Wire(entryButton, panel, "Open");

            // 面板常态隐藏
            root.SetActive(false);

            // 确保六个 Map 场景在 Build Settings 里
            int added = EnsureScenesInBuildSettings();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveOpenScenes();

            string summary = string.Format(
                "关卡路径布局已建好。\n\n" +
                "节点：{0} 个，间距 {1}，起伏 {2}\n" +
                "新加入 Build Settings 的场景：{3} 个\n" +
                "旧 LevelSelectPanel 已禁用：{4} 个\n\n" +
                "交互：左右滑动 / 拖动 / 箭头 / A·D / ←·→ / 滚轮 切换；\n" +
                "Enter·空格 / 点当前节点 / 点「开始」进入；Esc 关闭。",
                count, kSpacing, kAmplitude, added, disabled);

            Debug.Log("[LevelPathSetup] " + summary.Replace("\n", " "));
            Report(auto, summary);
        }

        // ==================== 位置 ====================

        /// <summary>第 index 个节点在 Track 里的局部位置（编辑器为此算的，和运行时公式一致）。</summary>
        private static Vector2 NodeLocalPosition(int index, int count)
        {
            float x = (index - (count - 1) * 0.5f) * kSpacing;
            float phase = (index - (count - 1) * 0.5f) / Mathf.Max(0.01f, kWaveLength);
            float y = Mathf.Sin(phase * Mathf.PI) * kAmplitude + kVerticalOffset;
            return new Vector2(x, y);
        }

        // ==================== Build Settings ====================

        private static int EnsureScenesInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int added = 0;

            for (int i = 0; i < 6; i++)
            {
                string path = kSceneFolder + "Map" + i + ".unity";

                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    Debug.LogWarning("[LevelPathSetup] 找不到场景文件：" + path);
                    continue;
                }

                bool present = false;
                for (int s = 0; s < scenes.Count; s++)
                {
                    if (scenes[s].path == path) { present = true; break; }
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
                Debug.Log("[LevelPathSetup] 已把 " + added + " 个 Map 场景加入 Build Settings。");
            }

            return added;
        }

        // ==================== helpers ====================

        private static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
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

        private static TMPro.TMP_Text NewLabel(string name, Transform parent, string text, float size,
            Vector2 pos, Vector2 sizeDelta, TMPro.TextAlignmentOptions align, Color color)
        {
            GameObject go = NewUI(name, parent);
            SetRect(go, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, sizeDelta);

            var tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>圆形按钮：底图 + 居中文字。</summary>
        private static Button NewRoundButton(string name, Transform parent, Vector2 pos, Vector2 size,
            string label, float fontSize, Sprite sprite, Color bgColor)
        {
            GameObject go = NewUI(name, parent);
            SetRect(go, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);

            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = bgColor;
            img.raycastTarget = true;

            Button b = go.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = img;

            NewLabel("文字", go.transform, label, fontSize,
                Vector2.zero, Vector2.zero, TMPro.TextAlignmentOptions.Center, kTextDark);

            // 文字铺满按钮，方便居中
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

        private static void Wire(Button button, LevelPathPanel panel, string method)
        {
            if (button == null || panel == null) { return; }

            SerializedObject so = new SerializedObject(button);
            SerializedProperty calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (calls == null)
            {
                Debug.LogWarning("[LevelPathSetup] 找不到 m_OnClick，回调未绑定：" + method);
                return;
            }

            int index = calls.arraySize;
            calls.InsertArrayElementAtIndex(index);

            SerializedProperty call = calls.GetArrayElementAtIndex(index);
            call.FindPropertyRelative("m_Target").objectReferenceValue = panel;
            call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = "PAO.UI.LevelPathPanel, Assembly-CSharp";
            call.FindPropertyRelative("m_MethodName").stringValue = method;
            call.FindPropertyRelative("m_Mode").intValue = 1;
            call.FindPropertyRelative("m_CallState").intValue = 2;

            so.ApplyModifiedPropertiesWithoutUndo();
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
            if (root.name == name) { return root.gameObject; }

            for (int i = 0; i < root.childCount; i++)
            {
                GameObject hit = FindDeep(root.GetChild(i), name);
                if (hit != null) { return hit; }
            }

            return null;
        }

        private static void Report(bool auto, string message)
        {
            if (auto)
            {
                Debug.Log("[LevelPathSetup] " + message.Replace("\n", " "));
            }
            else
            {
                EditorUtility.DisplayDialog("关卡路径布局", message, "好");
            }
        }

        // ==================== 自动安装（常驻重试，失败会再试）====================

        [InitializeOnLoadMethod]
        private static void HookAutoBuild()
        {
            if (EditorPrefs.GetBool(kAutoBuildKey, false))
            {
                return;
            }

            EditorApplication.update += AutoBuildTick;
        }

        private static void AutoBuildTick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                return;
            }

            if (SceneManager.GetActiveScene().name != kTargetScene)
            {
                return;
            }

            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                return;
            }

            GameObject existing = FindDeep(canvas.transform, kPanelName);
            bool alreadyPathLayout = existing != null && existing.GetComponent<LevelPathPanel>() != null;

            if (alreadyPathLayout)
            {
                EditorApplication.update -= AutoBuildTick;
                EditorPrefs.SetBool(kAutoBuildKey, true);
                return;
            }

            try
            {
                Build(true);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[LevelPathSetup] 自动构建失败：" + e.Message);
            }
        }
    }
}

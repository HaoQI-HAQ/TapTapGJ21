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
    /// 一键搭建 Control UI 场景的 HUD + 暂停开关。
    ///
    /// 做四件事：
    ///   1. 把 Pause Panel 烘成未激活 —— 平时隐藏，按 Esc 才出现
    ///   2. 场景里确保有一个 BubbleTypeSwitcher（三种泡泡类型的切换器）
    ///   3. 右下角建三个类型圆：当前选中的那种亮起，其它暗下去
    ///   4. 挂 BubbleTypeHud，负责亮圈刷新 + Esc 开关暂停菜单
    ///
    /// 用法：菜单 **PAO → UI → 构建 HUD**
    /// 幂等：重复执行先删旧的 HUD 再重建（Pause Panel 只改激活状态，不动布局）。
    /// </summary>
    public static class LevelHudBuilder
    {
        private const string kHudName = "HUD";
        private const string kPausePanelName = "Pause Panel";
        private const string kSwitcherName = "Bubble Type Switcher";
        private const string kSpriteFolder = "Assets/GameMain/UI/Sprites/";
        private const string kAutoBuildKey = "PAO.LevelHud.AutoBuilt.v1";

        private static readonly string[] kIconNames = { "icon_sticky", "icon_bouncy", "icon_bomb" };

        // 三个圆的三角排布（相对 HUD 容器中心），第一个是主位
        private static readonly Vector2[] kCirclePos =
        {
            new Vector2(0f, 150f),
            new Vector2(-150f, -70f),
            new Vector2(150f, -70f)
        };

        private const float kCircleSize = 150f;

        [MenuItem("PAO/UI/构建 HUD", false, 23)]
        public static void BuildFromMenu()
        {
            Build(false);
        }

        public static void Build(bool auto)
        {
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Report(auto, "场景里找不到 Canvas。请打开 Control UI 场景再执行。");
                return;
            }

            Sprite ring = AssetDatabase.LoadAssetAtPath<Sprite>(kSpriteFolder + "bubble_ring.png");
            if (ring == null)
            {
                Report(auto, "缺少 Sprite：Assets/GameMain/UI/Sprites/bubble_ring.png");
                return;
            }

            // ---------- 1. 暂停面板：常态隐藏 ----------
            GameObject pausePanel = FindDeep(canvas.transform, kPausePanelName);
            if (pausePanel == null)
            {
                Report(auto, "场景里找不到 \"" + kPausePanelName + "\"。请先构建暂停界面。");
                return;
            }

            PauseMenu pauseMenu = pausePanel.GetComponent<PauseMenu>();

            // ---------- 2. 确保有 BubbleTypeSwitcher ----------
            BubbleTypeSwitcher switcher = Object.FindObjectOfType<BubbleTypeSwitcher>(true);
            if (switcher == null)
            {
                var go = new GameObject(kSwitcherName);
                go.transform.SetParent(canvas.transform.parent, false);   // 和 Canvas 平级，不属于 UI
                switcher = go.AddComponent<BubbleTypeSwitcher>();
                Debug.Log("[LevelHudBuilder] 场景里没有 BubbleTypeSwitcher，已自动创建一个。");
            }

            // ---------- 3. 幂等清理 + 建 HUD ----------
            Transform old = canvas.transform.Find(kHudName);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
                Debug.Log("[LevelHudBuilder] 已删除旧的 \"" + kHudName + "\" 并重建。");
            }

            GameObject hud = NewUI(kHudName, canvas.transform);
            SetRect(hud, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-230f, 230f), new Vector2(540f, 540f));

            var slots = new List<BubbleTypeHud.TypeSlot>();

            for (int i = 0; i < BubbleTypeSwitcher.TypeCount; i++)
            {
                BubbleType type = (BubbleType)i;
                string label = BubbleTypeSwitcher.DisplayNameOf(type);

                // 圆的根：负责缩放，同时是点击区
                GameObject circle = NewUI(label, hud.transform);
                SetRect(circle, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    kCirclePos[i], new Vector2(kCircleSize, kCircleSize));

                Image hit = circle.AddComponent<Image>();
                hit.color = new Color(1f, 1f, 1f, 0f);
                hit.raycastTarget = true;

                Button button = circle.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.targetGraphic = hit;

                // 彩色的圆环（亮/暗由 BubbleTypeHud 控制）
                GameObject ringGo = NewUI("Ring", circle.transform);
                SetRect(ringGo, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                Image ringImage = ringGo.AddComponent<Image>();
                ringImage.sprite = ring;
                ringImage.type = Image.Type.Simple;
                ringImage.color = BubbleTypeSwitcher.ColorOf(type);
                ringImage.raycastTarget = false;

                // 圆里的图标
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

                // 类型名，放在圆下方
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

            // 圆的按钮导航：左右环绕
            for (int i = 0; i < slots.Count; i++)
            {
                Navigation nav = slots[i].button.navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnRight = slots[(i + 1) % slots.Count].button;
                nav.selectOnLeft = slots[(i - 1 + slots.Count) % slots.Count].button;
                slots[i].button.navigation = nav;
            }

            // ---------- 4. 挂 HUD 控制器 ----------
            BubbleTypeHud hudComponent = hud.AddComponent<BubbleTypeHud>();
            hudComponent.Configure(slots.ToArray(), pauseMenu);

            // 暂停面板：常态隐藏 + 不要一进场景就暂停时间
            if (pauseMenu != null)
            {
                SerializedObject pauseSo = new SerializedObject(pauseMenu);
                SetBool(pauseSo, "m_PauseTimeOnEnable", false);
                SetBool(pauseSo, "m_PauseTimeOnOpen", true);
                pauseSo.ApplyModifiedPropertiesWithoutUndo();
            }

            pausePanel.SetActive(false);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            string summary = string.Format(
                "HUD 已建好并保存。\n\n" +
                "类型圆：{0} 个（{1}）\n" +
                "当前选中的圆会亮起，其余暗下去\n" +
                "点击圆 / 按 1 2 3 / 滚轮 都能切换\n" +
                "Pause Panel：已设为常态隐藏，按 Esc 开关",
                slots.Count,
                string.Join(" / ", System.Array.ConvertAll(kIconNames, n => n.Replace("icon_", ""))));

            Debug.Log("[LevelHudBuilder] " + summary.Replace("\n", " "));
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
                        Debug.Log("[LevelHudBuilder] 运行中，已跳过自动构建。");
                        return;
                    }

                    Scene active = SceneManager.GetActiveScene();
                    if (active.name != "Control UI")
                    {
                        Debug.Log("[LevelHudBuilder] 当前是 \"" + active.name +
                                  "\"，不是 Control UI，已跳过。打开该场景后用菜单 PAO → UI → 构建 HUD。");
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
                    Debug.LogError("[LevelHudBuilder] 自动构建失败：" + e.Message +
                                   "\n请用菜单 PAO → UI → 构建 HUD 手动执行。");
                }
            };
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

        private static void SetBool(SerializedObject so, string name, bool value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null)
            {
                p.boolValue = value;
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
                Debug.Log("[LevelHudBuilder] " + message.Replace("\n", " "));
            }
            else
            {
                EditorUtility.DisplayDialog("构建 HUD", message, "好");
            }
        }
    }
}

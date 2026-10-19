using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    /// <summary>
    /// 把「吊着的泡泡球 + 柱子」做成泡泡摆锤（BubbleHammer）。
    ///
    /// 【为什么需要工具】
    /// 一架摆锤由三件事凑成：支点在柱顶、柱子和泡泡球挂到支点下面且世界坐标不变、
    /// 泡泡球挂上 TerrainBubble 并按泡泡墙的参数配好。
    /// 手搭很容易漏掉"挂 TerrainBubble"这一步 —— 漏了它泡泡球就只是个装饰，
    /// 弹力泡泡打不进去、炸弹也炸不掉。
    ///
    /// 【配对规则】
    /// 选中里凡是"圆球"（Sphere 网格 / SphereCollider / 已有 TerrainBubble）当锤头，
    /// 其余当柱子；每颗球配**水平距离最近**的那根柱子（没选柱子就自动在场景里找）。
    ///
    /// 【生成的结构】
    ///   Hammer_xxx               ← 支点（柱顶），挂 BubbleHammer
    ///   └─ Hammer_xxx_Body
    ///      ├─ 柱子
    ///      └─ 泡泡球（TerrainBubble）
    ///
    /// 菜单：
    ///   Tools/PAO/关卡机关/把选中的泡泡+柱子做成泡泡摆锤
    ///   Tools/PAO/关卡机关/取消泡泡摆锤（还原）
    /// </summary>
    public static class BubbleHammerSetup
    {
        private const string kMenu = "Tools/PAO/关卡机关/";

        private const float kDefaultAmplitude = 30f;
        private const float kDefaultPeriod = 3.2f;
        private const float kDefaultHitSpeed = 16f;
        private const float kPairRadius = 3f;

        /// <summary>泡泡墙容积：几颗弹力泡泡能把它填满。</summary>
        private const int kCapacity = 3;

        /// <summary>弹力泡泡打破后多久长回来。</summary>
        private const float kRespawnDelay = 8f;

        private const string kRigPrefix = "Hammer_";
        private const string kBodySuffix = "_Body";

        [MenuItem(kMenu + "把选中的泡泡+柱子做成泡泡摆锤", priority = 55)]
        public static void BuildFromSelection()
        {
            List<Transform> selected = new List<Transform>();

            GameObject[] selection = Selection.gameObjects;

            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] != null)
                {
                    selected.Add(selection[i].transform);
                }
            }

            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("泡泡摆锤",
                    "先选中要做的物体：泡泡球（锤头）+ 吊着它的柱子，可以多选几组。", "好");
                return;
            }

            List<Transform> bubbles = new List<Transform>();
            List<Transform> pillars = new List<Transform>();

            for (int i = 0; i < selected.Count; i++)
            {
                if (IsBubbleHead(selected[i]))
                {
                    bubbles.Add(selected[i]);
                }
                else
                {
                    pillars.Add(selected[i]);
                }
            }

            if (bubbles.Count == 0)
            {
                EditorUtility.DisplayDialog("泡泡摆锤",
                    "选中的物体里没有找到「球」（Sphere 网格 / SphereCollider）。\n" +
                    "锤头得是圆球，柱子和球一起选。", "好");
                return;
            }

            List<Transform> created = new List<Transform>();
            StringBuilder log = new StringBuilder();

            for (int i = 0; i < bubbles.Count; i++)
            {
                Transform pillar = FindPillar(bubbles[i], pillars);

                if (pillar == null)
                {
                    log.Append("  · 跳过 ").Append(bubbles[i].name).Append("：附近没找到柱子\n");
                    continue;
                }

                float phase = (i % 2 == 0) ? 0f : 180f;

                Transform rig = Build(bubbles[i], pillar,
                    kRigPrefix + bubbles[i].name, phase, out string message);

                if (rig != null)
                {
                    created.Add(rig);
                    log.Append("  · ").Append(rig.name).Append("：柱 ").Append(pillar.name)
                       .Append("，相位 ").Append(phase).Append("°\n");
                }
                else
                {
                    log.Append("  · 失败 ").Append(bubbles[i].name).Append("：").Append(message).Append('\n');
                }
            }

            if (created.Count > 0)
            {
                Object[] objects = new Object[created.Count];

                for (int i = 0; i < created.Count; i++)
                {
                    objects[i] = created[i].gameObject;
                }

                Selection.objects = objects;
                SceneView.RepaintAll();
            }

            Debug.Log("[BubbleHammer] 建成 " + created.Count + " / " + bubbles.Count + " 架泡泡摆锤：\n" + log);
        }

        [MenuItem(kMenu + "重命名泡泡摆锤（按位置编号 01、02…）", priority = 57)]
        public static void RenameHammers()
        {
            BubbleHammer[] hammers = Object.FindObjectsByType<BubbleHammer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            System.Array.Sort(hammers, delegate (BubbleHammer a, BubbleHammer b)
            {
                return a.transform.position.z.CompareTo(b.transform.position.z);
            });

            StringBuilder log = new StringBuilder();

            for (int i = 0; i < hammers.Length; i++)
            {
                string newName = "Hammer_Bubble_" + (i + 1).ToString("00");

                // Body 名字跟着改，免得看起来对不上
                Transform body = hammers[i].transform.childCount > 0 ? hammers[i].transform.GetChild(0) : null;

                if (body != null)
                {
                    body.name = newName + kBodySuffix;
                }

                log.Append("  · ").Append(hammers[i].name).Append(" → ").Append(newName).Append('\n');
                hammers[i].name = newName;
            }

            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[BubbleHammer] 重命名 " + hammers.Length + " 架：\n" + log);
        }

        [MenuItem(kMenu + "取消泡泡摆锤（还原）", priority = 56)]
        public static void UnwrapSelection()
        {
            List<BubbleHammer> hammers = new List<BubbleHammer>();
            GameObject[] selection = Selection.gameObjects;

            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] == null)
                {
                    continue;
                }

                BubbleHammer hammer = selection[i].GetComponent<BubbleHammer>();

                if (hammer == null)
                {
                    hammer = selection[i].GetComponentInChildren<BubbleHammer>();
                }

                if (hammer != null && !hammers.Contains(hammer))
                {
                    hammers.Add(hammer);
                }
            }

            if (hammers.Count == 0)
            {
                Debug.LogWarning("[BubbleHammer] 选中的物体里没有可还原的泡泡摆锤。");
                return;
            }

            for (int i = 0; i < hammers.Count; i++)
            {
                Unwrap(hammers[i].transform);
            }

            Debug.Log("[BubbleHammer] 已还原 " + hammers.Count + " 架泡泡摆锤。");
        }

        // ==================== 核心 ====================

        /// <summary>把一颗泡泡球 + 一根柱子做成摆锤。</summary>
        public static Transform Build(Transform bubble, Transform pillar, string rigName,
                                      float phase, out string message)
        {
            message = string.Empty;

            if (bubble == null || pillar == null)
            {
                message = "参数为空";
                return null;
            }

            if (bubble.IsChildOf(pillar) || pillar.IsChildOf(bubble))
            {
                message = "球和柱子的父子关系不对";
                return null;
            }

            Bounds pillarBounds;

            if (!TryGetBounds(pillar, out pillarBounds))
            {
                message = "柱子没有 Renderer/Collider";
                return null;
            }

            Bounds bubbleBounds;
            TryGetBounds(bubble, out bubbleBounds);

            // 支点：柱顶正中
            Vector3 pivot = new Vector3(pillarBounds.center.x, pillarBounds.max.y, pillarBounds.center.z);

            if (bubbleBounds.center.y > pivot.y - 0.1f)
            {
                message = "球比柱顶还高，配不上";
                return null;
            }

            Scene scene = bubble.gameObject.scene;

            GameObject rig = new GameObject(rigName);
            Undo.RegisterCreatedObjectUndo(rig, "做成泡泡摆锤");
            rig.transform.position = pivot;
            rig.transform.rotation = Quaternion.identity;

            if (scene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(rig, scene);
            }

            GameObject body = new GameObject(rigName + kBodySuffix);
            Undo.RegisterCreatedObjectUndo(body, "做成泡泡摆锤");
            body.transform.SetParent(rig.transform, false);

            Undo.SetTransformParent(pillar, body.transform, "做成泡泡摆锤");
            Undo.SetTransformParent(bubble, body.transform, "做成泡泡摆锤");

            // 锤头 = 泡泡墙
            TerrainBubble terrain = bubble.GetComponent<TerrainBubble>();

            if (terrain == null)
            {
                terrain = Undo.AddComponent<TerrainBubble>(bubble.gameObject);
            }

            ConfigureTerrainBubble(terrain, bubble);

            // 摆动
            BubbleHammer hammer = Undo.AddComponent<BubbleHammer>(rig);
            hammer.Configure(bubble, Vector3.forward, kDefaultAmplitude, kDefaultPeriod,
                             phase, kDefaultHitSpeed);

            EditorSceneManager.MarkSceneDirty(scene);

            message = "支点 " + pivot.ToString("F2") + "，摆角 " + kDefaultAmplitude +
                      "°，周期 " + kDefaultPeriod + "s，锤头半径 " + hammer.HeadRadius.ToString("F2") + "m";

            return rig.transform;
        }

        public static bool Unwrap(Transform rig)
        {
            if (rig == null || rig.GetComponent<BubbleHammer>() == null)
            {
                return false;
            }

            Transform parent = rig.parent;
            List<Transform> children = new List<Transform>();

            for (int i = 0; i < rig.childCount; i++)
            {
                Transform child = rig.GetChild(i);

                if (child.name.EndsWith(kBodySuffix))
                {
                    for (int c = 0; c < child.childCount; c++)
                    {
                        children.Add(child.GetChild(c));
                    }
                }
            }

            for (int i = children.Count - 1; i >= 0; i--)
            {
                Undo.SetTransformParent(children[i], parent, "还原泡泡摆锤");
            }

            Undo.DestroyObjectImmediate(rig.gameObject);
            return true;
        }

        /// <summary>把泡泡球的 TerrainBubble 配成"泡泡墙"。</summary>
        private static void ConfigureTerrainBubble(TerrainBubble terrain, Transform bubble)
        {
            SerializedObject so = new SerializedObject(terrain);
            SetInt(so, "m_Capacity", kCapacity);
            SetFloat(so, "m_RespawnDelay", kRespawnDelay);
            SetInt(so, "m_RewardAmount", 5);
            SetFloat(so, "m_PopDelay", 0f);

            // 摆锤不参与"泡泡潮抽水"，否则四架摆锤会把潮水拖慢
            SetFloat(so, "m_TideSlowdownPerFill", 0f);

            // 染色：空的时候保持美术原来的颜色，快满时偏橙 —— 这样玩家能看出"快炸了"
            Color baseColor = ReadBaseColor(bubble);

            SetBool(so, "m_TintByFill", true);
            SetColor(so, "m_EmptyColor", baseColor);
            SetColor(so, "m_FullColor", new Color(1f, 0.45f, 0.35f, 1f));

            so.ApplyModifiedProperties();
        }

        private static void SetInt(SerializedObject so, string name, int value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.intValue = value;
        }

        private static void SetFloat(SerializedObject so, string name, float value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.floatValue = value;
        }

        private static void SetBool(SerializedObject so, string name, bool value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.boolValue = value;
        }

        private static void SetColor(SerializedObject so, string name, Color value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.colorValue = value;
        }

        private static Color ReadBaseColor(Transform bubble)
        {
            Renderer renderer = bubble.GetComponentInChildren<Renderer>();

            if (renderer != null && renderer.sharedMaterial != null)
            {
                Material mat = renderer.sharedMaterial;
                int baseColor = Shader.PropertyToID("_BaseColor");

                if (mat.HasProperty(baseColor))
                {
                    return mat.GetColor(baseColor);
                }

                if (mat.HasProperty("_Color"))
                {
                    return mat.GetColor("_Color");
                }
            }

            return new Color(0.25f, 0.8f, 0.85f, 1f);
        }

        // ==================== 辅助 ====================

        /// <summary>这个物体能不能当锤头（圆球）。</summary>
        public static bool IsBubbleHead(Transform t)
        {
            if (t == null)
            {
                return false;
            }

            if (t.GetComponent<TerrainBubble>() != null)
            {
                return true;
            }

            if (t.GetComponent<SphereCollider>() != null)
            {
                return true;
            }

            MeshFilter mf = t.GetComponent<MeshFilter>();

            return mf != null && mf.sharedMesh != null &&
                   mf.sharedMesh.name.IndexOf("Sphere", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>给泡泡球找它上面那根柱子。</summary>
        private static Transform FindPillar(Transform bubble, List<Transform> selectedPillars)
        {
            Vector3 bubblePos = bubble.position;
            Transform best = null;
            float bestDistance = float.MaxValue;

            // ① 优先从"一起选中的"柱子里挑
            for (int i = 0; i < selectedPillars.Count; i++)
            {
                Transform pillar = selectedPillars[i];

                if (pillar == null || pillar.IsChildOf(bubble) || bubble.IsChildOf(pillar))
                {
                    continue;
                }

                float distance = HorizontalDistance(bubblePos, pillar.position);

                if (distance > kPairRadius || distance >= bestDistance)
                {
                    continue;
                }

                Bounds b;

                if (!TryGetBounds(pillar, out b) || b.max.y < bubblePos.y)
                {
                    continue;   // 柱子得在球上面
                }

                best = pillar;
                bestDistance = distance;
            }

            if (best != null)
            {
                return best;
            }

            // ② 没选柱子：在场景里找最近的木柱
            Transform[] all = Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            for (int i = 0; i < all.Length; i++)
            {
                Transform candidate = all[i];

                if (candidate == null || candidate.name.IndexOf("Wooden_Pillar",
                        System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                float distance = HorizontalDistance(bubblePos, candidate.position);

                if (distance > kPairRadius || distance >= bestDistance)
                {
                    continue;
                }

                Bounds b;

                if (!TryGetBounds(candidate, out b) || b.max.y < bubblePos.y)
                {
                    continue;
                }

                best = candidate;
                bestDistance = distance;
            }

            return best;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private static bool TryGetBounds(Transform root, out Bounds bounds)
        {
            bounds = default(Bounds);
            bool has = false;

            if (root == null)
            {
                return false;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                if (!has)
                {
                    bounds = renderers[i].bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            if (has)
            {
                return true;
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null)
                {
                    continue;
                }

                if (!has)
                {
                    bounds = colliders[i].bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(colliders[i].bounds);
                }
            }

            return has;
        }
    }
}

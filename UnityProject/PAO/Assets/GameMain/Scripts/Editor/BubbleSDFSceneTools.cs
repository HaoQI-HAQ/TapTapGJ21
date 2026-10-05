using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PAO.BubbleFX.EditorTools
{
    /// <summary>
    /// 一键在场景里生成测试泡泡群，用来快速看 SDF/薄膜干涉的效果，
    /// 不用等发射器接好。
    ///
    /// 用法：Hierarchy 里右键 -> PAO/泡泡 SDF/生成测试泡泡群。
    ///
    /// 生成的是纯 SDFBubble 空物体（没有碰撞体、没有刚体），
    /// 只负责好看，方便单独调材质参数。想清掉就用下面那个菜单。
    /// </summary>
    public static class BubbleSDFSceneTools
    {
        private const string k_RootName = "SDF Bubble Test Cluster";

        [MenuItem("GameObject/PAO/泡泡 SDF/生成测试泡泡群", false, 10)]
        public static void SpawnTestCluster(MenuCommand command)
        {
            // 已经有就直接复用，避免堆一堆重叠的
            GameObject root = GameObject.Find(k_RootName);
            if (root == null)
            {
                root = new GameObject(k_RootName);
                Undo.RegisterCreatedObjectUndo(root, "Create Bubble Test Cluster");
            }

            // 放在当前 Scene 视图中心，这样不用找
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
                root.transform.position = view.pivot;

            Vector3 origin = root.transform.position;

            AddBubble(root.transform, origin + new Vector3(0f, 0f, 0f),   0.55f, new Color(0.70f, 0.90f, 1.00f, 1f));
            AddBubble(root.transform, origin + new Vector3(0.85f, 0.10f, 0.10f), 0.42f, new Color(0.85f, 0.75f, 1.00f, 1f));
            AddBubble(root.transform, origin + new Vector3(-0.70f, 0.22f, 0.18f), 0.48f, new Color(0.70f, 1.00f, 0.88f, 1f));
            AddBubble(root.transform, origin + new Vector3(0.20f, 0.92f, -0.12f), 0.36f, new Color(1.00f, 0.85f, 0.72f, 1f));
            AddBubble(root.transform, origin + new Vector3(-0.35f, -0.75f, 0.14f), 0.33f, new Color(0.80f, 0.92f, 1.00f, 1f));
            AddBubble(root.transform, origin + new Vector3(1.35f, 0.75f, -0.20f), 0.28f, new Color(1.00f, 0.78f, 0.95f, 1f));

            // 这颗明显小、壳更薄，用来对照壳厚的效果
            AddBubble(root.transform, origin + new Vector3(-1.30f, -0.35f, 0.25f), 0.22f,
                      new Color(0.85f, 0.95f, 1.00f, 1f));

            EditorSceneManager.MarkAllScenesDirty();

            Debug.Log("[BubbleSDF] 已生成测试泡泡群（" + root.transform.childCount + " 颗）。\n" +
                      "如果什么都看不到，先确认：\n" +
                      "  1. 菜单 Tools/PAO/泡泡 SDF/安装到 URP Renderer 已经执行过\n" +
                      "  2. Scene 视图右上角开了 Effect（Animated Materials）\n" +
                      "  3. Game 视图里相机确实朝着这堆泡泡");
        }

        [MenuItem("GameObject/PAO/泡泡 SDF/清除测试泡泡群", false, 11)]
        public static void ClearTestCluster(MenuCommand command)
        {
            GameObject root = GameObject.Find(k_RootName);
            if (root == null)
            {
                Debug.Log("[BubbleSDF] 场景里没有测试泡泡群。");
                return;
            }

            Undo.DestroyObjectImmediate(root);
            EditorSceneManager.MarkAllScenesDirty();
        }

        // ------------------------------------------------------------------

        private static void AddBubble(Transform parent, Vector3 worldPosition, float radius, Color color)
        {
            var go = new GameObject("Bubble (SDF)");
            Undo.RegisterCreatedObjectUndo(go, "Create Bubble");

            go.transform.SetParent(parent, true);
            go.transform.position = worldPosition;

            var bubble = go.AddComponent<SDFBubble>();

            // SDFBubble 的半径字段是私有的，这里走公开属性设
            bubble.Radius = radius;
            bubble.Color = color;

            bubble.Refresh();
        }
    }
}

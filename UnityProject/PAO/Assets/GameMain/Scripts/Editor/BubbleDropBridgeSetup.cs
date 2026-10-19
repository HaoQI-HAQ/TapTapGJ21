using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    /// <summary>
    /// 把「泡泡墙 + 挂在墙里的砖块/铁网格」做成 <see cref="BubbleDropBridge"/>：
    /// 打破泡泡墙 → 砖块和铁网格从上面的柱子断开、绕底边倒下去 → 和前方石头接成桥。
    ///
    /// 【为什么需要工具】
    /// 要点有两个，手搭容易漏：
    ///   1. 掉落件必须是"独立的一组"（挂到桥的壳下面），否则还跟着原来的父级，倒不动；
    ///   2. 铰链支点要落在掉落件的**底边**（包围盒 min.y 的中心），不然整块东西是原地打转。
    ///
    /// 菜单：
    ///   Tools/PAO/关卡机关/把选中的泡泡墙做成掉落桥
    ///   Tools/PAO/关卡机关/取消掉落桥（还原）
    /// </summary>
    public static class BubbleDropBridgeSetup
    {
        private const string kMenu = "Tools/PAO/关卡机关/";

        private const float kDefaultFallAngle = 88f;
        private const float kDefaultFallDuration = 1.4f;

        private const string kRigPrefix = "Bridge_";
        private const string kBodySuffix = "_Body";

        [MenuItem(kMenu + "把选中的泡泡墙做成掉落桥", priority = 50)]
        public static void BuildFromSelection()
        {
            GameObject[] selection = Selection.gameObjects;

            if (selection.Length < 2)
            {
                EditorUtility.DisplayDialog("泡泡墙掉落桥",
                    "选中「泡泡墙」+「要掉下去的砖块/铁网格」（至少两个物体）。\n" +
                    "泡泡墙是那个带 TerrainBubble 的球。", "好");
                return;
            }

            List<Transform> all = new List<Transform>(selection.Length);

            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] != null)
                {
                    all.Add(selection[i].transform);
                }
            }

            Transform wall = null;
            List<Transform> falling = new List<Transform>(all.Count);

            // 先找出墙：优先选带 TerrainBubble 的
            for (int i = 0; i < all.Count; i++)
            {
                if (wall == null && all[i].GetComponentInChildren<TerrainBubble>(true) != null)
                {
                    wall = all[i];
                    continue;
                }
            }

            // 其余都是要掉下去的（去掉墙的子物体，避免重复）
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == wall)
                {
                    continue;
                }

                if (wall != null && all[i].IsChildOf(wall))
                {
                    continue;
                }

                falling.Add(all[i]);
            }

            if (wall == null)
            {
                EditorUtility.DisplayDialog("泡泡墙掉落桥",
                    "选中的物体里没有找到泡泡墙（带 TerrainBubble 的那个球）。\n" +
                    "把墙和要掉下去的砖块/铁网格一起选。", "好");
                return;
            }

            if (falling.Count == 0)
            {
                EditorUtility.DisplayDialog("泡泡墙掉落桥",
                    "除了墙以外没有别的物体可以掉。把砖块和铁网格一起选上。", "好");
                return;
            }

            Transform rig = Build(wall, falling.ToArray(),
                kRigPrefix + wall.name, out string message);

            if (rig != null)
            {
                Selection.activeGameObject = rig.gameObject;
                SceneView.RepaintAll();
            }

            Debug.Log("[DropBridge] " + (rig != null ? "建成：" : "失败：") + message);
        }

        [MenuItem(kMenu + "取消掉落桥（还原）", priority = 51)]
        public static void UnwrapSelection()
        {
            int count = 0;
            GameObject[] selection = Selection.gameObjects;

            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] == null)
                {
                    continue;
                }

                BubbleDropBridge bridge = selection[i].GetComponent<BubbleDropBridge>();

                if (bridge == null)
                {
                    bridge = selection[i].GetComponentInChildren<BubbleDropBridge>();
                }

                if (bridge != null && Unwrap(bridge.transform))
                {
                    count++;
                }
            }

            Debug.Log("[DropBridge] 已还原 " + count + " 座掉落桥。");
        }

        // ==================== 核心 ====================

        public static Transform Build(Transform wall, Transform[] falling, string rigName, out string message)
        {
            message = string.Empty;

            if (wall == null || falling == null || falling.Length == 0)
            {
                message = "参数为空";
                return null;
            }

            // 掉落组的包围盒 → 决定壳的位置和铰链
            Bounds fallingBounds = default(Bounds);
            bool hasBounds = false;

            for (int i = 0; i < falling.Length; i++)
            {
                Bounds b;

                if (falling[i] == null || !TryGetBounds(falling[i], out b))
                {
                    continue;
                }

                if (!hasBounds) { fallingBounds = b; hasBounds = true; }
                else fallingBounds.Encapsulate(b);
            }

            if (!hasBounds)
            {
                message = "掉落件没有 Renderer/Collider，算不出包围盒";
                return null;
            }

            Vector3 anchor = new Vector3(fallingBounds.center.x, fallingBounds.min.y, fallingBounds.center.z);
            Scene scene = wall.gameObject.scene;

            GameObject rig = new GameObject(rigName);
            Undo.RegisterCreatedObjectUndo(rig, "做成掉落桥");
            rig.transform.position = anchor;
            rig.transform.rotation = Quaternion.identity;

            if (scene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(rig, scene);
            }

            // 【不改父子关系】
            // 掉落件现在挂在关卡自己的分组（比如 sm_l3_ji_guan）下面，
            // 那层如果是 prefab 实例，SetParent 出去会被 Unity 挡掉（弹回来），
            // 所以组件只记引用、每帧写位置/旋转，零件的爸爸是谁都不影响。
            BubbleDropBridge bridge = Undo.AddComponent<BubbleDropBridge>(rig);
            bridge.Configure(wall, falling, BubbleDropBridge.FallMode.Hinge,
                             Vector3.right, kDefaultFallAngle, kDefaultFallDuration);

            EditorSceneManager.MarkSceneDirty(scene);

            message = "墙=" + wall.name + "，掉落件 " + falling.Length + " 个，" +
                      "铰链底边 " + anchor.ToString("F2") + "，倒 " + kDefaultFallAngle +
                      "°，用时 " + kDefaultFallDuration + "s";

            return rig.transform;
        }

        public static bool Unwrap(Transform rig)
        {
            if (rig == null || rig.GetComponent<BubbleDropBridge>() == null)
            {
                return false;
            }

            // 零件本来就没换过爸爸，直接删掉逻辑载体就行
            Undo.DestroyObjectImmediate(rig.gameObject);
            return true;
        }

        private static bool TryGetBounds(Transform root, out Bounds bounds)
        {
            bounds = default(Bounds);
            bool has = false;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;

                if (!has) { bounds = renderers[i].bounds; has = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }

            if (has) return true;

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null) continue;

                if (!has) { bounds = colliders[i].bounds; has = true; }
                else bounds.Encapsulate(colliders[i].bounds);
            }

            return has;
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    /// <summary>
    /// Map4 关卡的可粘地形批量设置。
    ///
    /// 关卡规则：除了「地面」和「泡泡墙」，其余所有能撞到的东西都应该可以被
    /// 浮粘泡泡黏住。手动一个个挂 StickySurface 太费事，所以这里做成菜单工具：
    ///
    ///   Tools/PAO/关卡/可粘地形：给非地面物体加上 StickySurface
    ///   Tools/PAO/关卡/可粘地形：移除场景里的 StickySurface
    ///
    /// 判定「地面 / 泡泡墙」的依据（和关卡白盒的材质约定一致）：
    ///   · WB_Ground      —— 地面材质，跳过；
    ///   · WB_Destructible —— 粉色泡泡墙材质，跳过；
    ///   · WB_Void        —— 远景虚空背板，跳过；
    ///   · 名字带 Pavement 的碰撞体（美术地砖铺装代理）跳过；
    ///   · Terrain / TerrainCollider、TerrainBubble、所有碰撞体都是 Trigger 的
    ///     物体跳过。
    /// 其余带非 Trigger 碰撞体的物体统统挂上 StickySurface。
    /// 想改规则只改下面的两个数组即可。
    /// </summary>
    public static class Map4StickySurfaceTool
    {
        /// <summary>这些材质视为「地面 / 泡泡墙 / 虚空」，不挂可粘地形。</summary>
        private static readonly string[] s_SkipMaterialNames =
        {
            "WB_Ground",
            "WB_Destructible",
            "WB_Void",
        };

        /// <summary>名字里带这些关键字（小写）即视为地面，不挂可粘地形。美术地砖铺装走的就是这条。</summary>
        private static readonly string[] s_SkipNameKeywords =
        {
            "pavement",
        };

        [MenuItem("Tools/PAO/关卡/可粘地形：给非地面物体加上 StickySurface", priority = 62)]
        public static void ApplyToOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogWarning("[Map4 Sticky] 没有打开的场景。");
                return;
            }

            Undo.SetCurrentGroupName("Sticky Surfaces");
            int undoGroup = Undo.GetCurrentGroup();

            int added = 0;
            int skippedGround = 0;
            int skippedBubbleWall = 0;
            int alreadyHas = 0;

            List<GameObject> objects = CollectObjects(scene);
            for (int i = 0; i < objects.Count; i++)
            {
                GameObject go = objects[i];
                if (go == null)
                {
                    continue;
                }

                Collider[] colliders = go.GetComponents<Collider>();
                if (colliders.Length == 0 || !HasSolidCollider(colliders))
                {
                    continue;
                }

                // 父级上已经挂了（比如美术 prefab 实例的根节点整体挂了一个）就不用再挂
                if (go.GetComponentInParent<StickySurface>() != null)
                {
                    alreadyHas++;
                    continue;
                }

                string reason = GetSkipReason(go);
                if (reason == "ground")
                {
                    skippedGround++;
                    continue;
                }

                if (reason == "bubble-wall")
                {
                    skippedBubbleWall++;
                    continue;
                }

                if (reason != null)
                {
                    continue;
                }

                Undo.AddComponent<StickySurface>(go);
                added++;
            }

            Undo.CollapseUndoOperations(undoGroup);

            if (added > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            Debug.Log(string.Format(
                "[Map4 Sticky] 场景 {0}：新加 {1} 个 StickySurface；跳过地面 {2} 个、泡泡墙 {3} 个，已有 {4} 个。",
                scene.name, added, skippedGround, skippedBubbleWall, alreadyHas));
        }

        [MenuItem("Tools/PAO/关卡/可粘地形：移除场景里的 StickySurface", priority = 63)]
        public static void ClearFromOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogWarning("[Map4 Sticky] 没有打开的场景。");
                return;
            }

            Undo.SetCurrentGroupName("Clear Sticky Surfaces");
            int undoGroup = Undo.GetCurrentGroup();

            int removed = 0;
            List<GameObject> objects = CollectObjects(scene);
            for (int i = 0; i < objects.Count; i++)
            {
                GameObject go = objects[i];
                if (go == null)
                {
                    continue;
                }

                StickySurface[] components = go.GetComponents<StickySurface>();
                for (int c = 0; c < components.Length; c++)
                {
                    Undo.DestroyObjectImmediate(components[c]);
                    removed++;
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            Debug.Log(string.Format("[Map4 Sticky] 场景 {0}：移除 {1} 个 StickySurface。", scene.name, removed));
        }

        [MenuItem("Tools/PAO/关卡/可粘地形：查看场景统计", priority = 64)]
        public static void LogStatistics()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return;
            }

            int total = 0;
            int sticky = 0;
            List<GameObject> objects = CollectObjects(scene);
            for (int i = 0; i < objects.Count; i++)
            {
                GameObject go = objects[i];
                if (go == null)
                {
                    continue;
                }

                if (go.GetComponents<Collider>().Length == 0)
                {
                    continue;
                }

                total++;
                if (go.GetComponentInParent<StickySurface>() != null)
                {
                    sticky++;
                }
            }

            Debug.Log(string.Format("[Map4 Sticky] 场景 {0}：{1} 个带碰撞体的物体，其中 {2} 个可粘。", scene.name, total, sticky));
        }

        private static List<GameObject> CollectObjects(Scene scene)
        {
            List<GameObject> objects = new List<GameObject>(512);
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                {
                    continue;
                }

                Transform[] transforms = roots[i].GetComponentsInChildren<Transform>(true);
                for (int t = 0; t < transforms.Length; t++)
                {
                    if (transforms[t] != null)
                    {
                        objects.Add(transforms[t].gameObject);
                    }
                }
            }

            return objects;
        }

        private static bool HasSolidCollider(Collider[] colliders)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && !colliders[i].isTrigger)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>返回跳过原因（ground / bubble-wall / other），null 表示应当挂上可粘。</summary>
        private static string GetSkipReason(GameObject go)
        {
            if (go.GetComponent<Terrain>() != null || go.GetComponent<TerrainCollider>() != null)
            {
                return "ground";
            }

            if (go.GetComponent<TerrainBubble>() != null)
            {
                return "bubble-wall";
            }

            string name = go.name.ToLowerInvariant();
            for (int i = 0; i < s_SkipNameKeywords.Length; i++)
            {
                if (name.Contains(s_SkipNameKeywords[i]))
                {
                    return "ground";
                }
            }

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null)
                    {
                        continue;
                    }

                    for (int k = 0; k < s_SkipMaterialNames.Length; k++)
                    {
                        if (materials[i].name == s_SkipMaterialNames[k])
                        {
                            return s_SkipMaterialNames[k] == "WB_Destructible" ? "bubble-wall" : "ground";
                        }
                    }
                }
            }

            return null;
        }
    }
}

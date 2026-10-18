using UnityEditor;
using UnityEngine;

namespace PAO.EditorTools
{
    /// <summary>
    /// 按**网格的真实包围盒**重设碰撞体，修"换了 mesh 之后碰撞对不上"的问题。
    ///
    /// 【问题是怎么来的】
    /// 这套白盒地形的碰撞体都是 BoxCollider / SphereCollider，且 **size 固定 1×1×1**，
    /// 靠 Transform 的缩放来"变大变小"（Unity 原始 Cube 的网格正好是 1×1×1）。
    ///
    /// 一旦把网格换成别的（导入的 FBX、改过顶点的 mesh、别的 primitive），
    /// 网格的真实尺寸就不再是 1×1×1 了，而碰撞体还是 1×1×1 ——
    /// 于是碰撞体积和看到的形状**对不上**：可能偏小、偏大、或者位置歪掉。
    ///
    /// 【这个工具怎么修】
    /// 读 MeshFilter.sharedMesh.bounds（网格本地空间的包围盒），
    /// 然后把碰撞体的 center / size 设成与它一致。
    /// 这样"看到的形状"和"挡人的形状"就永远一致，跟 Transform 缩放无关。
    ///
    /// 【为什么用 bounds 而不是重算顶点】
    /// Mesh.bounds 是 Unity 预先算好的包围盒，读取零成本；
    /// 重算顶点只在需要"精确到三角面"时才值得，而这里只要挡住人。
    /// 想要完全贴合形状的话，用 MeshCollider 更合适（工具里有那个选项）。
    ///
    /// 用法：
    ///   1. 在 Hierarchy 里**选中**要修的物体（可多选）
    ///   2. 菜单 Tools → PAO → 关卡白盒 → 按 Mesh 修正碰撞体
    /// </summary>
    public static class MeshColliderFixer
    {
        private const string kMenuPath = "Tools/PAO/关卡白盒/按 Mesh 修正碰撞体";

        /// <summary>是否改成 MeshCollider（完全贴合形状，但开销大一些）。</summary>
        private static bool s_UseMeshCollider = false;

        [MenuItem(kMenuPath, priority = 88)]
        public static void FixSelected()
        {
            GameObject[] selected = Selection.gameObjects;

            if (selected == null || selected.Length == 0)
            {
                EditorUtility.DisplayDialog("按 Mesh 修正碰撞体",
                    "请先在 Hierarchy 里选中要修的物体（可以多选）。\n\n" +
                    "工具会把它们的碰撞体调成和网格包围盒一致。", "好");
                return;
            }

            int fixedCount = 0;
            int added = 0;
            int skipped = 0;
            string report = "";

            // 统一走 Undo，出错了能 Ctrl+Z
            Undo.SetCurrentGroupName("按 Mesh 修正碰撞体");
            int group = Undo.GetCurrentGroup();

            for (int i = 0; i < selected.Length; i++)
            {
                GameObject go = selected[i];

                // 子物体也可能有网格，一并处理（比如模型挂了一层父物体）
                MeshFilter[] filters = go.GetComponentsInChildren<MeshFilter>(true);

                if (filters.Length == 0)
                {
                    skipped++;
                    report += "  · " + go.name + "：没有 MeshFilter，跳过\n";
                    continue;
                }

                for (int f = 0; f < filters.Length; f++)
                {
                    GameObject target = filters[f].gameObject;
                    Mesh mesh = filters[f].sharedMesh;

                    if (mesh == null)
                    {
                        skipped++;
                        report += "  · " + target.name + "：MeshFilter 上没挂网格，跳过\n";
                        continue;
                    }

                    if (FixOne(target, mesh))
                    {
                        fixedCount++;
                        report += "  · " + target.name + "：碰撞体已按包围盒 " +
                                  FormatBounds(mesh.bounds) + " 修正\n";
                    }
                    else if (AddOne(target, mesh))
                    {
                        added++;
                        report += "  · " + target.name + "：原本没有碰撞体，已按包围盒添加\n";
                    }
                    else
                    {
                        skipped++;
                    }
                }
            }

            Undo.CollapseUndoOperations(group);

            string summary = "修正 " + fixedCount + " 个，新增 " + added +
                             " 个，跳过 " + skipped + " 个。\n\n" + report;

            Debug.Log("[MeshColliderFixer] " + summary.Replace("\n", " "));

            EditorUtility.DisplayDialog("按 Mesh 修正碰撞体",
                summary + "\n如果改错了：Ctrl+Z 可以撤销。", "好");
        }

        /// <summary>
        /// 修正已有碰撞体。返回是否改动了东西。
        /// </summary>
        private static bool FixOne(GameObject go, Mesh mesh)
        {
            Bounds b = mesh.bounds;
            bool changed = false;

            // ---- BoxCollider：center / size 直接照包围盒设 ----
            BoxCollider box = go.GetComponent<BoxCollider>();

            if (box != null)
            {
                // 注意：这里设的是**本地空间**的 center/size，
                // 而 mesh.bounds 也是本地空间的 —— 两边同一坐标系，不需要换算缩放。
                // 最终效果 = 本地尺寸 × Transform 缩放，和看到的网格完全一致。
                Undo.RecordObject(box, "修正 BoxCollider");
                box.center = b.center;
                box.size = b.size;
                EditorUtility.SetDirty(box);
                changed = true;
            }

            // ---- SphereCollider：用包围盒的外接球 ----
            SphereCollider sphere = go.GetComponent<SphereCollider>();

            if (sphere != null)
            {
                Undo.RecordObject(sphere, "修正 SphereCollider");
                sphere.center = b.center;
                sphere.radius = b.extents.magnitude;   // 外接球半径
                EditorUtility.SetDirty(sphere);
                changed = true;
            }

            // ---- CapsuleCollider：用包围盒的长轴 ----
            CapsuleCollider capsule = go.GetComponent<CapsuleCollider>();

            if (capsule != null)
            {
                Undo.RecordObject(capsule, "修正 CapsuleCollider");
                capsule.center = b.center;

                Vector3 e = b.extents;

                // 哪一维最长，胶囊就朝哪个方向立
                if (e.y >= e.x && e.y >= e.z)
                {
                    capsule.direction = 1;
                    capsule.height = b.size.y;
                    capsule.radius = Mathf.Max(e.x, e.z);
                }
                else if (e.x >= e.y && e.x >= e.z)
                {
                    capsule.direction = 0;
                    capsule.height = b.size.x;
                    capsule.radius = Mathf.Max(e.y, e.z);
                }
                else
                {
                    capsule.direction = 2;
                    capsule.height = b.size.z;
                    capsule.radius = Mathf.Max(e.x, e.y);
                }

                EditorUtility.SetDirty(capsule);
                changed = true;
            }

            // ---- MeshCollider：直接用网格 ----
            MeshCollider meshCollider = go.GetComponent<MeshCollider>();

            if (meshCollider != null)
            {
                Undo.RecordObject(meshCollider, "修正 MeshCollider");
                meshCollider.sharedMesh = mesh;
                meshCollider.convex = false;
                EditorUtility.SetDirty(meshCollider);
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// 原本完全没碰撞体：按包围盒补一个。
        /// </summary>
        private static bool AddOne(GameObject go, Mesh mesh)
        {
            Bounds b = mesh.bounds;

            if (s_UseMeshCollider)
            {
                MeshCollider mc = Undo.AddComponent<MeshCollider>(go);
                mc.sharedMesh = mesh;
                mc.convex = false;
                return true;
            }

            // 默认加 BoxCollider：最省、最稳，白盒地形足够
            BoxCollider box = Undo.AddComponent<BoxCollider>(go);
            box.center = b.center;
            box.size = b.size;

            Debug.Log("[MeshColliderFixer] " + go.name + " 原本没有碰撞体，已补一个 BoxCollider（" +
                      FormatBounds(b) + "）");

            return true;
        }

        private static string FormatBounds(Bounds b)
        {
            return "center(" + b.center.x.ToString("F2") + "," + b.center.y.ToString("F2") + "," +
                   b.center.z.ToString("F2") + ") size(" + b.size.x.ToString("F2") + "," +
                   b.size.y.ToString("F2") + "," + b.size.z.ToString("F2") + ")";
        }

        // ==================== 额外的实用命令 ====================

        /// <summary>
        /// 把碰撞体重置回"和网格一致"的最简状态：
        /// 对使用 Unity 原始 Cube/Sphere/Capsule 的物体，把 center 归零、size/radius 归 1。
        ///
        /// 【什么时候用】
        /// 网格确实是 1×1×1 的标准 primitive，但碰撞体被手动调歪了。
        /// 这时候回到 1×1×1 才是最干净的 —— 尺寸完全交给 Transform 缩放。
        /// </summary>
        [MenuItem("Tools/PAO/关卡白盒/碰撞体重置为 1x1x1（标准 primitive）", priority = 89)]
        public static void ResetToUnit()
        {
            GameObject[] selected = Selection.gameObjects;

            if (selected == null || selected.Length == 0)
            {
                EditorUtility.DisplayDialog("重置碰撞体",
                    "请先在 Hierarchy 里选中物体。", "好");
                return;
            }

            int n = 0;

            Undo.SetCurrentGroupName("重置碰撞体为 1x1x1");
            int group = Undo.GetCurrentGroup();

            for (int i = 0; i < selected.Length; i++)
            {
                // 注意类型是 Collider[]，不是 Component[] ——
                // GetComponentsInChildren<T>() 返回 T[]；
                // 写成 Component[] 会在下面 `Collider col = comps[c];` 报 CS0266
                // （Component 不能隐式转成 Collider）。
                Collider[] comps = selected[i].GetComponentsInChildren<Collider>(true);

                for (int c = 0; c < comps.Length; c++)
                {
                    Collider col = comps[c];

                    BoxCollider box = col as BoxCollider;
                    if (box != null)
                    {
                        Undo.RecordObject(box, "重置 BoxCollider");
                        box.center = Vector3.zero;
                        box.size = Vector3.one;
                        EditorUtility.SetDirty(box);
                        n++;
                        continue;
                    }

                    SphereCollider sphere = col as SphereCollider;
                    if (sphere != null)
                    {
                        Undo.RecordObject(sphere, "重置 SphereCollider");
                        sphere.center = Vector3.zero;
                        sphere.radius = 0.5f;      // Unity 原始 Sphere 的半径
                        EditorUtility.SetDirty(sphere);
                        n++;
                        continue;
                    }

                    CapsuleCollider capsule = col as CapsuleCollider;
                    if (capsule != null)
                    {
                        Undo.RecordObject(capsule, "重置 CapsuleCollider");
                        capsule.center = Vector3.zero;
                        capsule.height = 2f;
                        capsule.radius = 0.5f;
                        EditorUtility.SetDirty(capsule);
                        n++;
                    }
                }
            }

            Undo.CollapseUndoOperations(group);

            Debug.Log("[MeshColliderFixer] 已重置 " + n + " 个碰撞体为 1x1x1。");
            EditorUtility.DisplayDialog("重置碰撞体",
                "已重置 " + n + " 个碰撞体。\n\n" +
                "注意：这只对「网格确实是 1×1×1 标准 primitive」的物体成立。\n" +
                "如果网格是导入的模型，应该用「按 Mesh 修正碰撞体」那个菜单。", "好");
        }
    }
}

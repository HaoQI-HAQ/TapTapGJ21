using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    /// <summary>
    /// 把「选中的一批物体」变成一架炸弹秋千。
    ///
    /// 【为什么要用工具而不是手搭】
    /// 秋千的三件事必须同时对上，手搭很容易错一个：
    ///   1. 支点必须在**选中物体的最高点**（不然转起来会啃地）；
    ///   2. 选中物体必须整体挂到支点下面，而且世界坐标不能变（不然关卡会错位）；
    ///   3. 摆长要等于「支点 → 重心」，否则周期跟体型对不上。
    /// 所以做成一个一键菜单：选好物体 → 点一下 → 支点、父级、摆长、轴全部算好。
    ///
    /// 【生成的结构】
    ///   Swing_xxx                ← 支点在这里，挂 BombDrivenSwing
    ///   └─ Swing_xxx_Body        ← 只是个偏移壳，保证原始物体的世界坐标不变
    ///      ├─ 原来的物体 A
    ///      └─ 原来的物体 B
    ///
    /// 【典型的用法】
    /// 关卡里"用四根柱子吊着的平台"：把台板 + 四根柱子一起选中 →
    /// 点菜单 → 支点自动落在柱子顶端，整组东西就开始以那里为轴前后荡。
    ///
    /// 菜单：
    ///   Tools/PAO/关卡机关/把选中的物体做成炸弹秋千
    ///   Tools/PAO/关卡机关/取消秋千（还原成原来的物体）
    ///   Tools/PAO/关卡机关/在当前视角新建一架测试秋千
    ///   Tools/PAO/关卡机关/诊断：打印当前选中的物体
    /// </summary>
    public static class SwingRigSetup
    {
        private const string kMenu = "Tools/PAO/关卡机关/";

        /// <summary>默认最大摆角（度）。</summary>
        private const float kDefaultMaxAngle = 50f;

        /// <summary>默认一次满力度爆炸的角冲量（度/秒）。</summary>
        private const float kDefaultBlastImpulse = 90f;

        private const string kRigPrefix = "Swing_";
        private const string kBodySuffix = "_Body";

        // ==================== 菜单：把选中物体做成秋千 ====================

        [MenuItem(kMenu + "把选中的物体做成炸弹秋千", priority = 60)]
        public static void WrapSelection()
        {
            Transform[] roots = TopMostSelected();

            if (roots.Length == 0)
            {
                EditorUtility.DisplayDialog("炸弹秋千",
                    "先在 Hierarchy 里选中要做成秋千的物体（可以多选）。\n\n" +
                    "建议把「台板 + 吊住它的柱子/绳子」一起选上：支点会自动放在这组东西的最高点。",
                    "好");
                return;
            }

            string rigName = kRigPrefix + roots[0].name;

            Transform rig = Wrap(roots, rigName, null);

            if (rig != null)
            {
                Selection.activeGameObject = rig.gameObject;
                EditorGUIUtility.PingObject(rig.gameObject);
                SceneView.RepaintAll();
            }
        }

        // ==================== 菜单：诊断（只打日志，不改场景） ====================

        [MenuItem(kMenu + "诊断：打印当前选中的物体", priority = 63)]
        public static void LogSelection()
        {
            GameObject[] selected = Selection.gameObjects;

            if (selected.Length == 0)
            {
                Debug.Log("[SwingRig][诊断] 当前没有选中任何物体。");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("[SwingRig][诊断] 当前选中 ").Append(selected.Length).Append(" 个物体：\n");

            for (int i = 0; i < selected.Length; i++)
            {
                GameObject go = selected[i];

                if (go == null)
                {
                    continue;
                }

                Bounds bounds;

                if (TryGetBounds(new[] { go.transform }, out bounds))
                {
                    sb.Append("  [").Append(i).Append("] ").Append(go.name)
                      .Append("  pos=").Append(go.transform.position.ToString("F2"))
                      .Append("  boundsCenter=").Append(bounds.center.ToString("F2"))
                      .Append("  boundsSize=").Append(bounds.size.ToString("F2"))
                      .Append('\n');
                }
                else
                {
                    sb.Append("  [").Append(i).Append("] ").Append(go.name)
                      .Append("  pos=").Append(go.transform.position.ToString("F2"))
                      .Append("  (没有 Renderer)\n");
                }
            }

            Bounds total;

            if (TryGetBounds(TopMostSelected(), out total))
            {
                sb.Append("合计包围盒：center=").Append(total.center.ToString("F2"))
                  .Append(" size=").Append(total.size.ToString("F2"))
                  .Append(" maxY=").Append(total.max.y.ToString("F2"))
                  .Append("\n会用的支点：")
                  .Append(new Vector3(total.center.x, total.max.y, total.center.z).ToString("F2"));
            }

            Debug.Log(sb.ToString());
        }

        // ==================== 菜单：还原 ====================

        [MenuItem(kMenu + "取消秋千（还原成原来的物体）", priority = 61)]
        public static void UnwrapSelection()
        {
            int count = 0;

            GameObject[] selected = Selection.gameObjects;

            for (int i = 0; i < selected.Length; i++)
            {
                if (selected[i] == null)
                {
                    continue;
                }

                BombDrivenSwing swing = selected[i].GetComponent<BombDrivenSwing>();

                if (swing == null)
                {
                    swing = selected[i].GetComponentInChildren<BombDrivenSwing>();
                }

                if (swing == null)
                {
                    continue;
                }

                if (Unwrap(swing.transform))
                {
                    count++;
                }
            }

            if (count == 0)
            {
                Debug.LogWarning("[SwingRig] 选中的物体里没有可还原的秋千。");
                return;
            }

            Debug.Log("[SwingRig] 已还原 " + count + " 架秋千（物体已回到原来的父级）。");
        }

        // ==================== 菜单：新建一架测试秋千 ====================

        [MenuItem(kMenu + "在当前视角新建一架测试秋千", priority = 62)]
        public static void CreateTestSwing()
        {
            SceneView view = SceneView.lastActiveSceneView;
            Vector3 pivot = view != null
                ? view.pivot + Vector3.up * 4f
                : new Vector3(0f, 4f, 0f);

            const float hangLength = 3.5f;

            GameObject rig = new GameObject(kRigPrefix + "Test");
            Undo.RegisterCreatedObjectUndo(rig, "新建测试秋千");
            rig.transform.position = pivot;
            SceneManager.MoveGameObjectToScene(rig, SceneManager.GetActiveScene());

            GameObject body = new GameObject(rig.name + kBodySuffix);
            body.transform.SetParent(rig.transform, false);

            // 座板
            GameObject seat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seat.name = "Seat";
            Undo.RegisterCreatedObjectUndo(seat, "新建测试秋千");
            seat.transform.SetParent(body.transform, false);
            seat.transform.localPosition = new Vector3(0f, -hangLength, 0f);
            seat.transform.localScale = new Vector3(2.2f, 0.18f, 1.1f);

            // 两根绳子：只做视觉，不带碰撞
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject rope = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rope.name = side < 0 ? "Rope_L" : "Rope_R";
                Undo.RegisterCreatedObjectUndo(rope, "新建测试秋千");
                rope.transform.SetParent(body.transform, false);
                rope.transform.localPosition = new Vector3(side * 0.95f, -hangLength * 0.5f, 0f);
                rope.transform.localScale = new Vector3(0.08f, hangLength, 0.08f);

                Collider ropeCollider = rope.GetComponent<Collider>();

                if (ropeCollider != null)
                {
                    Object.DestroyImmediate(ropeCollider);
                }
            }

            BombDrivenSwing swing = Undo.AddComponent<BombDrivenSwing>(rig);
            swing.Configure(Vector3.right, kDefaultMaxAngle, hangLength, 0f, kDefaultBlastImpulse);

            Selection.activeGameObject = rig;
            EditorSceneManager.MarkSceneDirty(rig.scene);

            Debug.Log("[SwingRig] 已在 " + pivot + " 新建测试秋千：最大摆角 " + kDefaultMaxAngle +
                      " 度，摆长 " + hangLength + " 米（周期约 " +
                      PendulumPeriod(hangLength).ToString("F2") + " 秒）。");
        }

        // ==================== 核心：包装 ====================

        /// <summary>
        /// 把一批物体挂到新的支点下面，并加一个 <see cref="BombDrivenSwing"/>。
        /// </summary>
        /// <param name="roots">要变成秋千的物体（会保持世界坐标）。</param>
        /// <param name="rigName">生成的父物体名字。</param>
        /// <param name="pivotOverride">支点；null = 自动取包围盒顶部中心。</param>
        public static Transform Wrap(Transform[] roots, string rigName, Vector3? pivotOverride)
        {
            if (roots == null || roots.Length == 0)
            {
                return null;
            }

            Bounds bounds;

            if (!TryGetBounds(roots, out bounds))
            {
                Debug.LogWarning("[SwingRig] 选中的物体既没有 Renderer 也没有 Collider，算不出包围盒。");
                return null;
            }

            Vector3 pivot = pivotOverride.HasValue
                ? pivotOverride.Value
                : new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);

            // 摆长 = 支点到重心（包围盒中心）的高度。
            // 不是"支点到台板"的高度 —— 刚体是整组东西，
            // 质量分布在整条杆上，等效摆长比杆长的一半还要小一点，取包围盒中心已经够用。
            float hangLength = Mathf.Clamp(pivot.y - bounds.center.y, 0.5f, 40f);

            // 轴：绕**水平方向上较长的那一边**转，这样摆动方向是"前后"。
            // 门架 6m 宽 1.5m 深 → 轴取 X → 在 Z 方向荡，正好是过道方向。
            Vector3 size = bounds.size;
            Vector3 axis = size.x >= size.z ? Vector3.right : Vector3.forward;

            GameObject rig = new GameObject(rigName);
            Undo.RegisterCreatedObjectUndo(rig, "做成炸弹秋千");
            rig.transform.position = pivot;
            rig.transform.rotation = Quaternion.identity;

            Scene scene = roots[0].gameObject.scene;

            if (scene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(rig, scene);
            }

            GameObject body = new GameObject(rigName + kBodySuffix);
            Undo.RegisterCreatedObjectUndo(body, "做成炸弹秋千");
            body.transform.SetParent(rig.transform, false);

            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                {
                    continue;
                }

                // 保持世界坐标：物体只是换了个爸爸
                Undo.SetTransformParent(roots[i], body.transform, "做成炸弹秋千");
            }

            BombDrivenSwing swing = Undo.AddComponent<BombDrivenSwing>(rig);
            swing.Configure(axis, kDefaultMaxAngle, hangLength, 0f, kDefaultBlastImpulse);

            EditorSceneManager.MarkSceneDirty(scene);

            StringBuilder names = new StringBuilder();

            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                {
                    continue;
                }

                if (names.Length > 0)
                {
                    names.Append(", ");
                }

                names.Append(roots[i].name);
            }

            Debug.Log("[SwingRig] 已生成 \"" + rigName + "\"：" + roots.Length + " 个物体，" +
                      "支点 " + pivot + "，摆长 " + hangLength.ToString("F2") + " 米，" +
                      "轴 " + axis + "，最大摆角 " + kDefaultMaxAngle + " 度，" +
                      "周期约 " + PendulumPeriod(hangLength).ToString("F2") + " 秒。\n" +
                      "物体：" + names);

            return rig.transform;
        }

        // ==================== 核心：还原 ====================

        /// <summary>拆掉一架秋千：把物体还给原来的父级，然后删掉支点。</summary>
        public static bool Unwrap(Transform rig)
        {
            if (rig == null)
            {
                return false;
            }

            BombDrivenSwing swing = rig.GetComponent<BombDrivenSwing>();

            if (swing == null)
            {
                return false;
            }

            Transform parent = rig.parent;

            // 把 Body 下的物体提到秋千原来的父级，世界坐标不变
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
                Undo.SetTransformParent(children[i], parent, "还原炸弹秋千");
            }

            // 角度归零，免得物体还带着一身旋转
            swing.ResetSwing();

            Undo.DestroyObjectImmediate(rig.gameObject);
            return true;
        }

        // ==================== 辅助 ====================

        /// <summary>只保留"最上面那一层"的选中项（选了父子就别重复处理）。</summary>
        private static Transform[] TopMostSelected()
        {
            GameObject[] selected = Selection.gameObjects;
            List<Transform> result = new List<Transform>(selected.Length);

            for (int i = 0; i < selected.Length; i++)
            {
                if (selected[i] == null)
                {
                    continue;
                }

                Transform t = selected[i].transform;
                bool nested = false;

                for (int s = 0; s < selected.Length; s++)
                {
                    if (selected[s] == null || selected[s] == selected[i])
                    {
                        continue;
                    }

                    if (t.IsChildOf(selected[s].transform))
                    {
                        nested = true;
                        break;
                    }
                }

                if (!nested)
                {
                    result.Add(t);
                }
            }

            return result.ToArray();
        }

        /// <summary>算一批物体的世界包围盒（Renderer 优先，没有就用 Collider）。</summary>
        public static bool TryGetBounds(Transform[] roots, out Bounds bounds)
        {
            bounds = default(Bounds);
            bool has = false;

            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                {
                    continue;
                }

                Renderer[] renderers = roots[i].GetComponentsInChildren<Renderer>(true);

                for (int r = 0; r < renderers.Length; r++)
                {
                    if (renderers[r] == null)
                    {
                        continue;
                    }

                    if (!has)
                    {
                        bounds = renderers[r].bounds;
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderers[r].bounds);
                    }
                }

                if (renderers.Length == 0)
                {
                    Collider[] colliders = roots[i].GetComponentsInChildren<Collider>(true);

                    for (int c = 0; c < colliders.Length; c++)
                    {
                        if (colliders[c] == null)
                        {
                            continue;
                        }

                        if (!has)
                        {
                            bounds = colliders[c].bounds;
                            has = true;
                        }
                        else
                        {
                            bounds.Encapsulate(colliders[c].bounds);
                        }
                    }
                }
            }

            return has;
        }

        /// <summary>单摆周期 2π√(L/g)。</summary>
        private static float PendulumPeriod(float length)
        {
            return 2f * Mathf.PI * Mathf.Sqrt(Mathf.Max(0.1f, length) / 9.81f);
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    /// <summary>
    /// 把「一块板子（+ 下面/中间的柱子）」做成**炸弹跷跷板**：
    /// 在柱子顶上放一个支点，板子挂在支点下面，炸弹炸哪边，哪边就压下去、另一边翘起来，
    /// 站在翘起那端的人会被顺势挑飞。
    ///
    /// 【复用 BombDrivenSwing】
    /// 跷跷板和秋千的运动学其实一样：一个刚体绕固定支点转，被爆炸按力臂推。
    /// 区别只是支点在**下面**、摆角小、而且要把乘客挑出去。
    /// 所以这里不另写一套，直接复用已经验证过的 BombDrivenSwing，
    /// 把 m_LaunchRiders 打开 —— 乘客被抬升太快时顺着那一瞬间的速度飞出去。
    ///
    /// 【选什么】
    /// 选中「板子」；如果柱子也一起选中，支点就取柱子顶面中心（推荐）。
    /// 只选板子的话，支点取板子包围盒中心（相当于板子正中垫了一块石头）。
    ///
    /// 菜单：
    ///   Tools/PAO/关卡机关/把选中的板子做成炸弹跷跷板
    ///   Tools/PAO/关卡机关/取消跷跷板（还原）
    /// </summary>
    public static class BombSeesawSetup
    {
        private const string kMenu = "Tools/PAO/关卡机关/";
        private const string kRigPrefix = "Seesaw_";
        private const string kBodySuffix = "_Body";

        private const float kMaxAngle = 30f;
        private const float kPendulumLength = 1.6f;
        private const float kBlastImpulse = 130f;
        private const float kBlastRadius = 4.5f;
        private const float kLaunchRiseSpeed = 2.8f;
        private const float kLaunchBoost = 1.25f;

        [MenuItem(kMenu + "把选中的板子做成炸弹跷跷板", priority = 52)]
        public static void BuildFromSelection()
        {
            GameObject[] selection = Selection.gameObjects;

            if (selection.Length == 0)
            {
                EditorUtility.DisplayDialog("炸弹跷跷板",
                    "先选中要做跷跷板的那块板子；柱子也可以一起选（支点会落在柱子顶上）。", "好");
                return;
            }

            Transform plank = null;
            Transform pillar = null;

            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] == null)
                {
                    continue;
                }

                Transform t = selection[i].transform;

                if (IsPillar(t))
                {
                    if (pillar == null || GetBounds(t).size.y > GetBounds(pillar).size.y)
                    {
                        pillar = t;
                    }
                }
                else if (plank == null)
                {
                    plank = t;
                }
            }

            if (plank == null)
            {
                EditorUtility.DisplayDialog("炸弹跷跷板",
                    "没有找到「板子」。柱子和板子都像柱子的话，先把板子单独选中再点一次。", "好");
                return;
            }

            Transform rig = Build(plank, pillar, out string message);

            if (rig != null)
            {
                Selection.activeGameObject = rig.gameObject;
                SceneView.RepaintAll();
            }

            Debug.Log("[Seesaw] " + (rig != null ? "建成：" : "失败：") + message);
        }

        [MenuItem(kMenu + "取消跷跷板（还原）", priority = 53)]
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

                BombDrivenSwing swing = selection[i].GetComponent<BombDrivenSwing>();

                if (swing == null)
                {
                    swing = selection[i].GetComponentInChildren<BombDrivenSwing>();
                }

                if (swing != null && SwingRigSetup.Unwrap(swing.transform))
                {
                    count++;
                }
            }

            Debug.Log("[Seesaw] 已还原 " + count + " 个跷跷板。");
        }

        // ==================== 核心 ====================

        public static Transform Build(Transform plank, Transform pillar, out string message)
        {
            message = string.Empty;

            if (plank == null)
            {
                message = "板子为空";
                return null;
            }

            Bounds plankBounds = GetBounds(plank);
            Vector3 size = plankBounds.size;

            // 板子的长边方向（水平）
            bool longIsX = size.x >= size.z;

            // 支点：优先取柱子顶面中心，没有柱子就用板子中心
            Vector3 pivot;

            if (pillar != null)
            {
                Bounds pillarBounds = GetBounds(pillar);
                pivot = new Vector3(pillarBounds.center.x, pillarBounds.max.y, pillarBounds.center.z);
            }
            else
            {
                pivot = plankBounds.center;
            }

            // 铰链轴：**垂直于板子长边**的那根水平轴（绕它转，两头才会一上一下）
            Vector3 axis = longIsX ? Vector3.forward : Vector3.right;

            Scene scene = plank.gameObject.scene;

            GameObject rig = new GameObject(kRigPrefix + plank.name);
            Undo.RegisterCreatedObjectUndo(rig, "做成跷跷板");
            rig.transform.position = pivot;
            rig.transform.rotation = Quaternion.identity;

            if (scene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(rig, scene);
            }

            GameObject body = new GameObject(rig.name + kBodySuffix);
            Undo.RegisterCreatedObjectUndo(body, "做成跷跷板");
            body.transform.SetParent(rig.transform, false);

            Undo.SetTransformParent(plank, body.transform, "做成跷跷板");

            BombDrivenSwing swing = Undo.AddComponent<BombDrivenSwing>(rig);
            swing.Configure(axis, kMaxAngle, kPendulumLength, 0f, kBlastImpulse, kBlastRadius,
                            true, kLaunchRiseSpeed, kLaunchBoost);

            EditorSceneManager.MarkSceneDirty(scene);

            message = "板子=" + plank.name +
                      (pillar != null ? "，支点=柱子顶 " + pivot.ToString("F2") : "，支点=板中心 " + pivot.ToString("F2")) +
                      "，铰链轴=" + axis + "，最大角 " + kMaxAngle + "°，推 " + kBlastImpulse +
                      "，挑飞阈值 " + kLaunchRiseSpeed + " m/s ×" + kLaunchBoost;

            return rig.transform;
        }

        /// <summary>像不像"柱子"：又高又细的竖直物体。</summary>
        private static bool IsPillar(Transform t)
        {
            Bounds b = GetBounds(t);
            float horizontal = Mathf.Max(b.size.x, b.size.z);

            return b.size.y >= 2f && b.size.y >= horizontal * 1.5f && Mathf.Min(b.size.x, b.size.z) <= 1.5f;
        }

        private static Bounds GetBounds(Transform root)
        {
            Bounds bounds = default(Bounds);
            bool has = false;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;

                if (!has) { bounds = renderers[i].bounds; has = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }

            if (has)
            {
                return bounds;
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null) continue;

                if (!has) { bounds = colliders[i].bounds; has = true; }
                else bounds.Encapsulate(colliders[i].bounds);
            }

            return bounds;
        }
    }
}

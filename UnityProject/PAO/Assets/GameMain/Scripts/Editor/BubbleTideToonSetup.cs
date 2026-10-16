using System.Text;
using UnityEditor;
using UnityEngine;

namespace PAO.BubbleTide.EditorTools
{
    /// <summary>
    /// 泡泡潮·卡通泡泡（新版）的一键安装工具。
    ///
    /// 和 BubbleTideSceneSetup 的分工一样：
    ///   · 建材质          → 走 AssetDatabase，绝不手写 YAML
    ///   · 挂组件          → 用 Undo.AddComponent，可以一键撤销
    ///
    /// 【原则】这个工具**不会**修改 BubbleTideDirector / BubbleTideCellRenderer，
    /// 也不会动 PAO/BubbleBall 那一套。它只做两件事：建一个新材质、在潮水根物体上
    /// 多挂一个 BubbleTideInstanceRenderer。
    /// </summary>
    public static class BubbleTideToonSetup
    {
        private const string k_ShaderName  = "PAO/BubbleTideToon";
        private const string k_ShaderPath  = "Assets/GameMain/Art/Shaders/BubbleTideToon.shader";
        private const string k_MaterialDir = "Assets/GameMain/Art/Materials";
        private const string k_MaterialPath = k_MaterialDir + "/BubbleTideToon.mat";
        private const string k_RootName    = "BubbleTide";

        // ==================================================================
        // 1. 建材质
        // ==================================================================

        [MenuItem("Tools/PAO/泡泡潮/新建卡通泡泡材质", priority = 60)]
        public static void CreateMaterial()
        {
            Material material = CreateOrLoadMaterial(true);
            if (material == null)
            {
                return;
            }

            Selection.activeObject = material;
            EditorGUIUtility.PingObject(material);

            EditorUtility.DisplayDialog("泡泡潮·卡通",
                "材质已就绪：\n" + k_MaterialPath + "\n\n" +
                "着色器面板上请确认 AlphaToMask 已勾选 ——\n" +
                "它是「用抖动裁剪表达半透明」的关键，关掉的话边缘会变成硬边。",
                "好");
        }

        private static Material CreateOrLoadMaterial(bool interactive)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(k_ShaderPath);
            if (shader == null)
            {
                // 路径对不上就退回到按名字找（比如有人把 shader 挪了位置）
                shader = Shader.Find(k_ShaderName);
            }

            if (shader == null)
            {
                Report(interactive, "找不到 shader。\n期望路径：\n" + k_ShaderPath);
                return null;
            }

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
            if (existing != null)
            {
                // 已经有了就只修一下关键开关，不覆盖美术调过的参数
                bool dirty = false;

                if (existing.shader != shader)
                {
                    existing.shader = shader;
                    dirty = true;
                }

                if (!existing.enableInstancing)
                {
                    existing.enableInstancing = true;
                    dirty = true;
                }

                if (dirty)
                {
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();
                }

                return existing;
            }

            if (!AssetDatabase.IsValidFolder(k_MaterialDir))
            {
                AssetDatabase.CreateFolder("Assets/GameMain/Art", "Materials");
            }

            Material material = new Material(shader);
            material.name = "BubbleTideToon";

            // 关键三项：实例化、AlphaToMask、不投阴影
            // AlphaToMask 是这套「不透明 + 抖动裁剪」方案的前提，必须开
            material.enableInstancing = true;
            material.SetOverrideTag("RenderType", "Opaque");

            try
            {
                material.SetFloat("_AlphaToMask", 1f);
            }
            catch (System.Exception)
            {
                // 某些管线里没有这个属性，忽略即可
            }

            AssetDatabase.CreateAsset(material, k_MaterialPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
        }

        // ==================================================================
        // 2. 挂到潮水根物体上
        // ==================================================================

        [MenuItem("Tools/PAO/泡泡潮/挂上卡通泡泡渲染（新版）", priority = 61)]
        public static void InstallRenderer()
        {
            BubbleTideDirector director = Object.FindObjectOfType<BubbleTideDirector>();
            if (director == null)
            {
                EditorUtility.DisplayDialog("泡泡潮·卡通",
                    "场景里没有 BubbleTideDirector。\n\n" +
                    "请先跑一遍：\n工具 → PAO → 泡泡潮 → 一键搭建潮水",
                    "好");
                return;
            }

            Material material = CreateOrLoadMaterial(true);
            if (material == null)
            {
                return;
            }

            GameObject target = director.gameObject;

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("挂上卡通泡泡渲染");

            BubbleTideInstanceRenderer renderer = target.GetComponent<BubbleTideInstanceRenderer>();
            bool created = false;

            if (renderer == null)
            {
                renderer = Undo.AddComponent<BubbleTideInstanceRenderer>(target);
                created = true;
            }

            // 用 SerializedObject 写私有字段，这样能正确进 Undo
            SerializedObject so = new SerializedObject(renderer);
            so.FindProperty("m_Material").objectReferenceValue = material;
            so.FindProperty("m_Shader").objectReferenceValue = material.shader;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(renderer);
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = target;
            EditorGUIUtility.PingObject(target);

            // 顺手报告一下原版的渲染器状态（只读，不改）
            StringBuilder report = new StringBuilder();
            report.AppendLine("卡通泡泡渲染已就绪" + (created ? "（新建组件）" : "（复用已有组件）"));
            report.AppendLine("  挂载物体：" + target.name);
            report.AppendLine("  材质：" + k_MaterialPath);
            report.AppendLine();

            BubbleTideCellRenderer oldRenderer = target.GetComponent<BubbleTideCellRenderer>();
            if (oldRenderer != null)
            {
                report.AppendLine("提示：原来的体积潮水渲染（BubbleTideCellRenderer）还在。");
                report.AppendLine("      想让它退场，在它的 Inspector 上把 Render Visuals 关掉即可 ——");
                report.AppendLine("      这个工具不会替你去改它。");
            }

            report.AppendLine();
            report.AppendLine("进 Play 之后：");
            report.AppendLine("  · 绿色圆点是已出现的泡泡，橙色虚线圈是还没出现的（Scene 视图）");
            report.AppendLine("  · 左上角第二行会显示「已出现 / 总数 / 半径 / 面数」");
            report.AppendLine("  · 想让泡泡更大或更少，调 Bubble Radius 一个参数就行");

            Debug.Log("[泡泡潮·卡通] " + report.ToString());
            EditorUtility.DisplayDialog("泡泡潮·卡通", report.ToString(), "好");
        }

        // ==================================================================
        // 3. 检查安装状态
        // ==================================================================

        [MenuItem("Tools/PAO/泡泡潮/检查卡通泡泡安装状态", priority = 62)]
        public static void CheckStatus()
        {
            StringBuilder report = new StringBuilder();

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(k_ShaderPath);
            report.AppendLine(shader != null
                ? "✔ Shader 存在：" + k_ShaderPath
                : "✘ 找不到 Shader：" + k_ShaderPath);

            Material material = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
            report.AppendLine(material != null
                ? "✔ 材质存在：" + k_MaterialPath
                : "✘ 还没有材质（跑一次「新建卡通泡泡材质」）");

            if (material != null)
            {
                report.AppendLine(material.enableInstancing
                    ? "✔ 材质已开 GPU Instancing"
                    : "✘ 材质没开 GPU Instancing —— 泡泡会一个一个画，务必打开");

                float alphaToMask = material.GetFloat("_AlphaToMask");
                report.AppendLine(alphaToMask > 0.5f
                    ? "✔ AlphaToMask 已开（边缘抖动裁剪生效）"
                    : "⚠ AlphaToMask 是关的 —— 边缘会变成硬边，建议打开");
            }

            BubbleTideDirector director = Object.FindObjectOfType<BubbleTideDirector>();
            report.AppendLine(director != null
                ? "✔ 场景里有 BubbleTideDirector：" + director.gameObject.name
                : "✘ 场景里没有 BubbleTideDirector");

            if (director != null)
            {
                BubbleTideInstanceRenderer renderer =
                    director.GetComponent<BubbleTideInstanceRenderer>();
                report.AppendLine(renderer != null
                    ? "✔ 已挂上 BubbleTideInstanceRenderer"
                    : "✘ 还没挂 BubbleTideInstanceRenderer（跑一次「挂上卡通泡泡渲染」）");

                BubbleTideCellRenderer oldRenderer = director.GetComponent<BubbleTideCellRenderer>();
                if (oldRenderer != null)
                {
                    report.AppendLine(oldRenderer.RenderVisuals
                        ? "ℹ 原版体积潮水渲染还开着 —— 会和泡泡叠加，想关就在它 Inspector 上取消 Render Visuals"
                        : "✔ 原版体积潮水渲染已关");
                }
            }

            Debug.Log("[泡泡潮·卡通] 安装状态检查\n" + report.ToString());
            EditorUtility.DisplayDialog("泡泡潮·卡通 · 安装状态", report.ToString(), "好");
        }

        private static void Report(bool interactive, string message)
        {
            Debug.LogError("[泡泡潮·卡通] " + message);

            if (interactive)
            {
                EditorUtility.DisplayDialog("泡泡潮·卡通", message, "好");
            }
        }
    }
}

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PAO.BubbleFX.EditorTools
{
    /// <summary>
    /// 一键把 BubbleSDFRenderFeature 装进工程自己的 URP Renderer 资产。
    ///
    /// 完全照搬 URP 自己的 ScriptableRendererDataEditor.AddComponent 流程 ——
    /// 子资产和 featureMap 里的 localFileID 必须成对写对，缺一个 Unity 就会
    /// 当成脏数据把整个 Feature 丢掉。手改 YAML 太容易写坏了，所以走 API。
    ///
    /// ⚠️ 只处理 Assets/ 下的 Renderer。【绝不能碰 Packages/ 里的】：
    /// URP 包里自带一个隐藏的 UniversalRendererData.asset，改它 Unity 会报
    /// 「The following asset(s) located in immutable packages were unexpectedly
    /// altered」，而且包一升级修改就没了、还可能污染整个团队。
    ///
    /// 结果可以用 Ctrl+Z 撤销。
    /// </summary>
    public static class BubbleSDFInstaller
    {
        private const string k_InstallPath   = "Tools/PAO/泡泡 SDF/安装到 URP Renderer";
        private const string k_UninstallPath = "Tools/PAO/泡泡 SDF/从 URP Renderer 卸载";
        private const string k_StatusPath    = "Tools/PAO/泡泡 SDF/检查安装状态";

        // ------------------------------------------------------------------
        // 安装
        // ------------------------------------------------------------------

        [MenuItem(k_InstallPath, priority = 10)]
        public static void Install()
        {
            List<ScriptableRendererData> targets = CollectProjectRendererData();
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("泡泡 SDF",
                    "没找到工程自己的 URP Renderer 资产。\n请确认 Assets/Settings 下有 URP-*-Renderer.asset。",
                    "好");
                return;
            }

            var report = new StringBuilder();
            int installed = 0;
            int skipped   = 0;

            foreach (ScriptableRendererData data in targets)
            {
                if (HasFeature(data))
                {
                    skipped++;
                    report.AppendLine("· " + data.name + " — 已安装，跳过");
                    continue;
                }

                if (AddFeature(data)) installed++;
                report.AppendLine("· " + data.name + " — 已安装");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[BubbleSDF] 安装完成：新增 " + installed + "，跳过 " + skipped + "\n" + report);

            EditorUtility.DisplayDialog("泡泡 SDF",
                "安装完成。\n新增 " + installed + " 个，跳过 " + skipped + " 个。\n\n详见 Console。\n\n" +
                "接下来：\n" +
                "对象 菜单 -> PAO -> 泡泡 SDF -> 生成测试泡泡群",
                "好");
        }

        // ------------------------------------------------------------------
        // 卸载
        // ------------------------------------------------------------------

        /// <summary>
        /// 从所有 URP Renderer 里摘掉泡泡 Feature（含误装进 Packages 的那个）。
        /// 换 URP 版本或想重装时用这个清干净。
        /// </summary>
        [MenuItem(k_UninstallPath, priority = 12)]
        public static void Uninstall()
        {
            List<ScriptableRendererData> targets = CollectAllRendererData(includePackages: true);

            var report = new StringBuilder();
            int removed = 0;

            foreach (ScriptableRendererData data in targets)
            {
                if (data == null || !HasFeature(data))
                    continue;

                if (RemoveFeature(data))
                {
                    removed++;
                    report.AppendLine("· " + data.name + " — 已移除  (" + AssetDatabase.GetAssetPath(data) + ")");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[BubbleSDF] 卸载完成：移除 " + removed + " 处\n" + report);

            EditorUtility.DisplayDialog("泡泡 SDF",
                removed > 0
                    ? "已移除 " + removed + " 处 Bubble SDF Render Feature。\n\n详见 Console。"
                    : "没有找到已安装的 Bubble SDF Render Feature。",
                "好");
        }

        // ------------------------------------------------------------------
        // 状态
        // ------------------------------------------------------------------

        [MenuItem(k_StatusPath, priority = 13)]
        public static void CheckStatus()
        {
            var report = new StringBuilder();

            foreach (ScriptableRendererData data in CollectAllRendererData(includePackages: true))
            {
                if (data == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(data);
                bool inPackage = path.StartsWith("Packages/");

                report.AppendLine("· " + data.name +
                                  (HasFeature(data) ? " — 已安装" : " — 未安装") +
                                  (inPackage ? "   [在 Packages 里！需要卸载]" : ""));
            }

            Shader shader = Shader.Find("PAO/BubbleSDF");
            report.AppendLine();
            report.AppendLine("Shader PAO/BubbleSDF — " + (shader != null ? "找到" : "找不到"));

            // ---- 关键：把 Shader 编译错误直接报出来 ----
            // 泡泡突然整个消失，最常见的原因就是 HLSL 编译失败
            // （pass 0 没输出，pass 1 又把原画面盖掉 -> 全屏什么都看不见）。
            // 这里主动查一遍，省得去 Console 里翻。
            if (shader != null)
            {
                if (!shader.isSupported)
                {
                    report.AppendLine("  !! isSupported = false（当前平台不支持或编译失败）");
                }

                ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
                if (messages == null || messages.Length == 0)
                {
                    report.AppendLine("  编译干净，无错误/警告");
                }
                else
                {
                    report.AppendLine("  !! 有 " + messages.Length + " 条编译信息：");
                    for (int i = 0; i < messages.Length && i < 8; i++)
                    {
                        ShaderMessage m = messages[i];
                        report.AppendLine("    [" + m.severity + "] " + m.message +
                                          "  @ " + m.file + ":" + m.line);
                    }
                }
            }

            report.AppendLine();
            report.AppendLine("Payload stride — " + BubblePayload.Stride + " 字节");
            report.AppendLine("当前登记的泡泡数 — " + BubbleSDFManager.RegisteredCount);

            Debug.Log("[BubbleSDF] 状态检查\n" + report);
            EditorUtility.DisplayDialog("泡泡 SDF 状态", report.ToString(), "好");
        }

        // ------------------------------------------------------------------
        // 内部实现
        // ------------------------------------------------------------------

        /// <summary>照搬 URP 的 AddComponent 流程。</summary>
        private static bool AddFeature(ScriptableRendererData data)
        {
            var so = new SerializedObject(data);
            so.Update();

            SerializedProperty features   = so.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = so.FindProperty("m_RendererFeatureMap");

            if (features == null || featureMap == null)
            {
                Debug.LogError("[BubbleSDF] " + data.name +
                               " 找不到 m_RendererFeatures / m_RendererFeatureMap，请手动 Add Renderer Feature。");
                return false;
            }

            var feature = ScriptableObject.CreateInstance<BubbleSDFRenderFeature>();
            feature.name = "BubbleSDFRenderFeature";
            Undo.RegisterCreatedObjectUndo(feature, "Add Bubble SDF Render Feature");

            // 存成同一个 .asset 的子资产，引用才稳
            if (EditorUtility.IsPersistent(data))
                AssetDatabase.AddObjectToAsset(feature, data);

            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

            // 先扩容再赋值 —— Unity 序列化列表就是这个套路
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;

            featureMap.arraySize++;
            featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;

            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(data);
            return true;
        }

        /// <summary>把泡泡 Feature 从 Renderer 里摘掉（含它作为子资产的实例）。</summary>
        private static bool RemoveFeature(ScriptableRendererData data)
        {
            var so = new SerializedObject(data);
            so.Update();

            SerializedProperty features   = so.FindProperty("m_RendererFeatures");
            SerializedProperty featureMap = so.FindProperty("m_RendererFeatureMap");

            if (features == null || featureMap == null)
                return false;

            bool changed = false;

            // 从后往前删，避免索引错位
            for (int i = features.arraySize - 1; i >= 0; i--)
            {
                var feature = features.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererFeature;
                if (!(feature is BubbleSDFRenderFeature))
                    continue;

                features.DeleteArrayElementAtIndex(i);

                if (i < featureMap.arraySize)
                    featureMap.DeleteArrayElementAtIndex(i);

                // 子资产也要一起清掉，否则 .asset 里会残留孤儿对象
                if (feature != null && EditorUtility.IsPersistent(feature))
                    AssetDatabase.RemoveObjectFromAsset(feature);

                changed = true;
            }

            if (!changed)
                return false;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
            return true;
        }

        /// <summary>工程自己的 Renderer（Assets/ 下），安装只动这些。</summary>
        private static List<ScriptableRendererData> CollectProjectRendererData()
        {
            return CollectAllRendererData(includePackages: false);
        }

        private static List<ScriptableRendererData> CollectAllRendererData(bool includePackages)
        {
            var result = new List<ScriptableRendererData>();
            string[] guids = AssetDatabase.FindAssets("t:ScriptableRendererData");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // 包里的资产是只读的，改它 Unity 会告警，而且升级就丢
                if (!includePackages && path.StartsWith("Packages/"))
                    continue;

                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (data != null)
                    result.Add(data);
            }

            return result;
        }

        private static bool HasFeature(ScriptableRendererData data)
        {
            foreach (ScriptableRendererFeature feature in data.rendererFeatures)
            {
                if (feature is BubbleSDFRenderFeature)
                    return true;
            }

            return false;
        }
    }
}

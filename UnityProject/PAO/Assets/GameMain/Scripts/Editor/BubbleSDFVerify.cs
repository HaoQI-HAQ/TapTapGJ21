using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PAO.BubbleFX.EditorTools
{
    /// <summary>
    /// 仅供命令行自检用：
    ///   Unity.exe -batchmode -nographics -projectPath &lt;proj&gt;
    ///             -executeMethod PAO.BubbleFX.EditorTools.BubbleSDFVerify.Run -quit
    ///
    /// 检查 Shader 能否编译、结构体 stride 对不对、Renderer 装配是否就绪。
    /// 退出码 0 = 全过，1 = 有问题。日常开发用不到，留着当回归哨兵。
    ///
    /// 注意：这里刻意不引用 ShaderCompilerMessageSeverity 枚举 ——
    /// 它的命名空间在各个 Unity 版本间不保证稳定，改成读 ToString() 更耐造。
    /// </summary>
    public static class BubbleSDFVerify
    {
        /// <summary>严重到必须让自检失败的关键词。</summary>
        private static readonly string[] s_FatalKeywords = { "error", "错误" };

        public static void Run()
        {
            var log = new StringBuilder();
            int failures = 0;

            // ---- 1. Shader 能否找到 + 编译 ----
            Shader shader = Shader.Find("PAO/BubbleSDF");
            if (shader == null)
            {
                log.AppendLine("FAIL  Shader PAO/BubbleSDF not found");
                failures++;
            }
            else
            {
                log.AppendLine("OK    Shader PAO/BubbleSDF found (isSupported=" + shader.isSupported + ")");

                ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
                if (messages == null || messages.Length == 0)
                {
                    log.AppendLine("OK    Shader compiled with no errors/warnings");
                }
                else
                {
                    foreach (ShaderMessage m in messages)
                    {
                        string severity = m.severity.ToString();
                        log.AppendLine("SHADER [" + severity + "] " + m.message +
                                       "  @ " + m.file + ":" + m.line);

                        string lower = severity.ToLowerInvariant();
                        for (int i = 0; i < s_FatalKeywords.Length; i++)
                        {
                            if (lower.Contains(s_FatalKeywords[i]))
                            {
                                failures++;
                                break;
                            }
                        }
                    }
                }

                int passCount = shader.passCount;
                if (passCount < 2)
                {
                    log.AppendLine("FAIL  pass count = " + passCount + " (expected 2)");
                    failures++;
                }
                else
                {
                    log.AppendLine("OK    pass count = " + passCount);
                }
            }

            // ---- 2. 结构体布局自检 ----
            // 直接调 BubblePayload 自己的校验，字段个数与大小一起查。
            if (BubblePayload.ValidateLayout(out int sizeBytes, out string layoutError))
            {
                log.AppendLine("OK    BubblePayload layout: " + BubblePayload.FieldCount +
                               " fields, " + sizeBytes + " bytes");
            }
            else
            {
                log.AppendLine("FAIL  " + layoutError);
                failures++;
            }

            // ---- 3. Renderer 装配情况（仅信息，不算失败） ----
            int rendererCount = 0;
            int installed = 0;
            string[] guids = AssetDatabase.FindAssets("t:ScriptableRendererData");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (data == null)
                    continue;

                rendererCount++;

                foreach (ScriptableRendererFeature f in data.rendererFeatures)
                {
                    if (f is BubbleSDFRenderFeature)
                    {
                        installed++;
                        break;
                    }
                }
            }

            log.AppendLine("INFO  renderers=" + rendererCount + "  withBubbleFeature=" + installed);

            log.AppendLine();
            log.AppendLine(failures == 0 ? "===VERIFY PASS===" : "===VERIFY FAIL(" + failures + ")===");

            Debug.Log("[BubbleSDFVerify]\n" + log);

            if (Application.isBatchMode)
                EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }
}

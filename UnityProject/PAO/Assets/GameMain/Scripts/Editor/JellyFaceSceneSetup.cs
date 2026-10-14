// ---------------------------------------------------------------------------
// PAO / JellyFaceSceneSetup —— 水母表情系统的一键落地 + 自检
//
// 菜单：
//   PAO ▸ 水母表情 ▸ ① 一键落地（导入设置 + 动画名 + 材质 + 挂脚本）
//   PAO ▸ 水母表情 ▸ ② 自检（只读，只打日志，不改任何东西）
//
// 为什么需要它：
//   Blender 导出的 FBX，动画 take 名是「骨架.005|Idle」这种带对象名前缀的形式，
//   这个只能在 Unity 的 ModelImporter 里改 —— 脚本不方便手点，所以做成菜单。
//
// 安全约定：
//   * 只改"看起来就是本系统该管的"那些东西，其它一律不碰；
//   * ② 自检完全只读，随时可以先跑一遍看现状；
//   * 所有改动都会 Debug.Log 出来，出错会明确告诉你是哪一步。
// ---------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PAO.EditorTools
{
    public static class JellyFaceSceneSetup
    {
        private const string ShaderName = "PAO/ModelPBRExpression";
        private const string PatchObjectName = "jellyfish_face_patch";

        /// <summary>本系统认识的 5 段动画（用于认出 FBX，以及给 clip 起干净名字）。</summary>
        private static readonly string[] KnownClips =
            { "Idle", "Move", "AirLoop", "JumpStart", "Land" };

        // ==================================================================
        // ① 一键落地
        // ==================================================================
        [MenuItem("PAO/水母表情/① 一键落地（动画名 + 材质 + 挂脚本）", false, 1)]
        public static void RunAll()
        {
            var fbx = FindJellyFbx();
            if (fbx == null)
            {
                EditorUtility.DisplayDialog("找不到水母 FBX",
                    "在工程里没找到含 " + string.Join("/", KnownClips) + " 动画的 FBX。\n" +
                    "先把 shuimu_unity_5animations_face.fbx 拖进 Art/Models/最终版模型/ 再跑这个。", "知道了");
                return;
            }
            Debug.Log($"[JellyFace] 认到模型：{fbx}");

            RenameClips(fbx);
            EnsureAvatar(fbx);
            JellyFaceMaterialGenerator.Generate();
            JellyFaceMaterialGenerator.AssignToSwitcher();
            AutoAttachToScene();
            Verify();
        }

        // ==================================================================
        // ② 自检（只读）
        // ==================================================================
        [MenuItem("PAO/水母表情/② 自检（只读）", false, 2)]
        public static void VerifyMenu() => Verify();

        // ------------------------------------------------------------------
        // 找 FBX：拿每个 .fbx 的导入产物，看有没有 Idle/Move/... 这些 clip
        // ------------------------------------------------------------------
        private static string FindJellyFbx()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;

                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__")).ToArray();
                if (clips.Length == 0) continue;

                // 任一 clip 名以已知动画结尾，就认为是水母模型
                if (clips.Any(c => KnownClips.Any(k => c.name == k || c.name.EndsWith("|" + k))))
                    return path;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // 把 clip 名洗成 Idle / Move / ...（去掉 骨架.005| 这类前缀）
        // ------------------------------------------------------------------
        private static void RenameClips(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) { Debug.LogWarning("[JellyFace] 不是 ModelImporter，跳过改名。"); return; }

            var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();

            var so = new SerializedObject(importer);
            var list = so.FindProperty("m_ClipAnimations");
            if (list == null) { Debug.LogWarning("[JellyFace] 拿不到 m_ClipAnimations，跳过改名。"); return; }

            bool changed = false;
            for (int i = 0; i < list.arraySize && i < clips.Length; i++)
            {
                var el = list.GetArrayElementAtIndex(i);
                var nameProp = el.FindPropertyRelative("name");
                var takeProp = el.FindPropertyRelative("takeName");
                string raw = takeProp != null ? takeProp.stringValue : clips[i].name;

                string clean = CleanName(raw);
                if (string.IsNullOrEmpty(clean)) continue;
                if (nameProp.stringValue == clean) continue;

                Debug.Log($"[JellyFace] clip 改名：\"{raw}\" → \"{clean}\"");
                nameProp.stringValue = clean;
                changed = true;
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                importer.SaveAndReimport();
            }
            else
            {
                Debug.Log("[JellyFace] clip 名已经是干净的，无需改。");
            }
        }

        /// <summary>「骨架.005|骨架.005|Idle」→「Idle」。</summary>
        private static string CleanName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            var parts = raw.Split('|');
            string last = parts[parts.Length - 1].Trim();
            return string.IsNullOrEmpty(last) ? raw : last;
        }

        // ------------------------------------------------------------------
        private static void EnsureAvatar(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                Debug.Log($"[JellyFace] Animation Type 从 {importer.animationType} 改成 Generic。");
                importer.animationType = ModelImporterAnimationType.Generic;
            }
            if (!importer.importAnimation)
            {
                Debug.Log("[JellyFace] 打开 Import Animation。");
                importer.importAnimation = true;
            }
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------
        // 场景里挂脚本（只在没挂的时候挂，不覆盖已有设置）
        // ------------------------------------------------------------------
        private static void AutoAttachToScene()
        {
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => r.sharedMaterials.Any(m =>
                    m != null && m.shader != null && m.shader.name == ShaderName))
                .ToArray();

            if (renderers.Length == 0)
            {
                Debug.Log("[JellyFace] 场景里还没有用到表情材质的水母实例（正常：还没把模型拖进场景）。");
                return;
            }

            var roots = new HashSet<GameObject>();
            foreach (var r in renderers)
            {
                var go = r.transform.root.gameObject;
                roots.Add(go);
            }
            foreach (var go in roots)
            {
                if (go.GetComponent<JellyFaceSwitcher>() != null) continue;
                var faceRenderer = renderers.First(r => r.transform.root.gameObject == go);
                var sw = go.AddComponent<JellyFaceSwitcher>();
                sw.faceRenderer = faceRenderer;
                sw.faceMaterialIndex = System.Array.FindIndex(faceRenderer.sharedMaterials,
                    m => m != null && m.shader != null && m.shader.name == ShaderName);
                if (sw.faceMaterialIndex < 0) sw.faceMaterialIndex = 0;
                EditorUtility.SetDirty(sw);
                Debug.Log($"[JellyFace] 已给 {go.name} 挂上 JellyFaceSwitcher" +
                          $"（面片渲染器={faceRenderer.name}，槽位={sw.faceMaterialIndex}）。");
            }
            if (roots.Count > 0) AssetDatabase.SaveAssets();
        }

        // ==================================================================
        // 自检
        // ==================================================================
        private static void Verify()
        {
            var report = new List<string> { "===== 水母表情系统 自检 =====" };
            bool allOk = true;

            // 1) shader
            var shader = Shader.Find(ShaderName);
            report.Add(shader != null
                ? $"✅ Shader {ShaderName} 已找到"
                : $"❌ Shader {ShaderName} 没找到 —— 检查 ModelPBRExpression.shader 是否有编译错误");
            allOk &= shader != null;

            // 2) 贴图
            var texGuids = AssetDatabase.FindAssets("t:Texture2D",
                new[] { "Assets/GameMain/Art/Textures/低面/水母头身/表情" });
            report.Add(texGuids.Length >= 8
                ? $"✅ 表情贴图 {texGuids.Length} 张"
                : $"❌ 表情贴图只有 {texGuids.Length} 张（应有 8 张）");
            allOk &= texGuids.Length >= 8;

            // 3) 材质
            var matGuids = AssetDatabase.FindAssets("t:Material",
                new[] { "Assets/GameMain/Art/Materials/JellyFace" });
            var mats = matGuids.Select(AssetDatabase.GUIDToAssetPath)
                               .Select(AssetDatabase.LoadAssetAtPath<Material>)
                               .Where(m => m != null).ToArray();
            if (mats.Length == 0)
            {
                report.Add("⚠️  还没有表情材质 —— 跑一次「① 一键落地」");
            }
            else
            {
                report.Add($"✅ 表情材质 {mats.Length} 个");
                foreach (var m in mats)
                {
                    bool shaderOk = m.shader != null && m.shader.name == ShaderName;
                    bool exprOk = m.GetTexture("_ExpressionMap") != null;
                    bool baseOk = m.GetTexture("_BaseMap") != null;
                    if (!shaderOk || !exprOk || !baseOk)
                    {
                        allOk = false;
                        report.Add($"   ❌ {m.name}: shader={(m.shader ? m.shader.name : "null")} " +
                                   $"表情图={(exprOk ? "有" : "缺")} 底色图={(baseOk ? "有" : "缺")}");
                    }
                }
            }

            // 4) FBX / 动画
            var fbx = FindJellyFbx();
            if (fbx == null)
            {
                report.Add("❌ 没找到水母 FBX");
                allOk = false;
            }
            else
            {
                var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__"))
                    .Select(c => c.name).ToArray();
                report.Add($"✅ FBX {fbx}");
                report.Add($"   动画 clip（{clips.Length}）：{string.Join(", ", clips)}");
                bool clean = clips.All(c => !c.Contains("|"));
                if (!clean) { allOk = false; report.Add("   ❌ clip 名还带 | 前缀 —— 跑一次「① 一键落地」"); }

                var models = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<GameObject>().ToArray();
                var patch = models.FirstOrDefault(g => g.name == PatchObjectName)
                            ?? models.FirstOrDefault(g => g.name.StartsWith(PatchObjectName));
                if (patch == null)
                {
                    allOk = false;
                    report.Add($"   ❌ FBX 里没有脸部面片对象 {PatchObjectName} —— 用错 FBX 了？");
                }
                else
                {
                    var mf = patch.GetComponent<MeshFilter>();
                    var smr = patch.GetComponent<SkinnedMeshRenderer>();
                    var mesh = mf != null ? mf.sharedMesh : (smr != null ? smr.sharedMesh : null);
                    if (mesh == null) { allOk = false; report.Add("   ❌ 面片上没有 Mesh"); }
                    else
                    {
                        report.Add($"   ✅ 面片 mesh：{mesh.name}，{mesh.subMeshCount} 个子网格，" +
                                   $"UV 通道 {mesh.uv2.Length switch { 0 => "只有 UV0（❌ 少了 UV1）", _ => "UV0 + UV1 ✔" }}");
                        if (mesh.uv2.Length == 0) allOk = false;
                    }
                }
            }

            // 5) 场景
            var switchers = Object.FindObjectsByType<JellyFaceSwitcher>(FindObjectsSortMode.None);
            if (switchers.Length == 0)
                report.Add("⚠️  场景里没有 JellyFaceSwitcher（还没把水母放进场景）");
            else
                foreach (var s in switchers)
                {
                    int filled = s.expressions != null ? s.expressions.Count(m => m != null) : 0;
                    report.Add($"✅ Switcher @ {s.gameObject.name}：{filled} 个材质，" +
                               $"目标渲染器={(s.faceRenderer ? s.faceRenderer.name : "未指定")}，" +
                               $"槽位={s.faceMaterialIndex}，当前表情={s.Current}");
                    if (filled == 0 || s.faceRenderer == null) allOk = false;
                }

            report.Add(allOk ? "===== 结论：全部通过 =====" : "===== 结论：有项目未通过，见上面 ❌ =====");
            Debug.Log(string.Join("\n", report));
        }
    }
}

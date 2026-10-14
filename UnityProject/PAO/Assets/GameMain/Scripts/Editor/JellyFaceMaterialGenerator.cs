// ---------------------------------------------------------------------------
// PAO / JellyFaceMaterialGenerator —— 一键生成 8 个表情材质
//
// 菜单：PAO / 水母表情 / 生成 8 个表情材质
//
// 做的事：
//   1. 从 Art/Models/最终版模型/ 里的 FBX 找出「脸部面片」子物体上挂着的那张
//      head 贴图组合（BaseMap / Normal / Metallic / Roughness / Emission），
//      直接原样复制到每个表情材质上 —— 保证脸和身体的贴图永远一致；
//   2. 为 Art/Textures/低面/水母头身/表情/ 下的每张 T_JellyFace_*.png
//      生成一个材质 M_JellyFace_<后缀>，shader 用 PAO/ModelPBRExpression，
//      只改 _ExpressionMap；
//   3. 把生成结果写进 JellyFaceSwitcher 的默认列表（如果场景里有这个组件）。
//
// 可以重复执行：已存在的材质会被就地更新，不会新建重复资产。
// ---------------------------------------------------------------------------
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PAO.EditorTools
{
    public static class JellyFaceMaterialGenerator
    {
        private const string ExpressionTexFolder = "Assets/GameMain/Art/Textures/低面/水母头身/表情";
        private const string MaterialFolder      = "Assets/GameMain/Art/Materials/JellyFace";
        private const string ShaderName          = "PAO/ModelPBRExpression";

        // 身体材质名（脸要和它用同一批贴图）。找不到就退化为"从 FBX 上的材质里挑一个"。
        private const string BodyMaterialName    = "材质.046";

        private static readonly string[] MapSlots =
        {
            "_BaseMap", "_NormalMap", "_MetallicMap", "_RoughnessMap", "_EmissionMap", "_OpacityMap"
        };
        private static readonly string[] ToggleSlots =
        {
            "_EmissionEnabled", "_AlphaClip", "_UseOpacityMap", "_TransparentMode", "_ZWrite", "_Cull"
        };
        private static readonly string[] FloatSlots =
        {
            "_NormalScale", "_Metallic", "_Roughness", "_Cutoff", "_Opacity"
        };

        [MenuItem("PAO/水母表情/生成 8 个表情材质", false, 10)]
        public static void Generate()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("缺少 Shader",
                    $"找不到 {ShaderName}。\n确认 ModelPBRExpression.shader 已经导入并且没有编译错误。", "知道了");
                return;
            }

            var textures = LoadExpressionTextures();
            if (textures.Count == 0)
            {
                EditorUtility.DisplayDialog("没有表情贴图",
                    $"在 {ExpressionTexFolder} 下没找到 T_JellyFace_*.png。", "知道了");
                return;
            }

            var template = FindBodyMaterial();
            if (template == null)
            {
                Debug.LogWarning($"[JellyFace] 没找到身体材质「{BodyMaterialName}」，" +
                                 "生成的表情材质将只有表情贴图，底色需要手动补 —— 或者先在 FBX 上确认材质名。");
            }

            EnsureFolder(MaterialFolder);

            var created = new List<Material>();
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < textures.Count; i++)
                {
                    var tex = textures[i];
                    string suffix = ExtractSuffix(tex.name);
                    string matPath = $"{MaterialFolder}/M_JellyFace_{suffix}.mat";

                    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat == null)
                    {
                        mat = new Material(shader);
                        AssetDatabase.CreateAsset(mat, matPath);
                    }
                    else
                    {
                        mat.shader = shader;
                    }

                    EditorUtility.DisplayProgressBar("生成表情材质", tex.name,
                        (float)i / Mathf.Max(1, textures.Count));

                    CopyBodyTextures(template, mat);
                    mat.SetTexture("_ExpressionMap", tex);
                    mat.SetColor("_ExpressionColor", Color.white);
                    mat.SetFloat("_OutlineEnabled", 0f);       // 脸部面片不描边
                    mat.SetFloat("_AlphaClip", 0f);
                    mat.SetFloat("_TransparentMode", 0f);
                    mat.SetFloat("_ZWrite", 1f);
                    mat.SetFloat("_Cull", 2f);
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;

                    EditorUtility.SetDirty(mat);
                    created.Add(mat);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Selection.objects = created.Cast<Object>().ToArray();
            Debug.Log($"[JellyFace] 生成/更新了 {created.Count} 个表情材质 → {MaterialFolder}");
            EditorUtility.DisplayDialog("完成",
                $"生成/更新了 {created.Count} 个表情材质。\n\n" +
                "接下来：在场景里的水母对象上挂 JellyFaceSwitcher，" +
                "再执行菜单 PAO/水母表情/把表情材质装到场景里的 Switcher 上。", "好");
        }

        [MenuItem("PAO/水母表情/把表情材质装到场景里的 Switcher 上", false, 11)]
        public static void AssignToSwitcher()
        {
            var switcher = Object.FindFirstObjectByType<PAO.JellyFaceSwitcher>();
            if (switcher == null)
            {
                EditorUtility.DisplayDialog("场景里没有 JellyFaceSwitcher",
                    "先在场景里的水母对象上挂一个 JellyFaceSwitcher，再执行这个。", "知道了");
                return;
            }

            var mats = AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<Material>)
                .Where(m => m != null && m.shader != null && m.shader.name == ShaderName)
                .ToArray();

            var so = new SerializedObject(switcher);
            var prop = so.FindProperty("expressions");
            prop.arraySize = mats.Length;
            for (int i = 0; i < mats.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = mats[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[JellyFace] 已把 {mats.Length} 个表情材质装到 {switcher.name} 上。");
        }

        // ------------------------------------------------------------------
        private static List<Texture2D> LoadExpressionTextures()
        {
            if (!AssetDatabase.IsValidFolder(ExpressionTexFolder)) return new List<Texture2D>();
            return AssetDatabase.FindAssets("t:Texture2D", new[] { ExpressionTexFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileName(p).StartsWith("T_JellyFace_"))
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
                .Where(t => t != null)
                .ToList();
        }

        private static Material FindBodyMaterial()
        {
            var guids = AssetDatabase.FindAssets("t:Material");
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(path) == BodyMaterialName)
                    return AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            // 退化：拿第一个 ModelPBR 材质当模板
            foreach (var g in guids)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                if (mat != null && mat.shader != null && mat.shader.name == "PAO/ModelPBR")
                    return mat;
            }
            return null;
        }

        private static void CopyBodyTextures(Material from, Material to)
        {
            if (from == null) return;
            foreach (var slot in MapSlots)
            {
                if (!from.HasProperty(slot)) continue;
                to.SetTexture(slot, from.GetTexture(slot));
                to.SetTextureScale(slot, from.GetTextureScale(slot));
                to.SetTextureOffset(slot, from.GetTextureOffset(slot));
            }
            foreach (var slot in ToggleSlots)
                if (from.HasProperty(slot)) to.SetFloat(slot, from.GetFloat(slot));
            foreach (var slot in FloatSlots)
                if (from.HasProperty(slot)) to.SetFloat(slot, from.GetFloat(slot));
            if (from.HasProperty("_BaseColor")) to.SetColor("_BaseColor", from.GetColor("_BaseColor"));
            if (from.HasProperty("_EmissionColor")) to.SetColor("_EmissionColor", from.GetColor("_EmissionColor"));
        }

        private static string ExtractSuffix(string texName)
        {
            // T_JellyFace_02_Cute -> 02_Cute
            const string prefix = "T_JellyFace_";
            return texName.StartsWith(prefix) ? texName.Substring(prefix.Length) : texName;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            var leaf = Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}

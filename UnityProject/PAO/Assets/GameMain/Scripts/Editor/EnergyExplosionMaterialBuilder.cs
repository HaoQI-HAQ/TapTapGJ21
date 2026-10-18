using UnityEditor;
using UnityEngine;

namespace PAO.EditorTools
{
    /// <summary>
    /// 生成"能量爆炸"用的粒子材质资产。
    ///
    /// 【为什么需要这一步】
    /// 爆炸特效是**运行时构建**的（见 EnergyExplosionVFX）。
    /// 运行时拿不到贴图 —— Resources.Load 只认 Resources 目录，
    /// 而贴图放在 Assets/GameMain/Art/Textures/EnergyFX/ 下。
    ///
    /// 所以在这里把"贴图 + 着色器 + 参数"烤成 .mat 资产放进 Resources，
    /// 运行时用 Resources.Load&lt;Material&gt; 取。
    ///
    /// 【配色：跟炸弹泡泡走】
    /// 主色取 BubbleTypeSwitcher.ColorOf(BubbleType.Bomb) = (1.00, 0.62, 0.42)。
    /// 这是游戏里真正显示的颜色（HUD 三个圈、发射器上色都用它）。
    /// 早先这里写死的是冰蓝色，炸开时和泡泡对不上 —— 玩家看不出是同一颗。
    ///
    /// 菜单：Tools → PAO → 特效 → 生成能量爆炸材质
    /// </summary>
    public static class EnergyExplosionMaterialBuilder
    {
        private const string kTexFolder  = "Assets/GameMain/Art/Textures/EnergyFX/";
        private const string kMatFolder  = "Assets/GameMain/Resources/PAO/EnergyFX";
        private const string kShaderName = "PAO/EnergyParticle";

        private struct Recipe
        {
            public string name;
            public string texture;
            public Color tint;
            public float brightness;
            public float cols;
            public float rows;
            public float frame;

            public Recipe(string n, string tex, Color t, float b, float c = 1f, float r = 1f, float f = 0f)
            {
                name = n; texture = tex; tint = t; brightness = b;
                cols = c; rows = r; frame = f;
            }
        }

        [MenuItem("Tools/PAO/特效/生成能量爆炸材质", priority = 87)]
        public static void BuildMaterials()
        {
            Shader shader = Shader.Find(kShaderName);

            if (shader == null)
            {
                EditorUtility.DisplayDialog("能量爆炸材质",
                    "找不到着色器 " + kShaderName + "。\n\n" +
                    "请确认 Assets/GameMain/Art/Shaders/EnergyParticle.shader 存在，\n" +
                    "并且 Console 里没有它的编译错误。", "好");
                return;
            }

            EnsureFolder();

            // ---- 配色：从炸弹泡泡的类型色推出来 ----
            Color main = BubbleTypeSwitcher.ColorOf(BubbleType.Bomb);        // (1.00, 0.62, 0.42)
            Color hot  = Color.Lerp(main, Color.white, 0.70f);               // 过曝核心
            Color deep = new Color(main.r * 0.45f, main.g * 0.30f, main.b * 0.22f, 1f);  // 暗部

            var recipes = new[]
            {
                // 核心光团：用最亮的那一档，撑开时才有"过曝"感
                new Recipe("M_EnergyCoreFlash", "nova_6x3_spritesheet", hot,        3.2f, 6f, 3f, 5f),
                // 冲击环：用主色
                new Recipe("M_EnergyRing",      "ring",                 main,       2.6f),
                // 光条：主色偏亮一点，不然细线太暗看不见
                new Recipe("M_EnergyRay",       "sparkle",              hot,        3.0f),
                // 余烬：用主色
                new Recipe("M_EnergyEmber",     "glow1",                main,       2.0f),
                // 备用
                new Recipe("M_EnergyWave",      "wave",                 main,       2.2f),
                new Recipe("M_EnergyCircle",    "circle",               deep,       2.4f),
            };

            int made = 0;
            int skipped = 0;

            for (int i = 0; i < recipes.Length; i++)
            {
                Recipe r = recipes[i];
                string matPath = kMatFolder + "/" + r.name + ".mat";
                string texPath = kTexFolder + r.texture + ".png";

                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

                if (tex == null)
                {
                    Debug.LogWarning("[EnergyExplosionMaterialBuilder] 找不到贴图 " + texPath +
                                     "，跳过 " + r.name);
                    skipped++;
                    continue;
                }

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                else
                {
                    mat.shader = shader;
                }

                mat.SetTexture("_MainTex", tex);
                SetColor(mat, "_Tint", r.tint);
                SetFloat(mat, "_Brightness", r.brightness);
                SetColor(mat, "_Color", Color.white);
                SetFloat(mat, "_SheetCols", r.cols);
                SetFloat(mat, "_SheetRows", r.rows);
                SetFloat(mat, "_SheetFrame", r.frame);
                // 精灵表每帧都是方格硬切的，边缘要淡出，否则会看到方框
                SetFloat(mat, "_EdgeFade", (r.cols > 1f || r.rows > 1f) ? 0.22f : 0.32f);

                EditorUtility.SetDirty(mat);
                made++;

                Debug.Log("[EnergyExplosionMaterialBuilder] ✓ " + r.name +
                          "  颜色 " + r.tint + "  贴图 " + r.texture);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[EnergyExplosionMaterialBuilder] 完成：生成/更新 " + made +
                      " 个材质，跳过 " + skipped + " 个。主色 " + main);

            EditorUtility.DisplayDialog("能量爆炸材质",
                "生成/更新 " + made + " 个材质。\n跳过 " + skipped + " 个（贴图缺失）。\n\n" +
                "主色取自炸弹泡泡的类型色：" + main + "\n\n" +
                "路径：\n" + kMatFolder,
                "好");
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/GameMain/Resources"))
            {
                AssetDatabase.CreateFolder("Assets/GameMain", "Resources");
            }

            if (!AssetDatabase.IsValidFolder("Assets/GameMain/Resources/PAO"))
            {
                AssetDatabase.CreateFolder("Assets/GameMain/Resources", "PAO");
            }

            if (!AssetDatabase.IsValidFolder(kMatFolder))
            {
                AssetDatabase.CreateFolder("Assets/GameMain/Resources/PAO", "EnergyFX");
            }
        }

        private static void SetColor(Material m, string n, Color v)
        {
            if (m.HasProperty(n)) { m.SetColor(n, v); }
        }

        private static void SetFloat(Material m, string n, float v)
        {
            if (m.HasProperty(n)) { m.SetFloat(n, v); }
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using PAO.Underwater;

namespace PAO.Underwater.EditorTools
{
    /// <summary>
    /// 水面顶盖的摆放工具。
    ///
    /// 尺寸和高度不是写死的数字，而是点菜单时按当前场景里所有 Renderer 的真实世界包围盒算出来的：
    ///   高度 = 最高点 + Clearance（默认 12 米）
    ///   半边长 = 平面最大半径 × 1.4 + 160（保证从关卡里任何位置抬头，网格边缘都远在掠射角上）
    /// 所以关卡换了、白盒重跑过，再点一次就行。
    ///
    /// 菜单：
    ///   Tools/PAO/水下/为当前场景生成水面顶盖
    ///   Tools/PAO/水下/为 Map2–Map5 全部生成水面顶盖
    ///   Tools/PAO/水下/清理旧的 CartoonWater 水面（Map4 的遗留 Plane）
    /// </summary>
    public static class UnderwaterCeilingSetup
    {
        private const string WaterObjectName = "Water_Ceiling";
        private const string MaterialFolder = "Assets/GameMain/Art/Materials";
        private const string MaterialPath = MaterialFolder + "/M_UnderwaterCeiling.mat";
        private const string ShaderName = "PAO/UnderwaterCeiling";

        // 水面比关卡最高点再高一点，避免珊瑚尖刚好顶穿水面
        private const float Clearance = 12f;

        // Map4 里那两块倒过来的 Plane 用的就是这个材质。算包围盒时必须排掉，
        // 否则它们会把"关卡最高点"抬到 61 米，水面被顶到七十多米去。
        private const string LegacyWaterMaterialPath = "Assets/CartoonWater/Materials/CartoonWater.mat";

        private static readonly string[] Maps =
        {
            "Assets/GameMain/Scenes/Map2.unity",
            "Assets/GameMain/Scenes/Map3.unity",
            "Assets/GameMain/Scenes/Map4.unity",
            "Assets/GameMain/Scenes/Map5.unity",
        };

        private static Material s_LegacyWaterMaterial;

        [MenuItem("Tools/PAO/水下/为当前场景生成水面顶盖", priority = 90)]
        public static void BuildCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[PAO 水面] 没有打开的场景。");
                return;
            }

            if (!BuildForScene(scene, removeLegacy: true))
                return;

            Debug.Log("[PAO 水面] " + scene.name + " 完成。");
        }

        [MenuItem("Tools/PAO/水下/为 Map2–Map5 全部生成水面顶盖", priority = 91)]
        public static void BuildAllMaps()
        {
            // 一口气改五个场景，先让用户把当前未保存的东西处理掉
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            string startScene = SceneManager.GetActiveScene().path;

            foreach (string path in Maps)
            {
                if (!System.IO.File.Exists(path))
                {
                    Debug.LogWarning("[PAO 水面] 场景不存在，跳过：" + path);
                    continue;
                }

                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                BuildForScene(scene, removeLegacy: true);
            }

            // 尽量回到用户原来打开的场景
            if (!string.IsNullOrEmpty(startScene) && System.IO.File.Exists(startScene))
                EditorSceneManager.OpenScene(startScene, OpenSceneMode.Single);

            Debug.Log("[PAO 水面] Map2–Map5 全部完成。");
        }

        [MenuItem("Tools/PAO/水下/清理旧的 CartoonWater 水面（Map4 的遗留 Plane）", priority = 92)]
        public static void RemoveLegacyWaterInCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            int removed = RemoveLegacyWater(scene);
            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            Debug.Log("[PAO 水面] " + scene.name + " 清理掉 " + removed + " 个旧水面对象。");
        }

        private static bool BuildForScene(Scene scene, bool removeLegacy)
        {
            if (removeLegacy)
            {
                int removed = RemoveLegacyWater(scene);
                if (removed > 0)
                    Debug.Log("[PAO 水面] " + scene.name + "：清理掉 " + removed + " 个遗留水面对象。");
            }

            Material material = EnsureMaterial();
            if (material == null)
                return false;

            Bounds bounds;
            int counted;
            if (!TryComputeLevelBounds(scene, out bounds, out counted) || counted == 0)
            {
                Debug.LogWarning("[PAO 水面] " + scene.name + "：没找到任何可用的 Renderer，跳过。");
                return false;
            }

            GameObject water = FindOrCreateWaterObject(scene);
            UnderwaterCeiling ceiling = water.GetComponent<UnderwaterCeiling>();
            if (ceiling == null)
                ceiling = water.AddComponent<UnderwaterCeiling>();

            float waterHeight = bounds.max.y + Clearance;
            float halfSize = Mathf.Max(Mathf.Max(bounds.extents.x, bounds.extents.z) * 1.4f, 120f) + 160f;
            float skirtDepth = Mathf.Clamp(bounds.size.y + 60f, 80f, 400f);

            water.transform.SetPositionAndRotation(
                new Vector3(bounds.center.x, waterHeight, bounds.center.z),
                Quaternion.identity);
            water.transform.localScale = Vector3.one;

            ceiling.Configure(halfSize, skirtDepth);

            MeshRenderer waterRenderer = water.GetComponent<MeshRenderer>();
            if (waterRenderer != null)
            {
                waterRenderer.sharedMaterial = material;
                waterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                waterRenderer.receiveShadows = false;
            }

            // 不加碰撞体：水面是背景，玩家要能从它旁边过去。
            Collider existingCollider = water.GetComponent<Collider>();
            if (existingCollider != null)
                Object.DestroyImmediate(existingCollider);

            EditorUtility.SetDirty(ceiling);
            EditorUtility.SetDirty(water);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(string.Format(
                "[PAO 水面] {0}：统计了 {1} 个 Renderer，关卡最高点 y={2:F2}，" +
                "水面 y={3:F2}（+{4:F1}），半边长 {5:F1} 米（总宽 {6:F1} 米），裙边 {7:F1} 米。",
                scene.name, counted, bounds.max.y, waterHeight, Clearance,
                halfSize, halfSize * 2f, skirtDepth));

            return true;
        }

        private static Material EnsureMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
                return material;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError("[PAO 水面] 找不到 shader \"" + ShaderName +
                               "\"，先确认 Assets/GameMain/Art/Shaders/UnderwaterCeiling.shader 编译没报错。");
                return null;
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/GameMain/Art", "Materials");

            material = new Material(shader) { name = "M_UnderwaterCeiling" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();

            Debug.Log("[PAO 水面] 新建材质 " + MaterialPath);
            return material;
        }

        private static GameObject FindOrCreateWaterObject(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == WaterObjectName)
                    return root;
            }

            var water = new GameObject(WaterObjectName);
            SceneManager.MoveGameObjectToScene(water, scene);
            water.AddComponent<MeshFilter>();
            water.AddComponent<MeshRenderer>();
            return water;
        }

        private static bool TryComputeLevelBounds(Scene scene, out Bounds bounds, out int counted)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            counted = 0;

            bool has = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || !renderer.enabled)
                        continue;

                    if (!renderer.gameObject.activeInHierarchy)
                        continue;

                    // 粒子/拖尾/线段不进包围盒：它们的 bounds 会跳，而且不该决定水面高度
                    if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                        continue;

                    // 水面自己不算
                    if (renderer.GetComponentInParent<UnderwaterCeiling>() != null)
                        continue;

                    // 旧 CartoonWater 水面 / 材质丢失的遗留 Plane 不算（见 IsStrayWaterPlane 注释）
                    if (IsStrayWaterPlane(renderer))
                        continue;

                    Bounds rendererBounds = renderer.bounds;
                    if (rendererBounds.size.sqrMagnitude <= 1e-6f)
                        continue;

                    if (!has)
                    {
                        bounds = rendererBounds;
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(rendererBounds);
                    }

                    counted++;
                }
            }

            return has;
        }

        private static bool IsLegacyWater(Renderer renderer)
        {
            if (s_LegacyWaterMaterial == null)
                s_LegacyWaterMaterial = AssetDatabase.LoadAssetAtPath<Material>(LegacyWaterMaterialPath);

            if (s_LegacyWaterMaterial == null)
                return false;

            return renderer.sharedMaterial == s_LegacyWaterMaterial;
        }

        /// <summary>
        /// 认 Map4 里那两块遗留 Plane：根物体、名字以 Plane 开头、用的是 Unity 内置 Plane 网格，
        /// 而且是旧 CartoonWater 材质或者材质已经丢了（第二块 Plane 的材质 guid 在整个工程里都找不到，
        /// 打开就是品红）。四条同时成立才算，不会误删正常美术资源。
        /// </summary>
        private static bool IsStrayWaterPlane(Renderer renderer)
        {
            GameObject go = renderer.gameObject;
            if (go.transform.parent != null)
                return false;

            if (!go.name.StartsWith("Plane", System.StringComparison.Ordinal))
                return false;

            MeshFilter meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return false;

            if (meshFilter.sharedMesh.name != "Plane")
                return false;

            if (renderer.sharedMaterial == null)
                return true;

            return IsLegacyWater(renderer);
        }

        private static int RemoveLegacyWater(Scene scene)
        {
            int removed = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null)
                    continue;

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || !IsStrayWaterPlane(renderer))
                        continue;

                    GameObject target = renderer.gameObject;
                    if (target.GetComponent<UnderwaterCeiling>() != null)
                        continue;

                    Debug.Log("[PAO 水面] 删除遗留水面对象：" + target.name +
                              "（y=" + target.transform.position.y.ToString("F2") +
                              "，材质=" + (renderer.sharedMaterial == null ? "丢失" : renderer.sharedMaterial.name) + "）");
                    Object.DestroyImmediate(target);
                    removed++;
                }
            }

            return removed;
        }
    }
}

using PAO.AV;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.AV.Editor
{
    /// <summary>Idempotent scene/asset installer for the bus-driven visual layer.</summary>
    public static class PAOVisualInstaller
    {
        private const string Map4Path = "Assets/GameMain/Scenes/Map4.unity";
        private const string MaterialFolder = "Assets/GameMain/Art/Materials/AV";
        private const string ShaderPath = "Assets/GameMain/Art/Shaders/PAOFeedbackVFX.shader";

        [MenuItem("PAO/AV/Install Visual Director/Open Scene")]
        public static void InstallIntoOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[PAO Visual] No loaded active scene to install into.");
                return;
            }
            Install(scene);
        }

        [MenuItem("PAO/AV/Install Visual Director/Map4")]
        public static void InstallMap4()
        {
            Scene scene = SceneManager.GetSceneByPath(Map4Path);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(Map4Path, OpenSceneMode.Additive);
            try
            {
                Install(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void Install(Scene scene)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) shader = Shader.Find("PAO/FeedbackVFX");
            if (shader == null)
            {
                Debug.LogError("[PAO Visual] Missing shader: " + ShaderPath);
                return;
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/GameMain/Art/Materials"))
                    AssetDatabase.CreateFolder("Assets/GameMain/Art", "Materials");
                AssetDatabase.CreateFolder("Assets/GameMain/Art/Materials", "AV");
            }

            Material ring = LoadOrCreateMaterial(MaterialFolder + "/M_PAOFeedbackRing.mat", shader, 1f, 0.12f);
            Material pearl = LoadOrCreateMaterial(MaterialFolder + "/M_PAOFeedbackPearl.mat", shader, 0f, 0.14f);

            PAOVisualDirector director = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                director = root.GetComponentInChildren<PAOVisualDirector>(true);
                if (director != null) break;
            }
            if (director == null)
            {
                GameObject root = new GameObject("PAO Visual Director");
                SceneManager.MoveGameObjectToScene(root, scene);
                director = root.AddComponent<PAOVisualDirector>();
            }

            SerializedObject serialized = new SerializedObject(director);
            serialized.FindProperty("m_RingMaterial").objectReferenceValue = ring;
            serialized.FindProperty("m_PearlMaterial").objectReferenceValue = pearl;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[PAO Visual] Installed in " + scene.path + " with two persistent materials.");
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader, float shape, float softness)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetFloat("_Shape", shape);
            material.SetFloat("_Softness", softness);
            material.SetFloat("_Brightness", shape > 0.5f ? 1.5f : 1.2f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}

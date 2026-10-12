using PAO.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    /// <summary>
    /// 把 Menu 场景现有的设置面板保存成共用预制体，并接入玩法场景的暂停菜单。
    /// 主菜单和暂停菜单因此使用相同的设置内容与视觉结构。
    /// </summary>
    public static class PauseSettingsInstaller
    {
        private const string kMenuScenePath = "Assets/GameMain/UI/Menu.unity";
        private const string kGameplayScenePath = "Assets/Scenes/cs.unity";
        private const string kPrefabPath = "Assets/GameMain/UI/Settings Panel.prefab";
        private const string kRootName = "Settings Panel";

        [MenuItem("PAO/UI/同步设置面板到暂停菜单", false, 26)]
        public static void InstallFromMenu()
        {
            Install();
        }

        private static void Install()
        {
            Scene menuScene = SceneManager.GetSceneByPath(kMenuScenePath);
            bool openedMenu = !menuScene.isLoaded;
            if (openedMenu)
            {
                menuScene = EditorSceneManager.OpenScene(kMenuScenePath, OpenSceneMode.Additive);
            }

            GameObject source = FindInScene(menuScene, kRootName);
            if (source == null)
            {
                if (openedMenu) EditorSceneManager.CloseScene(menuScene, true);
                Debug.LogWarning("[PauseSettingsInstaller] Menu 场景里找不到 Settings Panel。");
                return;
            }

            GameObject clone = Object.Instantiate(source);
            clone.name = kRootName;
            clone.transform.SetParent(null, false);
            ClearMenuButtonReferences(clone.GetComponent<SettingsPanel>());
            clone.SetActive(false);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(clone, kPrefabPath);
            Object.DestroyImmediate(clone);

            if (openedMenu)
            {
                EditorSceneManager.CloseScene(menuScene, true);
            }

            if (prefab == null)
            {
                Debug.LogError("[PauseSettingsInstaller] 设置面板预制体保存失败。");
                return;
            }

            Scene gameplayScene = SceneManager.GetSceneByPath(kGameplayScenePath);
            bool openedGameplay = !gameplayScene.isLoaded;
            if (openedGameplay)
            {
                gameplayScene = EditorSceneManager.OpenScene(kGameplayScenePath, OpenSceneMode.Additive);
            }

            GameObject canvasObject = FindInScene(gameplayScene, "HUD Canvas");
            PauseMenu pauseMenu = FindComponentInScene<PauseMenu>(gameplayScene);
            if (canvasObject == null || pauseMenu == null)
            {
                if (openedGameplay) EditorSceneManager.CloseScene(gameplayScene, true);
                Debug.LogWarning("[PauseSettingsInstaller] 玩法场景缺少 HUD Canvas 或 PauseMenu。");
                return;
            }

            SettingsPanel settings = FindComponentInScene<SettingsPanel>(gameplayScene);
            if (settings == null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasObject.transform);
                instance.name = kRootName;
                instance.SetActive(false);
                settings = instance.GetComponent<SettingsPanel>();
            }

            SerializedObject pauseObject = new SerializedObject(pauseMenu);
            pauseObject.FindProperty("m_SettingsPanel").objectReferenceValue = settings;
            pauseObject.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(gameplayScene);
            EditorSceneManager.SaveScene(gameplayScene);
            AssetDatabase.SaveAssets();

            if (openedGameplay)
            {
                EditorSceneManager.CloseScene(gameplayScene, true);
            }

            Debug.Log("[PauseSettingsInstaller] 已把主菜单设置面板同步到玩法场景的暂停菜单。");
        }

        private static void ClearMenuButtonReferences(SettingsPanel panel)
        {
            if (panel == null) return;
            SerializedObject serialized = new SerializedObject(panel);
            SerializedProperty buttons = serialized.FindProperty("m_MenuButtons");
            if (buttons != null) buttons.arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject FindInScene(Scene scene, string objectName)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform[] children = roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < children.Length; j++)
                {
                    if (children[j].name == objectName) return children[j].gameObject;
                }
            }
            return null;
        }

        private static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T component = roots[i].GetComponentInChildren<T>(true);
                if (component != null) return component;
            }
            return null;
        }
    }
}

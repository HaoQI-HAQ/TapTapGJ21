using System.IO;
using PAO.UI.V2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.EditorTools
{
    public static class UIV2Tools
    {
        public const string PreviewPath = "Assets/GameMain/UI/V2/UI Preview.unity";

        [MenuItem("PAO/UI/V2/打开配置", false, 40)]
        public static void OpenConfig()
        { Selection.activeObject = Resources.Load<UIConfig>("PAOUIConfig"); }

        [MenuItem("PAO/UI/V2/打开 UI 预览场景", false, 41)]
        public static void OpenPreview()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(PreviewPath)) CreatePreview();
            else EditorSceneManager.OpenScene(PreviewPath);
        }

        public static void CreatePreview()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera"; cameraObject.transform.position = new Vector3(0, 4, -8);
            cameraObject.transform.rotation = Quaternion.Euler(15, 0, 0);
            Camera camera = cameraObject.GetComponent<Camera>(); camera.backgroundColor = new Color32(23, 56, 77, 255);
            camera.clearFlags = CameraClearFlags.SolidColor;
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Preview floor"; floor.transform.localScale = new Vector3(30, 1, 30); floor.transform.position = Vector3.down;
            GameObject player = new GameObject("Player", typeof(CharacterController), typeof(PlayerController), typeof(BubbleLauncher));
            player.transform.position = new Vector3(0, 1, 0);
            new GameObject("Bubble state", typeof(BubbleTypeSwitcher));
            new GameObject("UI prototype lab", typeof(UIPrototypeLab));
            Directory.CreateDirectory(Path.GetDirectoryName(PreviewPath));
            EditorSceneManager.SaveScene(scene, PreviewPath);
            AssetDatabase.Refresh();
        }

        [MenuItem("PAO/UI/V2/创建意识碎片触发器", false, 42)]
        public static void CreateMemory()
        {
            var go = new GameObject("Memory trigger"); Undo.RegisterCreatedObjectUndo(go, "Create memory trigger");
            var trigger = go.AddComponent<MemoryTrigger>(); trigger.memory.id = System.Guid.NewGuid().ToString("N");
            Selection.activeGameObject = go;
        }

        [MenuItem("PAO/UI/V2/创建章节终点触发器", false, 43)]
        public static void CreateGoal()
        {
            var go = new GameObject("Chapter goal"); Undo.RegisterCreatedObjectUndo(go, "Create chapter goal");
            go.AddComponent<ChapterGoalTrigger>(); Selection.activeGameObject = go;
        }

        [MenuItem("PAO/UI/V2/创建建构点", false, 44)]
        public static void CreateBuildSite()
        {
            var go = new GameObject("Construction site"); Undo.RegisterCreatedObjectUndo(go, "Create construction site");
            var site = go.AddComponent<BuildSite>(); site.id = System.Guid.NewGuid().ToString("N");
            Selection.activeGameObject = go;
        }
    }
}

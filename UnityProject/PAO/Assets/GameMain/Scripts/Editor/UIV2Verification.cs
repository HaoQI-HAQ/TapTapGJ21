using System.IO;
using PAO.UI.V2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PAO.EditorTools
{
    public static class UIV2Verification
    {
        public const string Requested = "PAO.UI.V2.VerificationRequested";
        public const string Headless = "PAO.UI.V2.VerificationHeadless";

        // Run in an isolated copy via Unity -batchmode -executeMethod ...RunAllCLI.
        public static void RunAllCLI()
        {
            PlayerSettings.companyName = "CodexVerification";
            PlayerSettings.productName = "PAOUIV2Verification";
            UIV2Tools.CreatePreview();
            var scenes = new EditorBuildSettingsScene[8];
            for (int i = 0; i < 6; i++)
            {
                string path = "Assets/Map" + i + ".unity";
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), path, true);
                scenes[i + 1] = new EditorBuildSettingsScene(path, true);
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Main Camera", typeof(Camera)).tag = "MainCamera";
            const string menu = "Assets/Menu.unity";
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), menu);
            scenes[0] = new EditorBuildSettingsScene(menu, true);
            scenes[7] = new EditorBuildSettingsScene(UIV2Tools.PreviewPath, true);
            EditorBuildSettings.scenes = scenes;
            SessionState.SetBool(Requested, true); SessionState.SetBool(Headless, true);
            EditorApplication.isPlaying = true;
        }
    }
}

using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace PAO.EditorTools
{
    public static class UIMenuVerification
    {
        public static void RunCLI()
        {
            if (!File.Exists("PAO_UI_VERIFICATION_PROJECT"))
                throw new System.InvalidOperationException("Menu verification requires an isolated validation project.");
            PlayerSettings.companyName = "CodexVerification";
            PlayerSettings.productName = "PAOMenuDesignVerification";
            typeof(UIV2Verification).GetMethod("RemapCompiledPackageScripts", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            var scenes = new EditorBuildSettingsScene[7];
            const string menu = "Assets/GameMain/UI/Menu.unity";
            scenes[0] = new EditorBuildSettingsScene(menu, true);
            for (int i = 0; i < 6; i++) scenes[i + 1] = new EditorBuildSettingsScene("Assets/GameMain/Scenes/Map" + i + ".unity", true);
            EditorBuildSettings.scenes = scenes;
            EditorSceneManager.OpenScene(menu);
            SessionState.SetBool("PAO.UI.MenuVerification", true);
            EditorApplication.isPlaying = true;
        }
    }
}

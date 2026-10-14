using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
            RequireVerificationProject();
            PlayerSettings.companyName = "CodexVerification";
            PlayerSettings.productName = "PAOUIV2Verification";
            RemapCompiledPackageScripts();
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

        public static void RunMechanicsCLI()
        {
            RequireVerificationProject();
            PlayerSettings.companyName = "CodexVerification";
            PlayerSettings.productName = "PAOMechanicsVerification";
            RemapCompiledPackageScripts();
            EditorBuildSettings.scenes = new[] {
                new EditorBuildSettingsScene("Assets/Scenes/cs.unity", true),
                new EditorBuildSettingsScene("Assets/GameMain/Scenes/Map5.unity", true) };
            EditorSceneManager.OpenScene("Assets/Scenes/cs.unity");
            SessionState.SetBool(Requested, true); SessionState.SetBool(Headless, true);
            SessionState.SetBool("PAO.UI.V2.VerificationMechanics", true);
            EditorApplication.isPlaying = true;
        }

        public static void RunFormalCLI()
        {
            RequireVerificationProject();
            PlayerSettings.companyName = "CodexVerification";
            PlayerSettings.productName = "PAOUIV2FormalVerification";
            RemapCompiledPackageScripts();
            var scenes = new List<EditorBuildSettingsScene>();
            for (int i = 0; i < 6; i++)
                scenes.Add(new EditorBuildSettingsScene("Assets/GameMain/Scenes/Map" + i + ".unity", true));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Main Camera", typeof(Camera)).tag = "MainCamera";
            const string menu = "Assets/Formal/Menu.unity";
            Directory.CreateDirectory("Assets/Formal");
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), menu);
            scenes.Insert(0, new EditorBuildSettingsScene(menu, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            SessionState.SetBool(Requested, true); SessionState.SetBool(Headless, true);
            SessionState.SetBool("PAO.UI.V2.VerificationFormal", true);
            EditorApplication.isPlaying = true;
        }

        private static void RequireVerificationProject()
        {
            if (!File.Exists(Path.Combine(Application.dataPath, "../PAO_UI_VERIFICATION_PROJECT")))
                throw new System.InvalidOperationException("UI verification must run in the isolated project prepared by Tools/UIV2/prepare_validation.py.");
        }

        // Only isolated test copies use compiled package DLLs instead of source packages.
        private static void RemapCompiledPackageScripts()
        {
            const string mapping = "PackageScriptGuids.txt";
            if (!File.Exists(mapping)) return;
            var references = new Dictionary<string, string>();
            foreach (string dll in Directory.GetFiles("Assets/Plugins/VerificationDependencies", "*.dll"))
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(dll.Replace('\\', '/')))
                    if (asset is MonoScript script && script.GetClass() != null &&
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId))
                        references[script.GetClass().Name] = "{fileID: " + localId + ", guid: " + guid + ", type: 3}";
            var substitutions = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(mapping))
            {
                string[] pair = line.Split(' ');
                if (pair.Length == 2 && references.TryGetValue(pair[1], out string reference)) substitutions[pair[0]] = reference;
            }
            foreach (string file in Directory.GetFiles("Assets", "*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".unity") && !file.EndsWith(".prefab") && !file.EndsWith(".asset")) continue;
                string content = File.ReadAllText(file);
                string replaced = Regex.Replace(content, @"\{fileID: 11500000, guid: ([a-f0-9]{32}), type: 3\}",
                    match => substitutions.TryGetValue(match.Groups[1].Value, out string reference) ? reference : match.Value);
                if (content != replaced) File.WriteAllText(file, replaced);
            }
            AssetDatabase.Refresh();
        }
    }
}

using System.Collections.Generic;
using PAO.AV;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.AV.Editor
{
    /// <summary>Idempotent Map4 narration installation and voice catalog seed.</summary>
    public static class PAOAudioInstaller
    {
        private const string Map4 = "Assets/GameMain/Scenes/Map4.unity";
        private const string LibraryPath = "Assets/GameMain/Resources/PAOAudio/NarrationLibrary.asset";

        [MenuItem("Tools/PAO/Audio/Install Map4 Sample")]
        public static void InstallMap4()
        {
            Scene scene = EditorSceneManager.OpenScene(Map4, OpenSceneMode.Single);
            InstallIntoScene(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[PAO Audio] Map4 narration triggers and voice catalog installed.");
        }

        public static void InstallIntoScene(Scene scene)
        {
            if (!scene.IsValid() || scene.name != "Map4")
                throw new System.InvalidOperationException("PAOAudioInstaller expects the Map4 scene.");

            GameObject root = null;
            foreach (GameObject item in scene.GetRootGameObjects())
                if (item.name == "PAO Map4 Audio Narrative") { root = item; break; }
            if (root == null)
            {
                root = new GameObject("PAO Map4 Audio Narrative");
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            MakeTrigger(root.transform, "Narration_Memory_Beach", new Vector3(-16f, 2.4f, -11f),
                new Vector3(5f, 4f, 8f),
                "走过的水域在这里重新汇聚。试着让不同的泡泡彼此回应。");
            MakeTrigger(root.transform, "Narration_Trench_Shard", new Vector3(1f, -1.6f, -19f),
                new Vector3(7f, 5f, 7f),
                "沉在海沟里的碎片，仍记得你经过时留下的回声。");
            MakeTrigger(root.transform, "Narration_Resonance_Core", new Vector3(1f, 3f, 1f),
                new Vector3(18f, 8f, 18f),
                "泡泡相遇、融合，再向外涌去。新的路径就在这些回响之间。");

            SeedLibrary();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void MakeTrigger(Transform root, string name, Vector3 position, Vector3 size, string line)
        {
            Transform child = root.Find(name);
            GameObject go = child != null ? child.gameObject : new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = position;
            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null) box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            NarrationTrigger trigger = go.GetComponent<NarrationTrigger>();
            if (trigger == null) trigger = go.AddComponent<NarrationTrigger>();
            trigger.Configure("", line, 0f);
            EditorUtility.SetDirty(go);
        }

        private static void SeedLibrary()
        {
            PAONarrationLibrary library = AssetDatabase.LoadAssetAtPath<PAONarrationLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<PAONarrationLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var byId = new Dictionary<string, PAONarrationLibrary.Entry>();
            if (library.entries != null)
                foreach (PAONarrationLibrary.Entry entry in library.entries)
                    if (entry != null && !string.IsNullOrEmpty(entry.id)) byId[entry.id] = entry;

            AddEntry(byId, "Map4/Narration_Memory_Beach",
                "走过的水域在这里重新汇聚。试着让不同的泡泡彼此回应。");
            AddEntry(byId, "Map4/Narration_Trench_Shard",
                "沉在海沟里的碎片，仍记得你经过时留下的回声。");
            AddEntry(byId, "Map4/Narration_Resonance_Core",
                "泡泡相遇、融合，再向外涌去。新的路径就在这些回响之间。");
            library.entries = new List<PAONarrationLibrary.Entry>(byId.Values).ToArray();
            EditorUtility.SetDirty(library);
        }

        private static void AddEntry(Dictionary<string, PAONarrationLibrary.Entry> byId, string id, string transcript)
        {
            PAONarrationLibrary.Entry entry;
            if (!byId.TryGetValue(id, out entry))
            {
                entry = new PAONarrationLibrary.Entry { id = id };
                byId.Add(id, entry);
            }
            entry.transcript = transcript;
        }
    }
}

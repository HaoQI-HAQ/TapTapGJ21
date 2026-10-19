using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PAO.EditorTools
{
    public static class Map2PresetArtRequest
    {
        const string Request = "Temp/PAO_Map2PresetArt.request";
        [InitializeOnLoadMethod] static void Consume()
        {
            if (!File.Exists(Request)) return;
            EditorApplication.delayCall += () => {
                if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
                string action = File.ReadAllText(Request).Trim();
                File.Delete(Request);
                try {
                    if (action == "inspect") Map2PresetAssetCatalog.Inspect();
                    else if (action == "apply") Map2PresetArtPass.Apply();
                    else if (action == "preview") Map2PresetArtPass.ExportPreview();
                    else if (action == "apply-preview") { Map2PresetArtPass.Apply(); Map2PresetArtPass.ExportPreview(); }
                    else if (action == "map2_5-art-and-colliders")
                    {
                        Map2PresetArtPass.ApplyMap2_5();
                        PresetArtMeshColliderBinding.Bind("Map2_5");
                        PresetArtMeshColliderBinding.Bind("Map2");
                        Map2PresetArtPass.ExportPreview("Map2");
                        Map2PresetArtPass.ExportPreview("Map2_5");
                    }
                    else if (action == "map2_5-preview") Map2PresetArtPass.ExportPreview("Map2_5");
                    else if (action == "colliders-both")
                    {
                        PresetArtMeshColliderBinding.Bind("Map2");
                        PresetArtMeshColliderBinding.Bind("Map2_5");
                    }
                    else throw new InvalidOperationException("Unknown Map2 art action: " + action);
                    File.WriteAllText("Temp/PAO_Map2PresetArt.status.json", "{\"action\":\"" + action + "\",\"status\":\"complete\",\"utc\":\"" + DateTime.UtcNow.ToString("O") + "\"}");
                }
                catch (Exception ex) {
                    File.WriteAllText("Temp/PAO_Map2PresetArt.error.txt", ex.ToString());
                    Debug.LogException(ex);
                }
            };
        }
    }
}

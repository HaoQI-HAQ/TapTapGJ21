using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PAO.UI.V2;

namespace PAO.EditorTools
{
    public static class UIMenuVerification
    {
        // Bake only visual meshes from the authored player. No gameplay scripts or cameras are instantiated.
        public static void BakeCharacterCLI()
        {
            if (!File.Exists("PAO_UI_VERIFICATION_PROJECT"))
                throw new System.InvalidOperationException("Character baking requires an isolated validation project.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            UIConfig config = Resources.Load<UIConfig>("PAOUIConfig");
            var stage = new GameObject("Character art stage");
            var materials = new System.Collections.Generic.List<Material>();
            foreach (Renderer source in config.defaultPlayer.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = source is SkinnedMeshRenderer skin ? skin.sharedMesh : source.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var visual = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
                visual.transform.SetParent(stage.transform, false);
                visual.transform.position = source.transform.position;
                visual.transform.rotation = source.transform.rotation;
                visual.transform.localScale = source.transform.lossyScale;
                visual.GetComponent<MeshFilter>().sharedMesh = mesh;
                var converted = new Material[source.sharedMaterials.Length];
                for (int i = 0; i < converted.Length; i++)
                {
                    Material original = source.sharedMaterials[i]; var material = new Material(Shader.Find("Standard"));
                    if (original != null)
                    {
                        Texture texture = original.HasProperty("_BaseMap") ? original.GetTexture("_BaseMap") : original.mainTexture;
                        material.mainTexture = texture;
                        material.color = original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") : original.HasProperty("_Color") ? original.color : Color.white;
                    }
                    material.SetFloat("_Glossiness", .36f); converted[i] = material; materials.Add(material);
                }
                visual.GetComponent<MeshRenderer>().sharedMaterials = converted;
            }
            Bounds bounds = new Bounds(); bool first = true;
            foreach (Renderer visual in stage.GetComponentsInChildren<Renderer>())
            { if (first) { bounds = visual.bounds; first = false; } else bounds.Encapsulate(visual.bounds); }
            if (first) throw new System.InvalidOperationException("Player has no visual mesh.");
            RenderSettings.ambientLight = new Color(.6f, .72f, .9f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            var key = new GameObject("Pearl key", typeof(Light)); key.GetComponent<Light>().type = LightType.Directional;
            key.GetComponent<Light>().intensity = 1.2f; key.transform.rotation = Quaternion.Euler(28, -35, 0);
            var rim = new GameObject("Lilac rim", typeof(Light)); rim.GetComponent<Light>().type = LightType.Directional;
            rim.GetComponent<Light>().color = new Color(.66f, .6f, 1); rim.GetComponent<Light>().intensity = .8f;
            rim.transform.rotation = Quaternion.Euler(10, 145, 0);
            var camera = new GameObject("Art camera", typeof(Camera)).GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.orthographic = true; camera.orthographicSize = bounds.size.y * .59f;
            camera.transform.position = bounds.center + new Vector3(-.25f, .1f, 1).normalized * bounds.size.magnitude * 2;
            camera.transform.LookAt(bounds.center); camera.nearClipPlane = .01f; camera.farClipPlane = bounds.size.magnitude * 6;
            var target = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0); image.Apply();
            File.WriteAllBytes("menu_character_pearl.png", image.EncodeToPNG());
            File.WriteAllText("character-bounds.txt", "Center: " + bounds.center + " Size: " + bounds.size);
            RenderTexture.active = null; camera.targetTexture = null; target.Release();
            foreach (Material material in materials) Object.DestroyImmediate(material);
            Object.DestroyImmediate(image); Object.DestroyImmediate(target); Object.DestroyImmediate(stage);
            EditorApplication.Exit(0);
        }
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

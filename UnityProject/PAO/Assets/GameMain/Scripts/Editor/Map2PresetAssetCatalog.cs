using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PAO.EditorTools
{
    public static class Map2PresetAssetCatalog
    {
        public const string Folder = "Assets/GameMain/Art/Map2PresetParts";
        public static string Output => Path.GetFullPath("../../docs/plan/Map2美术替换");
        [Serializable] public class Entry
        {
            public string id, sourcePath, sourceNode;
            public GameObject prefab;
            public Vector3 size;
        }
        [Serializable] class Catalog { public List<Entry> entries; }
        static readonly string[][] Specs = {
            new[]{"block_02","04","TFD_Building_Block_02A_LOD0"},
            new[]{"block_03","05","TFD_Building_Block_03A_LOD0"},
            new[]{"block_04","06","TFD_Building_Block_04A_LOD0"},
            new[]{"block_06","01","TFD_Building_Block_06A_LOD0"},
            new[]{"pavement_01","01","TFD_Pavement_01A_LOD0"},
            new[]{"pavement_02","02","TFD_Pavement_02A_LOD0"},
            new[]{"pavement_03","03","TFD_Pavement_03A_LOD0"},
            new[]{"pavement_06","01","TFD_Pavement_06A_LOD0"},
            new[]{"pavement_07","06","TFD_Pavement_07A_LOD0"},
            new[]{"pavement_08","01","TFD_Pavement_08A_LOD0"},
            new[]{"beam_08","01","TFD_Beam_08A_LOD0"},
            new[]{"beam_14","04","TFD_Beam_14A_LOD0"},
            new[]{"beam_15","06","TFD_Beam_15A_LOD0"},
            new[]{"wall_01","02","TFD_Wall_01A_LOD0"},
            new[]{"wall_02","04","TFD_Wall_02A_LOD0"},
            new[]{"wall_03","05","TFD_Wall_03A_LOD0"},
            new[]{"wall_04","01","TFD_Wall_04A_LOD0"},
            new[]{"wall_block_01","02","TFD_Wall_Block_01A_LOD0"},
            new[]{"wall_block_02","02","TFD_Wall_Block_02A_LOD0"},
            new[]{"stairs_01","06","TFD_Stairs_01A_LOD0"},
            new[]{"stairs_02","05","TFD_Stairs_02A_LOD0"},
            new[]{"column_02","01","TFD_Column_01A_Module_02_LOD0"},
            new[]{"column_03","06","TFD_Column_01A_Module_03_LOD0"},
            new[]{"column_06","05","TFD_Column_01A_Module_06_LOD0"},
            new[]{"rock_02","04","TFD_Rock_Large_02A_LOD0"},
            new[]{"rock_08","01","TFD_Rock_Large_08A_LOD0"},
            new[]{"stone_01","06","TFD_Stone_01A"},
            new[]{"stone_03","04","TFD_Stone_03A"},
        };

        public static List<Entry> LoadCatalog()
        {
            string path = Path.Combine(Output, "catalog.json");
            if (!File.Exists(path)) return BuildCatalog();
            var saved = JsonUtility.FromJson<Catalog>(File.ReadAllText(path));
            if (saved == null || saved.entries == null) throw new InvalidDataException("Preset catalog is missing entries.");
            foreach (var entry in saved.entries)
            {
                // Unity instance IDs in the JSON do not survive an editor restart.
                entry.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + entry.id + ".prefab");
                if (entry.prefab == null || entry.size.x <= 0 || entry.size.y <= 0 || entry.size.z <= 0)
                    throw new InvalidDataException("Invalid cached preset part: " + entry.id);
            }
            return saved.entries;
        }

        public static List<Entry> BuildCatalog()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/GameMain/Art", "Map2PresetParts");
            var entries = new List<Entry>();
            foreach (var spec in Specs)
            {
                string path = "Assets/GameMain/Art/Preset/" + spec[1] + ".glb";
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (source == null) throw new IOException("Preset model not imported: " + path);
                Transform node = source.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.StartsWith(spec[2], StringComparison.Ordinal) && t.GetComponentsInChildren<MeshFilter>(true).Length > 0)
                    .OrderBy(t => t.name.Length).FirstOrDefault();
                if (node == null) throw new InvalidDataException("Missing mesh node " + spec[2] + " in " + path);
                GameObject wrapper = new GameObject(spec[0]);
                GameObject geometry = new GameObject("Geometry");
                geometry.transform.SetParent(wrapper.transform, false);
                try
                {
                    foreach (MeshFilter sourceMesh in node.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var sourceRenderer = sourceMesh.GetComponent<MeshRenderer>();
                        if (sourceMesh.sharedMesh == null || sourceRenderer == null) continue;
                        GameObject part = new GameObject(sourceMesh.name);
                        part.transform.SetParent(geometry.transform, false);
                        part.transform.localPosition = sourceMesh.transform.position - node.position;
                        part.transform.localRotation = sourceMesh.transform.rotation;
                        part.transform.localScale = sourceMesh.transform.lossyScale;
                        part.AddComponent<MeshFilter>().sharedMesh = sourceMesh.sharedMesh;
                        var renderer = part.AddComponent<MeshRenderer>();
                        renderer.sharedMaterials = sourceRenderer.sharedMaterials;
                    }
                    // Remove the display-layout yaw while retaining the glTF axis conversion.
                    Vector3 axis = node.GetComponentInChildren<MeshFilter>().transform.right;
                    axis.y = 0;
                    if (axis.sqrMagnitude > .01f)
                        geometry.transform.localRotation = Quaternion.AngleAxis(Vector3.SignedAngle(axis, Vector3.right, Vector3.up), Vector3.up);
                    Bounds b = BoundsOf(wrapper);
                    if (spec[0].StartsWith("wall_") && b.size.z > b.size.x)
                        geometry.transform.localRotation = Quaternion.Euler(0, 90, 0) * geometry.transform.localRotation;
                    if (spec[0].StartsWith("stairs_")) OrientStairs(geometry);
                    b = BoundsOf(wrapper);
                    geometry.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                    b = BoundsOf(wrapper);
                    string prefabPath = Folder + "/" + spec[0] + ".prefab";
                    var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, prefabPath);
                    entries.Add(new Entry { id = spec[0], sourcePath = path, sourceNode = node.name, prefab = prefab, size = b.size });
                }
                finally { Object.DestroyImmediate(wrapper); }
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "catalog.json"), JsonUtility.ToJson(new Catalog { entries = entries }, true));
            return entries;
        }

        static void OrientStairs(GameObject geometry)
        {
            Bounds b = BoundsOf(geometry);
            Vector3 high = Vector3.zero, low = Vector3.zero; int nh = 0, nl = 0;
            foreach (var mf in geometry.GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 v in mf.sharedMesh.vertices)
                {
                    Vector3 p = mf.transform.TransformPoint(v);
                    if (p.y > b.max.y - b.size.y * .15f) { high += p; nh++; }
                    if (p.y < b.min.y + b.size.y * .15f) { low += p; nl++; }
                }
            if (nh == 0 || nl == 0) return;
            Vector3 direction = high / nh - low / nl; direction.y = 0;
            if (direction.sqrMagnitude > .0001f)
            {
                // All these modular stair meshes use orthogonal axes. Irregular chips can
                // bias the centroid slightly; snap the inferred climb axis to that grid.
                float angle = Mathf.Round(Vector3.SignedAngle(direction, Vector3.back, Vector3.up) / 90f) * 90f;
                geometry.transform.rotation = Quaternion.AngleAxis(angle, Vector3.up) * geometry.transform.rotation;
            }
        }

        public static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidDataException("No renderer in " + root.name);
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        public static void RenderPreview(GameObject model, string path, int width = 480, int height = 400)
        {
            var preview = new PreviewRenderUtility();
            GameObject copy = Object.Instantiate(model);
            copy.SetActive(true);
            try
            {
                preview.AddSingleGO(copy);
                Bounds b = BoundsOf(copy);
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = Mathf.Max(b.size.y, Mathf.Max(b.size.x, b.size.z)) * .72f;
                preview.camera.nearClipPlane = .01f;
                preview.camera.farClipPlane = 2000;
                preview.camera.clearFlags = CameraClearFlags.Color;
                preview.camera.backgroundColor = new Color(.12f,.16f,.19f,1);
                preview.camera.transform.position = b.center + new Vector3(1.3f,.95f,-1.4f).normalized * (b.size.magnitude * 2 + 10);
                preview.camera.transform.LookAt(b.center);
                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35, -30, 0);
                preview.lights[1].intensity = .7f;
                preview.ambientColor = new Color(.45f,.48f,.52f,1);
                preview.BeginStaticPreview(new Rect(0,0,width,height));
                preview.Render(true);
                Texture2D texture = preview.EndStaticPreview();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
            finally { preview.Cleanup(); }
        }

        public static void Inspect()
        {
            foreach (Entry entry in BuildCatalog())
                RenderPreview(entry.prefab, Path.Combine(Output,"previews",entry.id+".png"));
            Debug.Log("[Map2 Preset Art] Catalog and previews exported.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PAO.EditorTools
{
    /// <summary>
    /// Rebuilds measured placement proxies (2026-10-18). Source SRT and model bounds are retained;
    /// internal profiles, materials and source gameplay scripts are not original assets.
    /// </summary>
    public static class ShrineReferenceRebuild
    {
        const string LayoutFolder = "Assets/GameMain/Data/ShrineReferences/";
        const string MaterialFolder = "Assets/GameMain/Art/WhiteboxMaterials/";
        const string RequestPath = "Temp/PAO_ShrineRebuild.request";
        static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        [Serializable] public class Layout
        {
            public string sceneName, sourceMap, title, sourceUrl;
            public int sourceCount;
            public Vector3 entry, goal;
            public string spawnAnchor, goalAnchor;
            public Part[] objects;
            public SupportPart[] supportGeometry;
        }
        [Serializable] public class SupportPart
        {
            public string name, note;
            public Vector3 position, size;
        }
        [Serializable] public class Part
        {
            public string name, hash, kind, group, note, motion;
            public Vector3 position, rotation, scale, boundsMin, boundsMax, endpoint, motionEnd;
            public bool solid;
            public float motionSpeed;
        }
        [Serializable] class BuildReceipt
        {
            public string scene, source, backup, utc;
            public int sourceObjects, proxyObjects, renderers;
        }

        [InitializeOnLoadMethod]
        static void ConsumeExplicitRequest()
        {
            if (!File.Exists(RequestPath)) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(RequestPath) || EditorApplication.isPlayingOrWillChangePlaymode) return;
                string requestedMaps = File.ReadAllText(RequestPath).Trim();
                File.Delete(RequestPath);
                try
                {
                    if (requestedMaps == "Map2_5") RebuildMap2_5();
                    else if (requestedMaps == "Map2+Map3") RebuildMap2And3();
                    else throw new InvalidDataException("Unrecognized shrine rebuild request: " + requestedMaps);
                }
                catch (Exception ex)
                {
                    Directory.CreateDirectory("Temp");
                    File.WriteAllText("Temp/PAO_ShrineRebuild.error.txt", ex.ToString());
                    Debug.LogException(ex);
                }
            };
        }

        [MenuItem("Tools/PAO/关卡白盒/按原始坐标重建 Map2 + Map3", priority = 76)]
        public static void RebuildMap2And3()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before rebuilding scenes.");
            RebuildScene("Map2");
            RebuildScene("Map3");
            ShowRebuiltScene("Map2");
            Debug.Log("[PAO Shrine Reference] Map2 and Map3 rebuilt and saved from original placement coordinates. Meshes are whitebox proxies; original game logic is not imported.");
        }

        [MenuItem("Tools/PAO/关卡白盒/按原始坐标重建 Map2.5 卡奥·玛卡", priority = 75)]
        public static void RebuildMap2_5()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before rebuilding scenes.");
            RebuildScene("Map2_5");
            ShowRebuiltScene("Map2_5");
            Debug.Log("[PAO Shrine Reference] Map2.5 rebuilt and saved from Dungeon054. Geometry only; metal-door interactions are not implemented.");
        }

        static void ShowRebuiltScene(string mapName)
        {
            string path = "Assets/GameMain/Scenes/" + mapName + ".unity";
            Scene scene = SceneManager.GetSceneByPath(path);
            if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == mapName + "_Whitebox")
                {
                    Selection.activeGameObject = root;
                    SceneVisibilityManager.instance.Isolate(root, true);
                    if (SceneView.lastActiveSceneView != null)
                    {
                        SceneView.lastActiveSceneView.rotation = Quaternion.Euler(90, 0, 0);
                        SceneView.lastActiveSceneView.orthographic = true;
                        SceneView.lastActiveSceneView.FrameSelected();
                    }
                }
        }

        static void RebuildScene(string mapName)
        {
            string path = "Assets/GameMain/Scenes/" + mapName + ".unity";
            string backupDir = Path.GetFullPath("../../docs/plan/神庙重构参考/backups");
            Directory.CreateDirectory(backupDir);
            string backup = Path.Combine(backupDir, mapName + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".unity");
            Scene scene = SceneManager.GetSceneByPath(path);
            bool wasLoaded = scene.isLoaded;
            // A copy of the in-memory scene protects unsaved edits as well as saved ones.
            if (wasLoaded && scene.isDirty)
            {
                if (!EditorSceneManager.SaveScene(scene, backup, true))
                    throw new IOException("Could not back up unsaved scene: " + path);
            }
            else File.Copy(path, backup, false);
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            GameObject next = new GameObject(mapName + "_Reference_Staging");
            SceneManager.MoveGameObjectToScene(next, scene);
            try
            {
                Layout layout = BuildGeometry(next.transform, mapName);
                // Preserve every object outside the generated root, including user edits.
                foreach (GameObject previous in scene.GetRootGameObjects())
                    if (previous != next && previous.name == mapName + "_Whitebox")
                        Object.DestroyImmediate(previous);
                next.name = mapName + "_Whitebox";
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + path);
                var receipt = new BuildReceipt {
                    scene = path, source = layout.sourceUrl, backup = backup,
                    utc = DateTime.UtcNow.ToString("O"), sourceObjects = layout.sourceCount,
                    proxyObjects = next.GetComponentsInChildren<ShrineWhiteboxReference>(true).Length,
                    renderers = next.GetComponentsInChildren<Renderer>(true).Length
                };
                File.WriteAllText(Path.GetFullPath("../../docs/plan/神庙重构参考/" + mapName + "_build_receipt.json"), JsonUtility.ToJson(receipt, true));
            }
            catch
            {
                if (next != null && next.name.EndsWith("_Staging")) Object.DestroyImmediate(next);
                throw;
            }
            finally
            {
                if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        public static Layout BuildGeometry(Transform root, string mapName)
        {
            Layout layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutFolder + mapName + "_ReferenceLayout.json"));
            if (layout == null || layout.objects == null || layout.objects.Length == 0)
                throw new InvalidDataException("Empty shrine reference layout: " + mapName);
            var groups = new Dictionary<string, Transform>();
            foreach (Part part in layout.objects)
            {
                if (!groups.TryGetValue(part.group, out Transform group))
                {
                    group = new GameObject(part.group).transform;
                    group.SetParent(root, false);
                    groups.Add(part.group, group);
                }
                BuildPart(group, layout.sourceMap, part);
            }
            if (layout.supportGeometry != null && layout.supportGeometry.Length > 0)
            {
                Transform floor = new GameObject("Shell_Floor_Proxy").transform;
                floor.SetParent(root, false);
                foreach (SupportPart support in layout.supportGeometry)
                    Cube(floor, support.name, support.position, support.size, GetMaterial("Structure"), true);
            }
            string spawnAnchor = !string.IsNullOrEmpty(layout.spawnAnchor) ? layout.spawnAnchor : mapName == "Map2" ? "Start_Salvage_Court" : "West_Start_Camp";
            string goalAnchor = !string.IsNullOrEmpty(layout.goalAnchor) ? layout.goalAnchor : mapName == "Map2" ? "Memory_Beacon_Lower_Platform" : "Map3_Corkscrew_Upper_Exit_Shelf";
            AddAnchor(root, spawnAnchor, layout.entry + Vector3.up * 1.27f);
            AddAnchor(root, goalAnchor, layout.goal);
            AddAnchor(root, "SOURCE_COORDINATES_1_TO_1__MESHES_ARE_PROXIES", layout.entry);
            return layout;
        }

        static void AddAnchor(Transform root, string name, Vector3 position)
        {
            Transform anchor = new GameObject(name).transform;
            anchor.SetParent(root, false);
            anchor.localPosition = position;
        }

        static Material GetMaterial(string group)
        {
            string asset = group == "Mechanisms" ? "WB_FloatSticky" : group == "Breakable" ? "WB_Destructible" : group == "Landmarks" ? "WB_Bounce" : group == "Detail" ? "WB_Platform" : "WB_Ground";
            if (!Materials.TryGetValue(asset, out Material material) || material == null)
            {
                material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + asset + ".mat");
                if (material == null) throw new FileNotFoundException("Missing whitebox material: " + asset);
                Materials[asset] = material;
            }
            return material;
        }

        static Quaternion SourceRotation(Vector3 degrees)
        {
            return Quaternion.AngleAxis(degrees.z, Vector3.forward)
                 * Quaternion.AngleAxis(degrees.y, Vector3.up)
                 * Quaternion.AngleAxis(degrees.x, Vector3.right);
        }

        static GameObject Primitive(Transform parent, string name, PrimitiveType type, Vector3 center, Vector3 size, Material material, bool solid)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Cube(Transform parent, string name, Vector3 center, Vector3 size, Material material, bool solid)
        {
            return Primitive(parent, name, PrimitiveType.Cube, center, size, material, solid);
        }

        static void BuildPart(Transform group, string sourceMap, Part part)
        {
            Transform pivot = new GameObject(part.name + "__" + part.hash).transform;
            pivot.SetParent(group, false);
            pivot.localPosition = part.position;
            pivot.localRotation = SourceRotation(part.rotation);
            pivot.localScale = part.scale;
            var reference = pivot.gameObject.AddComponent<ShrineWhiteboxReference>();
            reference.sourceMap = sourceMap; reference.sourceActor = part.name; reference.sourceHash = part.hash;
            reference.sourcePosition = part.position; reference.sourceEulerDegrees = part.rotation;
            reference.modelBoundsMin = part.boundsMin; reference.modelBoundsMax = part.boundsMax;
            reference.geometryNote = part.note;
            Material material = GetMaterial(part.group);
            Vector3 min = part.boundsMin, max = part.boundsMax, size = max - min, center = (min + max) * .5f;

            switch (part.kind)
            {
                case "rope":
                    pivot.localScale = Vector3.one;
                    pivot.localRotation = Quaternion.identity;
                    Vector3 delta = part.endpoint - part.position;
                    GameObject rope = Primitive(pivot, "Source_link_chain", PrimitiveType.Cylinder, delta * .5f,
                        new Vector3(.10f, delta.magnitude * .5f, .10f), material, false);
                    if (delta.sqrMagnitude > .0001f) rope.transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
                    break;
                case "sphere":
                case "spikes":
                case "lamp":
                    Primitive(pivot, "Round_proxy", PrimitiveType.Sphere, center, size, material, part.solid);
                    break;
                case "cylinder":
                    Primitive(pivot, "Arrival_pad", PrimitiveType.Cylinder, center, new Vector3(size.x, size.y * .5f, size.z), material, part.solid);
                    break;
                case "ladder":
                    float rail = .09f;
                    Cube(pivot, "Left_rail", new Vector3(min.x + rail, center.y, center.z), new Vector3(rail, size.y, .10f), material, true);
                    Cube(pivot, "Right_rail", new Vector3(max.x - rail, center.y, center.z), new Vector3(rail, size.y, .10f), material, true);
                    int rungs = Mathf.Max(2, Mathf.RoundToInt(size.y / .32f));
                    for (int i = 0; i <= rungs; i++)
                        Cube(pivot, "Rung_" + i, new Vector3(center.x, Mathf.Lerp(min.y, max.y, (float)i / rungs), center.z), new Vector3(size.x, .09f, .10f), material, true);
                    break;
                case "piston":
                    Cube(pivot, "Piston_head", new Vector3(center.x, 2, center.z), new Vector3(size.x, 4, size.z), material, true);
                    Cube(pivot, "Piston_rod", new Vector3(center.x, -6, center.z), new Vector3(1.1f, 12, 1.1f), material, true);
                    break;
                case "pistonBase":
                case "channel":
                    float t = part.kind == "channel" ? .15f : .35f;
                    Cube(pivot, "Trough_bottom", new Vector3(center.x, min.y + t * .5f, center.z), new Vector3(size.x, t, size.z), material, true);
                    Cube(pivot, "Trough_left", new Vector3(min.x + t * .5f, center.y, center.z), new Vector3(t, size.y, size.z), material, true);
                    Cube(pivot, "Trough_right", new Vector3(max.x - t * .5f, center.y, center.z), new Vector3(t, size.y, size.z), material, true);
                    break;
                case "frame":
                    float border = .2f;
                    Cube(pivot, "Frame_left", new Vector3(min.x + border * .5f, center.y, center.z), new Vector3(border, size.y, size.z), material, true);
                    Cube(pivot, "Frame_right", new Vector3(max.x - border * .5f, center.y, center.z), new Vector3(border, size.y, size.z), material, true);
                    Cube(pivot, "Frame_top", new Vector3(center.x, max.y - border * .5f, center.z), new Vector3(size.x, border, size.z), material, true);
                    Cube(pivot, "Frame_bottom", new Vector3(center.x, min.y + border * .5f, center.z), new Vector3(size.x, border, size.z), material, true);
                    break;
                case "altar":
                    // Keep the altar approach hollow; its model envelope includes a tall canopy.
                    Cube(pivot, "Altar_plinth", new Vector3(center.x, min.y - .15f, center.z), new Vector3(size.x, .3f, size.z), material, true);
                    Cube(pivot, "Altar_marker", new Vector3(center.x, min.y + 1, min.z + 1), new Vector3(1.2f, 2, 1.2f), material, false);
                    break;
                case "stairs":
                    int steps = Mathf.Max(4, Mathf.CeilToInt(size.y / .25f));
                    for (int i = 0; i < steps; i++)
                    {
                        float height = size.y * (i + 1) / steps;
                        Cube(pivot, "Step_" + i, new Vector3(center.x, min.y + height * .5f, max.z - size.z * (i + .5f) / steps), new Vector3(size.x, height, size.z / steps), material, true);
                    }
                    break;
                case "slope":
                    BuildSlope(pivot, min, max, material);
                    break;
                default:
                    Cube(pivot, "Bounds_proxy", center, size, material, part.solid);
                    break;
            }
        }

        static void BuildSlope(Transform parent, Vector3 min, Vector3 max, Material material)
        {
            // Standard shrine stair modules rise toward local -Z. A wedge retains that profile.
            var vertices = new[] {
                new Vector3(min.x,min.y,min.z), new Vector3(max.x,min.y,min.z),
                new Vector3(min.x,min.y,max.z), new Vector3(max.x,min.y,max.z),
                new Vector3(min.x,max.y,min.z), new Vector3(max.x,max.y,min.z)
            };
            var mesh = new Mesh { name = "ReferenceSlopeProxy" };
            mesh.vertices = vertices;
            mesh.triangles = new[] {0,3,2,0,1,3, 0,4,5,0,5,1, 0,2,4,1,5,3, 2,3,5,2,5,4};
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            // Asset-backed meshes survive closing/reopening the saved scene.
            string folder = LayoutFolder + "GeneratedMeshes";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(LayoutFolder.TrimEnd('/'), "GeneratedMeshes");
            string signature = (max - min).ToString("F3") + min.ToString("F3");
            string path = folder + "/Slope_" + Hash128.Compute(signature).ToString() + ".asset";
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
            else Object.DestroyImmediate(mesh);
            GameObject go = new GameObject("Slope_profile_proxy");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = saved;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<MeshCollider>().sharedMesh = saved;
        }
    }
}

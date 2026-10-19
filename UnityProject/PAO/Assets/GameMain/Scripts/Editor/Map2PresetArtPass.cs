using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PAO.EditorTools
{
    public static class Map2PresetArtPass
    {
        static Dictionary<string, Map2PresetAssetCatalog.Entry> catalog;
        static Bounds[] pavingBounds;
        static readonly Dictionary<string, int> usage = new Dictionary<string, int>();
        static int modules;
        static int recessed;
        [Serializable] class Use { public string asset, pack; public int count; }
        [Serializable] class Receipt
        {
            public string scene, backup, utc;
            public int sourceActors, supportFloors, hiddenProxyRenderers, placedModules, originalColliders, finalColliders, recessedSubstrates;
            public string[] protectedGroups;
            public Use[] assets;
        }

        [MenuItem("Tools/PAO/Map2 美术/应用 Preset 01–06")]
        public static void Apply() { Apply("Map2"); }

        [MenuItem("Tools/PAO/Map2.5 美术/应用 Preset 01–06")]
        public static void ApplyMap2_5() { Apply("Map2_5"); }

        public static void Apply(string mapName)
        {
            if (mapName != "Map2" && mapName != "Map2_5") throw new ArgumentException("Unsupported art scene: " + mapName);
            string scenePath = "Assets/GameMain/Scenes/" + mapName + ".unity";
            string rootName = mapName + "_Preset_Art";
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before dressing " + mapName);
            catalog = Map2PresetAssetCatalog.LoadCatalog().ToDictionary(e => e.id);
            usage.Clear(); modules = 0; recessed = 0;
            string backupDir = Path.Combine(Map2PresetAssetCatalog.Output, "backups");
            Directory.CreateDirectory(backupDir);
            string backup = Path.Combine(backupDir, mapName + "_before_preset_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".unity");
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (scene.isLoaded && scene.isDirty)
            {
                if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Could not back up unsaved " + mapName);
            }
            else File.Copy(scenePath, backup, false);
            if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            GameObject whitebox = scene.GetRootGameObjects().Single(g => g.name == mapName + "_Whitebox");
            Transform old = whitebox.transform.Find(rootName);
            var oldState = old != null ? old.GetComponent<Map2PresetArtState>() : null;
            var oldCollision = old != null ? old.GetComponent<PresetArtMeshCollisionState>() : null;
            var previousStates = new Dictionary<Renderer, bool>();
            if (oldState != null && oldState.sourceRenderers != null)
                for (int i = 0; i < oldState.sourceRenderers.Length; i++)
                    if (oldState.sourceRenderers[i] != null)
                        previousStates[oldState.sourceRenderers[i]] = oldState.originalEnabled[i];
            string[] groups = mapName == "Map2_5" ? new[]{"Structure", "Detail", "PartitionWalls"} : new[]{"Structure", "Detail"};
            var actors = groups.Select(group => whitebox.transform.Find(group)).Where(t => t != null)
                .SelectMany(t => t.GetComponentsInChildren<ShrineWhiteboxReference>(true))
                .Where(a => !a.sourceActor.Contains("DoorIronRail")).ToArray();
            Transform floorGroup = mapName == "Map2_5" ? whitebox.transform.Find("Shell_Floor_Proxy") : null;
            var floors = floorGroup != null ? floorGroup.GetComponentsInChildren<MeshRenderer>(true) : new MeshRenderer[0];
            var sources = actors.SelectMany(a => a.GetComponentsInChildren<Renderer>(true)).Concat(floors).Distinct().ToArray();
            pavingBounds = actors.Where(a => a.sourceActor.Contains("BoxCover"))
                .Select(WorldBounds).ToArray();
            var states = sources.Select(r => previousStates.TryGetValue(r, out bool e) ? e : r.enabled).ToArray();
            var beforeRenderers = sources.Select(r => r.enabled).ToArray();
            int colliders = whitebox.GetComponentsInChildren<Collider>(true).Length;
            GameObject next = new GameObject(rootName + "_Staging");
            next.transform.SetParent(whitebox.transform, false);
            bool committed = false;
            PresetArtMeshColliderBinding.Result rebound = null;
            try
            {
                for (int i = 0; i < actors.Length; i++)
                {
                    if (i % 30 == 0) EditorUtility.DisplayProgressBar(mapName + " Preset art", actors[i].sourceActor, (float)i / actors.Length);
                    ShrineWhiteboxReference a = actors[i];
                    Transform art = new GameObject(a.name).transform;
                    art.SetParent(next.transform, false);
                    art.position = a.transform.position;
                    art.rotation = a.transform.rotation;
                    art.localScale = a.transform.lossyScale;
                    BuildActor(art, a, i);
                }
                for (int i = 0; i < floors.Length; i++) BuildSupportFloor(next.transform, floors[i], i);
                var state = next.AddComponent<Map2PresetArtState>();
                state.backupPath = backup; state.generatedUtc = DateTime.UtcNow.ToString("O");
                state.sourceRenderers = sources; state.originalEnabled = states;
                state.sourceActors = actors.Length; state.placedModules = modules;
                if (oldCollision != null)
                {
                    // Regeneration must retain the original proxy state and replace
                    // the art collisions before discarding the previous art root.
                    var collision = next.AddComponent<PresetArtMeshCollisionState>();
                    collision.sourceColliders = oldCollision.sourceColliders;
                    collision.originalEnabled = oldCollision.originalEnabled;
                    rebound = PresetArtMeshColliderBinding.ApplyToArtRoot(next);
                }
                // Keep all source objects active: only the visual proxy components are hidden.
                foreach (Renderer r in sources) r.enabled = false;
                if (old != null) old.gameObject.SetActive(false);
                next.name = rootName;
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException(mapName + " scene save failed.");
                committed = true;
                if (old != null) Object.DestroyImmediate(old.gameObject);
                EditorSceneManager.SaveScene(scene);
                var receipt = new Receipt {
                    scene = scenePath, backup = backup, utc = state.generatedUtc,
                    sourceActors = actors.Length, supportFloors = floors.Length, hiddenProxyRenderers = sources.Length, placedModules = modules,
                    originalColliders = colliders, finalColliders = whitebox.GetComponentsInChildren<Collider>(true).Length,
                    recessedSubstrates = recessed,
                    protectedGroups = new[]{"Mechanisms", "Breakable", "Landmarks", "Detail/DoorIronRail"},
                    assets = usage.Select(kv => new Use { asset = kv.Key, pack = catalog[kv.Key].sourcePath, count = kv.Value }).ToArray()
                };
                File.WriteAllText(Path.Combine(Map2PresetAssetCatalog.Output, mapName + "_art_receipt.json"), JsonUtility.ToJson(receipt, true));
                SceneManager.SetActiveScene(scene);
                Selection.activeGameObject = whitebox;
                SceneVisibilityManager.instance.Isolate(whitebox, true);
                if (SceneView.lastActiveSceneView != null)
                {
                    SceneView.lastActiveSceneView.rotation = Quaternion.Euler(62, 155, 0);
                    SceneView.lastActiveSceneView.orthographic = true;
                    SceneView.lastActiveSceneView.FrameSelected();
                }
                Debug.Log("[Preset Art] " + mapName + ": saved " + actors.Length + " actors, " + floors.Length + " support floors, " + modules + " art modules. Backup: " + backup);
            }
            catch
            {
                if (!committed)
                {
                    if (rebound != null) PresetArtMeshColliderBinding.Rollback(rebound);
                    for (int i = 0; i < sources.Length; i++) sources[i].enabled = beforeRenderers[i];
                    if (old != null) old.gameObject.SetActive(true);
                    Object.DestroyImmediate(next);
                }
                throw;
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static void BuildSupportFloor(Transform root, MeshRenderer source, int seed)
        {
            var mesh = source.GetComponent<MeshFilter>();
            if (mesh == null || mesh.sharedMesh == null) throw new InvalidDataException("Support floor has no mesh: " + source.name);
            Transform art = new GameObject(source.name).transform;
            art.SetParent(root, false);
            art.position = source.transform.position;
            art.rotation = source.transform.rotation;
            // Tile in metres, so a large room slab does not become one stretched tile.
            Vector3 min = Vector3.Scale(mesh.sharedMesh.bounds.min, source.transform.lossyScale);
            Vector3 max = Vector3.Scale(mesh.sharedMesh.bounds.max, source.transform.lossyScale);
            TilePlane(art, min, max, seed, false);
        }

        static void BuildActor(Transform parent, ShrineWhiteboxReference source, int seed)
        {
            string n = source.sourceActor;
            Vector3 min = source.modelBoundsMin, max = source.modelBoundsMax, size = max - min;
            if (n.Contains("Stair")) { Stairs(parent, min, max); return; }
            if (n.Contains("Slope")) { Slope(parent, source); return; }
            if (n.Contains("BoxCover") || n.Contains("Road"))
            {
                TilePlane(parent, min, max, seed, n.Contains("Road")); return;
            }
            if (n.Contains("Wainscot"))
            {
                if (n.EndsWith("_02"))
                {
                    const float t = .29948f;
                    Fit(parent, "beam_14", new Vector3(min.x,min.y,max.z-t), max);
                    Fit(parent, "beam_14", new Vector3(max.x-t,min.y,min.z), new Vector3(max.x,max.y,max.z-t));
                }
                else Fit(parent, "beam_14", min, max);
                return;
            }
            if (n.Contains("WallMark"))
            {
                // A shallow stone facing occupies the original decorative overlay only.
                Fit(parent, seed % 2 == 0 ? "wall_01" : "wall_02", min, max); return;
            }
            if (n.Contains("Candle"))
            {
                Fit(parent, n.Contains("Stand") ? "column_03" : "column_06", min, max); return;
            }
            if (n.Contains("Pillar")) { Fit(parent, "column_02", min, max); return; }
            if (n.Contains("TowerRoof")) { Fit(parent, "rock_02", min, max); return; }
            if (source.transform.parent.name == "Structure" || source.transform.parent.name == "PartitionWalls")
            {
                // The cover's top coincides with the old solid cube top. Recess only
                // the visual substrate; otherwise uneven paving z-fights with it.
                Bounds world = WorldBounds(source);
                bool covered = pavingBounds.Any(b => Mathf.Abs(b.max.y-world.max.y)<.06f
                    && Mathf.Min(b.max.x,world.max.x)-Mathf.Max(b.min.x,world.min.x)>.4f
                    && Mathf.Min(b.max.z,world.max.z)-Mathf.Max(b.min.z,world.min.z)>.4f);
                if (covered) { max.y -= .18f / Mathf.Max(.01f,source.transform.lossyScale.y); recessed++; }
                TileBlock(parent, min, max, seed); return;
            }
            // Retain unusual profiles rather than making a solid out of their bounds.
            CopyProfile(parent, source);
        }

        static Bounds WorldBounds(ShrineWhiteboxReference a)
        {
            Vector3 min=a.modelBoundsMin,max=a.modelBoundsMax;
            Bounds b=new Bounds(a.transform.TransformPoint(min),Vector3.zero);
            for(int i=0;i<8;i++) b.Encapsulate(a.transform.TransformPoint(new Vector3((i&1)==0?min.x:max.x,(i&2)==0?min.y:max.y,(i&4)==0?min.z:max.z)));
            return b;
        }

        static void TileBlock(Transform parent, Vector3 min, Vector3 max, int seed)
        {
            Vector3 size = max - min;
            int nx = Mathf.Max(1, Mathf.CeilToInt(size.x / 4.4f));
            int ny = Mathf.Max(1, Mathf.CeilToInt(size.y / 4.05f));
            int nz = Mathf.Max(1, Mathf.CeilToInt(size.z / 4.4f));
            Vector3 cell = new Vector3(size.x/nx, size.y/ny, size.z/nz);
            for (int y=0;y<ny;y++) for (int z=0;z<nz;z++) for (int x=0;x<nx;x++)
            {
                Vector3 a = min + Vector3.Scale(cell,new Vector3(x,y,z));
                // Mostly square-cut masonry, with weathered variants as occasional joints.
                int k=(seed+x+z*3+y*7)%7;
                Fit(parent, k==0 ? "block_02" : k==1 ? "block_03" : "block_04", a, a+cell);
            }
        }

        static void TilePlane(Transform parent, Vector3 min, Vector3 max, int seed, bool road)
        {
            Vector3 size = max-min;
            int nx = Mathf.Max(1,Mathf.RoundToInt(size.x/4));
            int nz = Mathf.Max(1,Mathf.RoundToInt(size.z/4));
            var ids = new[]{"pavement_01", "pavement_02", "pavement_03", "pavement_06", "pavement_07"};
            Vector3 cell = new Vector3(size.x/nx,size.y,size.z/nz);
            for(int z=0;z<nz;z++) for(int x=0;x<nx;x++)
            {
                Vector3 a = min + new Vector3(cell.x*x,0,cell.z*z);
                Vector3 top=a+cell;
                // Keep the top datum; allow the decorative slab to extend below it
                // instead of crushing its bevels into a nearly coplanar surface.
                a.y=top.y-Mathf.Max(.16f,cell.y);
                Fit(parent, road ? "pavement_02" : ids[(seed+x+z*2)%ids.Length], a, top);
            }
        }

        static void Stairs(Transform parent, Vector3 min, Vector3 max)
        {
            Vector3 s=max-min;
            int rows=Mathf.Max(1,Mathf.RoundToInt(s.y));
            int cols=Mathf.Max(1,Mathf.RoundToInt(s.x/2));
            float dx=s.x/cols,dy=s.y/rows,dz=s.z/rows;
            for(int row=0;row<rows;row++) for(int col=0;col<cols;col++)
            {
                Vector3 a=new Vector3(min.x+dx*col,min.y+dy*row,max.z-dz*(row+1));
                Fit(parent, col%2==0 ? "stairs_02" : "stairs_01", a,a+new Vector3(dx,dy,dz));
                if(row>0) Fit(parent,"block_04",new Vector3(a.x,min.y,a.z),new Vector3(a.x+dx,a.y,a.z+dz));
            }
        }

        static void Slope(Transform parent, ShrineWhiteboxReference source)
        {
            CopyProfile(parent, source);
        }

        static void CopyProfile(Transform parent, ShrineWhiteboxReference source)
        {
            Material stone = catalog["pavement_03"].prefab.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            foreach(var original in source.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = new GameObject("Stone_profile_"+original.name);
                go.transform.SetParent(parent,false);
                go.transform.position=original.transform.position;
                go.transform.rotation=original.transform.rotation;
                go.transform.localScale=original.transform.localScale;
                go.AddComponent<MeshFilter>().sharedMesh=original.sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial=stone;
            }
        }

        static void Fit(Transform parent, string id, Vector3 min, Vector3 max)
        {
            var entry=catalog[id];
            var go=(GameObject)PrefabUtility.InstantiatePrefab(entry.prefab,parent);
            Vector3 size=max-min;
            go.transform.localPosition=new Vector3((min.x+max.x)*.5f,min.y,(min.z+max.z)*.5f);
            go.transform.localRotation=Quaternion.identity;
            go.transform.localScale=new Vector3(size.x/entry.size.x,size.y/entry.size.y,size.z/entry.size.z);
            if(id.StartsWith("beam_") && size.z>size.x)
            {
                go.transform.localRotation=Quaternion.Euler(0,90,0);
                go.transform.localScale=new Vector3(size.z/entry.size.x,size.y/entry.size.y,size.x/entry.size.z);
            }
            go.name=id+"_"+modules.ToString("D4");
            if(id.StartsWith("pavement_"))
                foreach(var r in go.GetComponentsInChildren<MeshRenderer>())
                    r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            modules++;
            usage[id]=usage.TryGetValue(id,out int count)?count+1:1;
        }

        [MenuItem("Tools/PAO/Map2 美术/导出当前场景预览")]
        public static void ExportPreview() { ExportPreview("Map2"); }

        public static void ExportPreview(string mapName)
        {
            string scenePath = "Assets/GameMain/Scenes/" + mapName + ".unity";
            Scene scene=SceneManager.GetSceneByPath(scenePath);
            if(!scene.isLoaded) scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
            GameObject root=scene.GetRootGameObjects().Single(g=>g.name==mapName+"_Whitebox");
            Bounds? focus = mapName == "Map2_5" ? new Bounds(new Vector3(-12,8,24),new Vector3(96,32,144)) : (Bounds?)null;
            ExportView(root,mapName+"_preset_overview.png",Quaternion.Euler(58,155,0),focus);
            ExportView(root,mapName+"_preset_top.png",Quaternion.Euler(90,180,0),focus);
            ExportView(root,mapName+"_preset_detail.png",Quaternion.Euler(48,155,0),mapName=="Map2_5"
                ? new Bounds(new Vector3(-12,5,29),new Vector3(76,24,66))
                : new Bounds(new Vector3(-5,3,-37),new Vector3(67,24,68)));
            SceneManager.SetActiveScene(scene);
            // Leave the finished map as the displayed scene. Never close another
            // scene with unsaved user work.
            for(int i=SceneManager.sceneCount-1;i>=0;i--)
            {
                Scene other=SceneManager.GetSceneAt(i);
                if(other!=scene && !other.isDirty) EditorSceneManager.CloseScene(other,true);
            }
            SceneVisibilityManager.instance.Isolate(root,true);
            Selection.activeGameObject=null;
            if(SceneView.lastActiveSceneView!=null)
            {
                SceneView.lastActiveSceneView.drawGizmos=false;
                SceneView.lastActiveSceneView.sceneLighting=false;
                SceneView.lastActiveSceneView.showGrid=false;
                SceneView.lastActiveSceneView.orthographic=true;
                SceneView.lastActiveSceneView.LookAtDirect(mapName == "Map2_5" ? new Vector3(-12,8,24) : new Vector3(0,7,-55),Quaternion.Euler(58,155,0),95);
                SceneView.lastActiveSceneView.Repaint();
            }
            Debug.Log("[Preset Art] " + mapName + ": three scene previews exported.");
        }

        static void ExportView(GameObject root,string filename,Quaternion rotation,Bounds? focus)
        {
            var preview=new PreviewRenderUtility();
            GameObject copy=Object.Instantiate(root);
            try
            {
                preview.AddSingleGO(copy);
                var rs=copy.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
                Bounds b=rs[0].bounds;
                foreach(var r in rs) b.Encapsulate(r.bounds);
                Bounds sceneBounds=b;
                if(focus.HasValue) b=focus.Value;
                const int width=1800,height=1600;
                Camera c=preview.camera;
                c.transform.rotation=rotation;
                c.transform.position=b.center-c.transform.forward*300;
                c.orthographic=true;
                // Static previews have a limited depth buffer. Restrict the clip
                // interval to the scene instead of losing precision to empty space.
                float depthMin=float.PositiveInfinity,depthMax=float.NegativeInfinity;
                for(int i=0;i<8;i++)
                {
                    Vector3 p=sceneBounds.center+Vector3.Scale(sceneBounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    float depth=Vector3.Dot(p-c.transform.position,c.transform.forward);
                    depthMin=Mathf.Min(depthMin,depth); depthMax=Mathf.Max(depthMax,depth);
                }
                c.nearClipPlane=Mathf.Max(.1f,depthMin-5); c.farClipPlane=depthMax+5;
                float horizontal=0,vertical=0;
                for(int i=0;i<8;i++)
                {
                    Vector3 p=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    p=Quaternion.Inverse(rotation)*(p-b.center);
                    horizontal=Mathf.Max(horizontal,Mathf.Abs(p.x));
                    vertical=Mathf.Max(vertical,Mathf.Abs(p.y));
                }
                c.orthographicSize=Mathf.Max(vertical,horizontal*height/width)*1.04f;
                c.clearFlags=CameraClearFlags.Color;
                c.backgroundColor=new Color(.105f,.15f,.18f,1);
                preview.lights[0].intensity=1.3f;
                preview.lights[0].transform.rotation=Quaternion.Euler(50,30,0);
                preview.lights[1].intensity=.6f;
                preview.ambientColor=new Color(.5f,.53f,.56f,1);
                preview.BeginStaticPreview(new Rect(0,0,width,height));
                preview.Render(true);
                Texture2D texture=preview.EndStaticPreview();
                File.WriteAllBytes(Path.Combine(Map2PresetAssetCatalog.Output,filename),texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }
            finally {preview.Cleanup();}
        }
    }
}

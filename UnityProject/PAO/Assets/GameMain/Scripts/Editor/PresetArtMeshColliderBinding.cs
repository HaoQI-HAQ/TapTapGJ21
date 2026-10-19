using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PAO.EditorTools
{
    /// <summary>
    /// Adds leaf mesh collisions to generated art, then disables only the proxy
    /// colliders identified by Map2PresetArtState. Does not rebuild the art.
    /// </summary>
    public static class PresetArtMeshColliderBinding
    {
        [Serializable]
        public sealed class Result
        {
            public string mapName;
            public string artRootName;
            public string scenePath;
            public string backupPath;
            public string utc;
            public int eligibleMeshFilters;
            public int meshColliderTotal;
            public int sceneMeshColliderTotal;
            public int addedMeshColliders;
            public int reusedMeshColliders;
            public int disabledProxyColliders;
            public int preservedTriggerColliders;
            public int preservedCustomArtColliders;
            public int protectedProxyColliders;
            public int dynamicConvexColliders;
            public int skippedInactiveOrHiddenMeshes;
            public bool saved;
            public bool rolledBack;

            [NonSerialized] internal Transaction transaction;
        }

        internal sealed class Transaction
        {
            internal GameObject artRoot;
            internal PresetArtMeshCollisionState state;
            internal bool createdState;
            internal StateSnapshot previousState;
            internal readonly List<MeshCollider> added = new List<MeshCollider>();
            internal readonly List<ColliderToggle> toggles = new List<ColliderToggle>();
        }

        internal sealed class ColliderToggle
        {
            internal Collider collider;
            internal bool enabled;
        }

        internal sealed class StateSnapshot
        {
            internal int bindingVersion, boundMeshFilters;
            internal string generatedUtc, backupPath;
            internal Collider[] sourceColliders;
            internal bool[] originalEnabled;
            internal MeshCollider[] generatedMeshColliders;

            internal StateSnapshot(PresetArtMeshCollisionState state)
            {
                bindingVersion = state.bindingVersion;
                boundMeshFilters = state.boundMeshFilters;
                generatedUtc = state.generatedUtc;
                backupPath = state.backupPath;
                sourceColliders = state.sourceColliders;
                originalEnabled = state.originalEnabled;
                generatedMeshColliders = state.generatedMeshColliders;
            }

            internal void Restore(PresetArtMeshCollisionState state)
            {
                state.bindingVersion = bindingVersion;
                state.boundMeshFilters = boundMeshFilters;
                state.generatedUtc = generatedUtc;
                state.backupPath = backupPath;
                state.sourceColliders = sourceColliders;
                state.originalEnabled = originalEnabled;
                state.generatedMeshColliders = generatedMeshColliders;
            }
        }

        sealed class MeshTarget
        {
            internal MeshFilter filter;
            internal MeshCollider existing;
            internal bool convex;
        }

        /// <summary>Back up, open additively if necessary, bind and save one existing map.</summary>
        public static Result Bind(string mapName)
        {
            EnsureEditMode();
            if (string.IsNullOrEmpty(mapName) || !Regex.IsMatch(mapName, @"^Map[0-9]+(?:_[0-9]+)?$"))
                throw new ArgumentException("Expected a map name such as Map2 or Map2_5.", nameof(mapName));

            string scenePath = "Assets/GameMain/Scenes/" + mapName + ".unity";
            string unityProject = Directory.GetParent(Application.dataPath).FullName;
            string absoluteScenePath = Path.Combine(unityProject, scenePath);
            if (!File.Exists(absoluteScenePath)) throw new FileNotFoundException("Map scene does not exist.", absoluteScenePath);
            string repository = Directory.GetParent(Directory.GetParent(unityProject).FullName).FullName;
            string output = Path.Combine(repository, "docs", "plan", "Map2美术替换");
            string backupFolder = Path.Combine(output, "backups");
            Directory.CreateDirectory(backupFolder);
            string backup = Path.Combine(backupFolder, mapName + "_before_mesh_collider_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".unity");

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool loaded = scene.IsValid() && scene.isLoaded;
            if (loaded && scene.isDirty)
            {
                if (!EditorSceneManager.SaveScene(scene, backup, true))
                    throw new IOException("Could not back up the unsaved map: " + backup);
            }
            else File.Copy(absoluteScenePath, backup, false);

            if (!loaded) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            GameObject whitebox = scene.GetRootGameObjects().SingleOrDefault(g => g.name == mapName + "_Whitebox");
            if (whitebox == null) throw new InvalidOperationException("Missing root " + mapName + "_Whitebox. Backup: " + backup);
            Transform art = whitebox.transform.Find(mapName + "_Preset_Art");
            if (art == null) throw new InvalidOperationException("Missing art root " + mapName + "_Preset_Art. Backup: " + backup);

            Result result = null;
            bool sceneSaved = false;
            try
            {
                result = ApplyToArtRoot(art.gameObject);
                result.mapName = mapName;
                result.scenePath = scenePath;
                result.backupPath = backup;
                result.transaction.state.backupPath = backup;
                EditorUtility.SetDirty(result.transaction.state);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new IOException("Could not save mesh collision changes to " + scenePath);
                sceneSaved = true;
                result.saved = true;
                string receipt = Path.Combine(output, mapName + "_mesh_collider_receipt.json");
                File.WriteAllText(receipt, JsonUtility.ToJson(result, true));
                Debug.Log("[Preset Mesh Collision] " + mapName + ": " + result.meshColliderTotal +
                    " art MeshColliders, " + result.addedMeshColliders + " added, " +
                    result.disabledProxyColliders + " proxy colliders disabled. Backup: " + backup);
                return result;
            }
            catch (Exception error)
            {
                if (result != null)
                {
                    try
                    {
                        Rollback(result);
                        if (sceneSaved && !EditorSceneManager.SaveScene(scene))
                            throw new IOException("In-memory rollback succeeded, but saving the rollback failed. Backup: " + backup);
                    }
                    catch (Exception rollbackError)
                    {
                        throw new AggregateException("Collision binding failed; rollback also reported an error. Backup: " + backup, error, rollbackError);
                    }
                }
                throw new InvalidOperationException("Collision binding failed. Backup: " + backup, error);
            }
        }

        /// <summary>
        /// Applies collisions in memory. The caller owns scene saving. All newly
        /// added colliders and changed enabled states are rolled back on failure.
        /// Call Rollback(result) if a later operation, such as saving, fails.
        /// </summary>
        public static Result ApplyToArtRoot(GameObject artRoot)
        {
            EnsureEditMode();
            if (artRoot == null) throw new ArgumentNullException(nameof(artRoot));
            if (!artRoot.scene.IsValid() || !artRoot.scene.isLoaded || EditorUtility.IsPersistent(artRoot))
                throw new InvalidOperationException("ApplyToArtRoot requires a loaded scene instance, not a prefab asset.");
            Map2PresetArtState provenance = artRoot.GetComponent<Map2PresetArtState>();
            if (provenance == null || provenance.sourceRenderers == null)
                throw new InvalidOperationException("The art root must contain Map2PresetArtState with sourceRenderers.");

            var result = new Result
            {
                artRootName = artRoot.name,
                scenePath = artRoot.scene.path,
                utc = DateTime.UtcNow.ToString("O"),
                transaction = new Transaction { artRoot = artRoot }
            };
            Transaction transaction = result.transaction;
            transaction.state = artRoot.GetComponent<PresetArtMeshCollisionState>();
            if (transaction.state != null) transaction.previousState = new StateSnapshot(transaction.state);

            // Preflight first. Existing colliders are never silently reconfigured.
            var targets = new List<MeshTarget>();
            foreach (MeshFilter filter in artRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled || !filter.gameObject.activeInHierarchy)
                {
                    result.skippedInactiveOrHiddenMeshes++;
                    continue;
                }
                Rigidbody body = FindAttachedBody(filter.transform);
                bool convex = body != null && !body.isKinematic;
                MeshCollider[] sameMesh = filter.GetComponents<MeshCollider>()
                    .Where(c => !c.isTrigger && c.sharedMesh == filter.sharedMesh).ToArray();
                MeshCollider existing = sameMesh.FirstOrDefault(c => c.enabled);
                if (sameMesh.Length > 0 && existing == null)
                    throw new InvalidOperationException("An existing matching MeshCollider is disabled on " + ObjectPath(filter.transform) +
                        ". Its custom state was preserved; no duplicate collider was added.");
                if (existing != null && convex && !existing.convex)
                    throw new InvalidOperationException("An existing non-convex collider belongs to a dynamic Rigidbody on " + ObjectPath(filter.transform) +
                        ". Its custom settings were preserved.");
                targets.Add(new MeshTarget { filter = filter, existing = existing, convex = convex });
            }
            if (targets.Count == 0)
                throw new InvalidOperationException("No enabled mesh-rendered art exists under " + artRoot.name + "; source collisions were left intact.");

            var sourceColliders = new HashSet<Collider>();
            foreach (Renderer source in provenance.sourceRenderers)
            {
                if (source == null) continue;
                if (source.gameObject.scene != artRoot.scene)
                    throw new InvalidOperationException("A source renderer belongs to a different scene: " + source.name);
                if (source.transform.IsChildOf(artRoot.transform))
                    throw new InvalidOperationException("sourceRenderers must reference original proxies outside the new art root: " + source.name);
                foreach (Collider collider in source.GetComponents<Collider>())
                {
                    if (collider.isTrigger) { result.preservedTriggerColliders++; continue; }
                    if (IsProtectedProxy(collider.transform)) { result.protectedProxyColliders++; continue; }
                    sourceColliders.Add(collider);
                }
            }
            result.preservedCustomArtColliders = artRoot.GetComponentsInChildren<Collider>(true).Length;

            try
            {
                // Finish every replacement before changing a single old support collider.
                foreach (MeshTarget target in targets)
                {
                    if (target.existing != null)
                    {
                        result.reusedMeshColliders++;
                        continue;
                    }
                    MeshCollider collider = target.filter.gameObject.AddComponent<MeshCollider>();
                    transaction.added.Add(collider);
                    collider.convex = target.convex;
                    collider.sharedMesh = target.filter.sharedMesh;
                    if (collider.sharedMesh == null) throw new InvalidOperationException("MeshCollider assignment failed on " + target.filter.name);
                    result.addedMeshColliders++;
                    if (target.convex) result.dynamicConvexColliders++;
                }

                var baseline = new Dictionary<Collider, bool>();
                if (transaction.previousState != null && transaction.previousState.sourceColliders != null)
                {
                    Collider[] old = transaction.previousState.sourceColliders;
                    bool[] states = transaction.previousState.originalEnabled;
                    for (int i = 0; i < old.Length; i++)
                        if (old[i] != null && states != null && i < states.Length) baseline[old[i]] = states[i];
                }
                foreach (Collider source in sourceColliders)
                {
                    if (!baseline.ContainsKey(source)) baseline.Add(source, source.enabled);
                    if (!source.enabled) continue;
                    transaction.toggles.Add(new ColliderToggle { collider = source, enabled = source.enabled });
                    source.enabled = false;
                    result.disabledProxyColliders++;
                }

                if (transaction.state == null)
                {
                    transaction.state = artRoot.AddComponent<PresetArtMeshCollisionState>();
                    transaction.createdState = true;
                }
                var generated = new HashSet<MeshCollider>();
                if (transaction.previousState != null && transaction.previousState.generatedMeshColliders != null)
                    foreach (MeshCollider collider in transaction.previousState.generatedMeshColliders)
                        if (collider != null && collider.transform.IsChildOf(artRoot.transform)) generated.Add(collider);
                foreach (MeshCollider collider in transaction.added) generated.Add(collider);
                Collider[] trackedSources = baseline.Keys.ToArray();
                transaction.state.bindingVersion = 1;
                transaction.state.generatedUtc = result.utc;
                transaction.state.sourceColliders = trackedSources;
                transaction.state.originalEnabled = trackedSources.Select(c => baseline[c]).ToArray();
                transaction.state.generatedMeshColliders = generated.ToArray();
                transaction.state.boundMeshFilters = targets.Count;
                result.eligibleMeshFilters = targets.Count;
                result.meshColliderTotal = artRoot.GetComponentsInChildren<MeshCollider>(true).Length;
                result.sceneMeshColliderTotal = artRoot.scene.GetRootGameObjects().Sum(g => g.GetComponentsInChildren<MeshCollider>(true).Length);
                EditorUtility.SetDirty(transaction.state);
                EditorSceneManager.MarkSceneDirty(artRoot.scene);
                return result;
            }
            catch
            {
                Rollback(result);
                throw;
            }
        }

        /// <summary>Restore the exact in-memory state before this Result's application.</summary>
        public static void Rollback(Result result)
        {
            if (result == null || result.transaction == null || result.rolledBack) return;
            Transaction transaction = result.transaction;
            for (int i = transaction.toggles.Count - 1; i >= 0; i--)
                if (transaction.toggles[i].collider != null)
                    transaction.toggles[i].collider.enabled = transaction.toggles[i].enabled;
            for (int i = transaction.added.Count - 1; i >= 0; i--)
                if (transaction.added[i] != null) Object.DestroyImmediate(transaction.added[i]);
            if (transaction.createdState)
            {
                if (transaction.state != null) Object.DestroyImmediate(transaction.state);
            }
            else if (transaction.state != null && transaction.previousState != null)
            {
                transaction.previousState.Restore(transaction.state);
                EditorUtility.SetDirty(transaction.state);
            }
            result.saved = false;
            result.rolledBack = true;
            if (transaction.artRoot != null && transaction.artRoot.scene.IsValid() && transaction.artRoot.scene.isLoaded)
                EditorSceneManager.MarkSceneDirty(transaction.artRoot.scene);
        }

        static Rigidbody FindAttachedBody(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                Rigidbody body = current.GetComponent<Rigidbody>();
                if (body != null) return body;
            }
            return null;
        }

        static bool IsProtectedProxy(Transform transform)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                string name = current.name;
                if (name == "Mechanisms" || name == "Breakable" || name == "Landmarks") return true;
                if (current.GetComponent<Rigidbody>() != null || current.GetComponent<Joint>() != null) return true;
            }
            return false;
        }

        static string ObjectPath(Transform transform)
        {
            var names = new List<string>();
            for (Transform current = transform; current != null; current = current.parent) names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }

        static void EnsureEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before binding preset art collisions.");
        }
    }
}

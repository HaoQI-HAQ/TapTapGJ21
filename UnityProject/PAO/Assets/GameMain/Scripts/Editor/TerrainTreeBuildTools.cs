using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PAO.EditorTools
{
    /// <summary>Audits terrain references, repairs unused missing slots, and verifies player builds.</summary>
    public static class TerrainTreeBuildTools
    {
        private static string ReportDirectory => Path.GetFullPath("../../docs/diagnostics/2026-10-21-terrain-build");

        [Serializable]
        private sealed class PrototypeInfo
        {
            public int index;
            public string prefabPath;
            public string prefabName;
            public bool missing;
            public int instanceCount;
        }

        [Serializable]
        private sealed class TerrainInfo
        {
            public string path;
            public string guid;
            public int treeInstanceCount;
            public int invalidInstanceCount;
            public List<string> buildScenes = new List<string>();
            public List<PrototypeInfo> prototypes = new List<PrototypeInfo>();
        }

        [Serializable]
        private sealed class AuditReport
        {
            public string utc;
            public string buildTarget;
            public string buildLocation;
            public int missingPrototypeCount;
            public List<TerrainInfo> terrains = new List<TerrainInfo>();
        }

        [Serializable]
        private sealed class RepairInfo
        {
            public string path;
            public string backup;
            public int[] removedPrototypeIndices;
            public int originalInstanceCount;
            public int resultingInstanceCount;
            public int remappedInstanceCount;
        }

        [Serializable]
        private sealed class RepairReport
        {
            public List<RepairInfo> changes = new List<RepairInfo>();
        }

        [Serializable]
        private sealed class PlayerBuildResult
        {
            public string result;
            public string outputPath;
            public string target;
            public int errors;
            public int warnings;
            public double seconds;
            public List<string> errorMessages = new List<string>();
        }

        [Serializable]
        private sealed class TempDirectoryReport
        {
            public string tempEnvironment;
            public string tmpEnvironment;
            public string tempPath;
            public bool canCreateTempFile;
            public string exception;
        }

        [MenuItem("PAO/Validation/Audit Build Temp Directory")]
        public static void AuditTempDirectory()
        {
            var report = new TempDirectoryReport
            {
                tempEnvironment = Environment.GetEnvironmentVariable("TEMP"),
                tmpEnvironment = Environment.GetEnvironmentVariable("TMP"),
                tempPath = Path.GetTempPath()
            };
            try
            {
                string probe = Path.GetTempFileName();
                File.Delete(probe);
                report.canCreateTempFile = true;
            }
            catch (Exception exception)
            {
                report.exception = exception.ToString();
            }
            Directory.CreateDirectory(ReportDirectory);
            File.WriteAllText(Path.Combine(ReportDirectory, "temp-directory-audit.json"), JsonUtility.ToJson(report, true));
        }

        [MenuItem("PAO/Validation/Audit Terrain Tree Prototypes")]
        public static void Audit()
        {
            var report = new AuditReport
            {
                utc = DateTime.UtcNow.ToString("o"),
                buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                buildLocation = EditorUserBuildSettings.GetBuildLocation(EditorUserBuildSettings.activeBuildTarget)
            };
            var enabledScenes = EditorBuildSettings.scenes.Where(s => s.enabled).ToArray();
            var dependencies = enabledScenes.ToDictionary(s => s.path,
                s => new HashSet<string>(AssetDatabase.GetDependencies(s.path, true)));
            foreach (string guid in AssetDatabase.FindAssets("t:TerrainData", new[] { "Assets" }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var terrain = AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath);
                if (terrain == null) continue;
                var prototypes = terrain.treePrototypes;
                var instances = terrain.treeInstances;
                var info = new TerrainInfo
                {
                    path = assetPath,
                    guid = guid,
                    treeInstanceCount = instances.Length,
                    invalidInstanceCount = instances.Count(t => t.prototypeIndex < 0 || t.prototypeIndex >= prototypes.Length),
                    buildScenes = dependencies.Where(d => d.Value.Contains(assetPath)).Select(d => d.Key).ToList()
                };
                for (int i = 0; i < prototypes.Length; i++)
                {
                    var prefab = prototypes[i] == null ? null : prototypes[i].prefab;
                    bool missing = prefab == null;
                    if (missing) report.missingPrototypeCount++;
                    info.prototypes.Add(new PrototypeInfo
                    {
                        index = i,
                        prefabPath = prefab == null ? "" : AssetDatabase.GetAssetPath(prefab),
                        prefabName = prefab == null ? "" : prefab.name,
                        missing = missing,
                        instanceCount = instances.Count(t => t.prototypeIndex == i)
                    });
                }
                report.terrains.Add(info);
            }
            Directory.CreateDirectory(ReportDirectory);
            File.WriteAllText(Path.Combine(ReportDirectory, "terrain-audit.json"), JsonUtility.ToJson(report, true));
            Debug.Log($"[TerrainTreeAudit] {report.terrains.Count} terrains; {report.missingPrototypeCount} missing prototypes. Report: {ReportDirectory}");
        }

        [MenuItem("PAO/Validation/Repair Unused Missing Terrain Tree Slots")]
        public static void RepairUnusedMissingSlots()
        {
            var report = new RepairReport();
            string backupDirectory = Path.Combine(ReportDirectory, "backups", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            foreach (string guid in AssetDatabase.FindAssets("t:TerrainData", new[] { "Assets" }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var terrain = AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath);
                if (terrain == null) continue;
                var prototypes = terrain.treePrototypes;
                var instances = terrain.treeInstances;
                int[] missing = Enumerable.Range(0, prototypes.Length)
                    .Where(i => prototypes[i] == null || prototypes[i].prefab == null).ToArray();
                if (missing.Length == 0) continue;
                var missingSet = new HashSet<int>(missing);
                if (instances.Any(t => t.prototypeIndex < 0 || t.prototypeIndex >= prototypes.Length || missingSet.Contains(t.prototypeIndex)))
                    throw new InvalidOperationException($"{assetPath}: missing prototype is used by painted instances, or an instance has an invalid index. Restore the original prefab before repairing.");

                Directory.CreateDirectory(backupDirectory);
                string backup = Path.Combine(backupDirectory, Path.GetFileName(assetPath));
                File.Copy(assetPath, backup, false);
                File.Copy(assetPath + ".meta", backup + ".meta", false);
                var mapping = new int[prototypes.Length];
                var retained = new List<TreePrototype>();
                for (int i = 0; i < prototypes.Length; i++)
                {
                    mapping[i] = missingSet.Contains(i) ? -1 : retained.Count;
                    if (mapping[i] >= 0) retained.Add(prototypes[i]);
                }
                var mapped = (TreeInstance[])instances.Clone();
                int remappedCount = 0;
                for (int i = 0; i < mapped.Length; i++)
                {
                    int newIndex = mapping[mapped[i].prototypeIndex];
                    if (newIndex != mapped[i].prototypeIndex) remappedCount++;
                    mapped[i].prototypeIndex = newIndex;
                }

                Undo.RegisterCompleteObjectUndo(terrain, "Repair unused missing terrain tree slots");
                // Clear only the in-memory instances while changing the prototype array, so Unity
                // cannot discard valid trees whose old indices would temporarily be out of range.
                terrain.SetTreeInstances(Array.Empty<TreeInstance>(), false);
                terrain.treePrototypes = retained.ToArray();
                terrain.SetTreeInstances(mapped, false);
                var actual = terrain.treeInstances;
                if (!actual.SequenceEqual(mapped) || actual.Length != instances.Length)
                {
                    terrain.SetTreeInstances(Array.Empty<TreeInstance>(), false);
                    terrain.treePrototypes = prototypes;
                    terrain.SetTreeInstances(instances, false);
                    throw new InvalidOperationException($"{assetPath}: instance preservation check failed; restored original data in memory.");
                }
                terrain.RefreshPrototypes();
                EditorUtility.SetDirty(terrain);
                AssetDatabase.SaveAssetIfDirty(terrain);
                report.changes.Add(new RepairInfo
                {
                    path = assetPath,
                    backup = backup,
                    removedPrototypeIndices = missing,
                    originalInstanceCount = instances.Length,
                    resultingInstanceCount = actual.Length,
                    remappedInstanceCount = remappedCount
                });
            }
            Directory.CreateDirectory(ReportDirectory);
            File.WriteAllText(Path.Combine(ReportDirectory, "terrain-repair.json"), JsonUtility.ToJson(report, true));
            Audit();
            Debug.Log($"[TerrainTreeRepair] Repaired {report.changes.Count} terrains. All painted instances preserved.");
        }

        [MenuItem("PAO/Validation/Validate Player Build")]
        public static void ValidatePlayerBuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Stop play mode or wait for the current build before verification.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException("This verification output is configured for the active Windows 64-bit target.");
            Directory.CreateDirectory(ReportDirectory);
            string reportPath = Path.Combine(ReportDirectory, "player-build.json");
            var result = new PlayerBuildResult
            {
                result = "Running",
                target = EditorUserBuildSettings.activeBuildTarget.ToString(),
                outputPath = Path.GetFullPath("output/terrain-build-validation-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "/PAO.exe")
            };
            File.WriteAllText(reportPath, JsonUtility.ToJson(result, true));
            {
                try
                {
                    result.result = "Running";
                    File.WriteAllText(reportPath, JsonUtility.ToJson(result, true));
                    Directory.CreateDirectory(Path.GetDirectoryName(result.outputPath));
                    var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                    {
                        scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                        target = EditorUserBuildSettings.activeBuildTarget,
                        locationPathName = result.outputPath,
                        options = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None
                    });
                    result.result = report.summary.result.ToString();
                    result.errors = (int)report.summary.totalErrors;
                    result.warnings = (int)report.summary.totalWarnings;
                    result.seconds = report.summary.totalTime.TotalSeconds;
                    foreach (var step in report.steps)
                        foreach (var message in step.messages)
                            if (message.type == LogType.Error || message.type == LogType.Exception)
                                result.errorMessages.Add(message.content);
                }
                catch (Exception exception)
                {
                    result.result = "Exception";
                    result.errorMessages.Add(exception.ToString());
                }
                File.WriteAllText(reportPath, JsonUtility.ToJson(result, true));
            }
        }
    }
}

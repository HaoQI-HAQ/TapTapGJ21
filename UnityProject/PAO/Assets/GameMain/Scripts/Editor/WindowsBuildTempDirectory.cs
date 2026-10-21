using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace PAO.EditorTools
{
    /// <summary>Gives native Windows build tools a project-owned temporary directory.</summary>
    public sealed class WindowsBuildTempDirectory : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private static bool active;
        private static string previousTemp;
        private static string previousTmp;

        public int callbackOrder => int.MinValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64 &&
                report.summary.platform != BuildTarget.StandaloneWindows)
                return;
            Restore();
            string scratch = Path.GetFullPath("Temp/WindowsBuildScratch");
            Directory.CreateDirectory(scratch);
            string probe = Path.Combine(scratch, Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(probe, "Build temporary directory write probe.");
            File.Delete(probe);
            previousTemp = Environment.GetEnvironmentVariable("TEMP");
            previousTmp = Environment.GetEnvironmentVariable("TMP");
            active = true;
            try
            {
                Environment.SetEnvironmentVariable("TEMP", scratch);
                Environment.SetEnvironmentVariable("TMP", scratch);
                EditorApplication.update += RestoreWhenBuildEnds;
            }
            catch
            {
                Restore();
                throw;
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            Restore();
        }

        private static void RestoreWhenBuildEnds()
        {
            // Postprocess callbacks are skipped for failed builds.
            if (!BuildPipeline.isBuildingPlayer) Restore();
        }

        private static void Restore()
        {
            if (!active) return;
            Environment.SetEnvironmentVariable("TEMP", previousTemp);
            Environment.SetEnvironmentVariable("TMP", previousTmp);
            active = false;
            EditorApplication.update -= RestoreWhenBuildEnds;
        }
    }
}

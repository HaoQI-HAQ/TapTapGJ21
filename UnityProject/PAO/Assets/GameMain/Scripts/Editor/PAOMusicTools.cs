using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PAO.AV.Editor
{
    /// <summary>
    /// 成品曲目的导入参数重导 + 响度测量。
    /// 批处理：
    ///   -executeMethod PAO.AV.Editor.PAOMusicTools.ReapplyCLI
    ///   -executeMethod PAO.AV.Editor.PAOMusicTools.MeasureCLI
    /// </summary>
    public static class PAOMusicTools
    {
        private const string CustomRoot = "Assets/GameMain/Resources/PAOAudio/Music/Custom";

        /// <summary>
        /// 重新导入 Music/Custom 下的音频，让 <see cref="PAOAudioImportSettings"/> 真正生效。
        /// 首次把文件放进工程时，资源可能赶在脚本编译完成之前就被导入，
        /// 那样后处理跑的是旧版本，曲目会留在 PCM / 60 MB 的状态。
        /// </summary>
        [MenuItem("Tools/PAO/Audio/Reapply Featured Import Settings")]
        public static void ReapplyCLI()
        {
            Reapply();
            LogSettings();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Reapply()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { CustomRoot });
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string guid in guids)
                    AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }

        private static void LogSettings()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { CustomRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) continue;
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                Debug.Log(string.Format("[PAOMusic] {0}: load={1} format={2} quality={3:F2} preload={4} background={5}",
                    Path.GetFileName(path), settings.loadType, settings.compressionFormat, settings.quality,
                    settings.preloadAudioData, importer.loadInBackground));
            }
        }

        [MenuItem("Tools/PAO/Audio/Measure Featured Music")]
        public static void MeasureCLI()
        {
            Reapply();
            var report = new StringBuilder();
            report.AppendLine("path\tsamples\tchannels\tfrequency\trms\tpeak\tdb_rms\tpeak_gain\trms_gain");
            report.AppendLine("# rms_gain = 让有效 RMS 落在 -26 dBFS 的 gain 建议值");
            PAOAudioImportSettings.Suspended = true;
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { CustomRoot }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (importer == null) continue;
                    AudioImporterSampleSettings original = importer.defaultSampleSettings;
                    try
                    {
                        AudioImporterSampleSettings settings = original;
                        settings.loadType = AudioClipLoadType.DecompressOnLoad;
                        settings.preloadAudioData = true;
                        importer.defaultSampleSettings = settings;
                        importer.SaveAndReimport();
                        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                        if (clip == null) { report.AppendLine(path + "\tLOAD FAILED"); continue; }
                        var data = new float[clip.samples * clip.channels];
                        if (!clip.GetData(data, 0)) { report.AppendLine(path + "\tGETDATA FAILED"); continue; }
                        double sum = 0d;
                        float peak = 0f;
                        for (int i = 0; i < data.Length; i++)
                        {
                            float value = data[i];
                            sum += (double)value * value;
                            float magnitude = Mathf.Abs(value);
                            if (magnitude > peak) peak = magnitude;
                        }
                        double rms = Math.Sqrt(sum / data.Length);
                        const double targetRms = 0.05d; // ≈ -26 dBFS，玩家默认音乐滑条 0.8 下的有效电平
                        double rmsGain = rms > 1e-9 ? targetRms / (rms * 0.8d) : 0d;
                        double peakGain = peak > 1e-9 ? 0.9d / (peak * 0.8d * 0.8d) : 0d;
                        report.AppendLine(string.Format(
                            "{0}\t{1}\t{2}\t{3}\t{4:F5}\t{5:F5}\t{6:F2}\t{7:F3}\t{8:F3}",
                            path, clip.samples, clip.channels, clip.frequency, rms, peak,
                            20d * Math.Log10(Math.Max(rms, 1e-9)), peakGain, rmsGain));
                    }
                    catch (Exception error)
                    {
                        report.AppendLine(path + "\tERROR " + error.Message);
                    }
                    finally
                    {
                        importer.defaultSampleSettings = original;
                        importer.SaveAndReimport();
                    }
                }
            }
            finally
            {
                PAOAudioImportSettings.Suspended = false;
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/music-loudness.txt", report.ToString());
            LogSettings();
            Debug.Log("[PAOMusic] " + report);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace PAO.AV.Editor
{
    /// <summary>Keep authored loop samples intact and use a small memory footprint.</summary>
    public sealed class PAOAudioImportSettings : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/GameMain/Resources/PAOAudio/")) return;
            AudioImporter importer = (AudioImporter)assetImporter;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.loadType = assetPath.Contains("/SFX/")
                ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.Streaming;
            importer.defaultSampleSettings = settings;
        }
    }
}

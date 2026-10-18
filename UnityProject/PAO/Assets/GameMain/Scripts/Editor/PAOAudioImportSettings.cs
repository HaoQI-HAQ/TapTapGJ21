using UnityEditor;
using UnityEngine;

namespace PAO.AV.Editor
{
    /// <summary>Keep authored loop samples intact and use a small memory footprint.</summary>
    public sealed class PAOAudioImportSettings : AssetPostprocessor
    {
        /// <summary>用户提供的成品背景乐目录，按长音频处理。</summary>
        private const string FeaturedMusic = "Assets/GameMain/Resources/PAOAudio/Music/Custom/";

        /// <summary>测量响度时临时挂起，否则后处理会把导入参数又改回 Vorbis。</summary>
        internal static bool Suspended;

        private void OnPreprocessAudio()
        {
            if (Suspended) return;
            if (!assetPath.StartsWith("Assets/GameMain/Resources/PAOAudio/")) return;
            AudioImporter importer = (AudioImporter)assetImporter;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            if (assetPath.StartsWith(FeaturedMusic, System.StringComparison.Ordinal))
            {
                // 成品曲目：6 分钟立体声 44.1 kHz 的 PCM 大约 63 MB/首。
                // 这种长度只能用压缩流式播放，Vorbis 0.7 听不出和原始 MP3 的差别。
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = .7f;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                settings.loadType = AudioClipLoadType.Streaming;
                settings.preloadAudioData = false;
                importer.loadInBackground = true;
                importer.forceToMono = false;
            }
            else
            {
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                settings.loadType = assetPath.Contains("/SFX/")
                    ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.Streaming;
            }
            importer.defaultSampleSettings = settings;
        }
    }
}

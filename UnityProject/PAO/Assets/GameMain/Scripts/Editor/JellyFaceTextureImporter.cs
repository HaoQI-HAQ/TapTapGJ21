// ---------------------------------------------------------------------------
// PAO / JellyFaceTextureImporter —— 水母表情贴图的自动导入设置
//
// 为什么需要它：
//   表情贴图是 1024² 的「彩色线条 + 透明底」PNG，进 Unity 默认设置会踩三个坑：
//     1. 默认 Wrap Mode 是 Repeat —— 面片被铃铛轮廓裁过，UV 到不了 0/1 边界，
//        Repeat 会让边缘像素从对边卷回来，脸上出现莫名其妙的线；
//     2. 默认不认 alpha 是透明度，mipmap 生成时会把透明区域的杂色渗进边缘；
//     3. 平台压缩格式选错会直接把颜色搞丢 —— 见下面 Format 那段的说明。
//
// 这张图是「彩色」的，所以格式必须带 RGB：
//   ✅ RGBA32（当前，方案 A）—— 不压缩，颜色与 alpha 都无损，1024² 带 mipmap ≈ 5.6 MB/张
//   ✅ DXT5              —— 要压体积时换这个（8 位 alpha、无 alpha 劈裂）≈ 1.4 MB/张
//   ❌ Alpha8            —— 只有 alpha 通道，RGB 整条丢失 → 场景里表情变黑白
//
// 用法：
//   正常导入 / 右键 Reimport 即可，设置是自动应用的。
//   改完这个脚本后，对表情文件夹右键 → Reimport 一次才会重新生效。
// ---------------------------------------------------------------------------
using UnityEditor;
using UnityEngine;

namespace PAO.EditorTools
{
    public class JellyFaceTextureImporter : AssetPostprocessor
    {
        // 只有这个目录下的贴图会被处理，避免误伤项目里其它 PNG
        private const string ExpressionFolder = "/Art/Textures/低面/水母头身/表情/";

        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains(ExpressionFolder))
                return;
            if (!assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                return;

            var importer = (TextureImporter)assetImporter;

            importer.textureType         = TextureImporterType.Default;
            importer.sRGBTexture         = true;                 // 线条是有颜色的（黑墨 + 粉腮红 + 黄星星）
            importer.alphaIsTransparency = true;
            importer.alphaSource         = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled       = true;                 // 拉远时不闪烁
            importer.wrapMode            = TextureWrapMode.Clamp; // 关键：不能 Repeat
            importer.filterMode          = FilterMode.Bilinear;
            importer.anisoLevel          = 4;
            importer.npotScale           = TextureImporterNPOTScale.None;

            // ⚠️ 格式必须是「带颜色」的。
            //    曾经用过 Alpha8 —— 那是个只有 alpha 通道的格式，RGB 会被整条丢掉，
            //    结果就是场景里表情只剩黑白（Inspector 预览读的是磁盘 PNG，所以还带粉色，
            //    看起来很像"导入丢色"，其实是压缩格式选错了）。
            //    现在按方案 A 用 RGBA32：完全不压缩，颜色和 alpha 都逐像素无损。
            //    如果以后要压体积，换成 DXT5（带 8 位 alpha、无 alpha 劈裂）即可。
            const TextureImporterFormat Format = TextureImporterFormat.RGBA32;
            const TextureImporterCompression Compression = TextureImporterCompression.Uncompressed;
            const int MaxSize = 1024;

            foreach (var platform in new[] { "Standalone", "Android", "iPhone" })
            {
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name               = platform,
                    overridden         = true,
                    maxTextureSize     = MaxSize,
                    format             = Format,
                    textureCompression = Compression,
                });
            }
        }
    }
}

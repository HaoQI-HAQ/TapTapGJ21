using System;
using UnityEngine;

namespace PAO.AV
{
    /// <summary>
    /// 背景音乐目录：场景名 → 曲目。
    ///
    /// 数据放在 <c>Resources/PAOAudio/MusicCatalog.json</c>，运行时不编译就能改：
    /// 换歌、调增益、给某个场景单独指定曲子，都只改这个 JSON。
    /// 没在 JSON 里写到的场景会先落到 <c>scene == "*"</c> 的兜底条目，
    /// 再落到老规则 <c>Resources/PAOAudio/Music/&lt;场景名&gt;_Base</c>。
    ///
    /// 音量不在这里定死：这里的 <see cref="Entry.gain"/> 只是相对增益，
    /// 真正的总音量是设置里的「音乐」滑条（<see cref="PAO.UI.V2.UISettingsData.music"/>）。
    /// </summary>
    [Serializable]
    public sealed class PAOMusicCatalog
    {
        /// <summary>Resources 下的路径，不含扩展名。</summary>
        public const string ResourcePath = "PAOAudio/MusicCatalog";

        [Serializable]
        public sealed class Entry
        {
            [Tooltip("场景名；\"*\" 表示没单独写到的场景都用它")]
            public string scene = "*";

            [Tooltip("Resources 下的路径，不含扩展名。留空则回退到 <场景名>_Base")]
            public string clip = string.Empty;

            [Tooltip("相对增益，1 = 原始响度")]
            [Range(0f, 1f)] public float gain = .34f;

            [Tooltip("随暂停菜单一起暂停")]
            public bool pauseWithGameplay = true;

            [Tooltip("用户曲目下关掉原合成音乐的 Pulse / Tension 叠层，避免两个调性打架")]
            public bool muteSynthLayers = true;
        }

        public Entry[] entries = new Entry[0];

        /// <summary>读 Resources 里的 JSON。缺失或格式错误都只告警，不打断游戏。</summary>
        public static PAOMusicCatalog Load()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[PAOMusicCatalog] 找不到 Resources/" + ResourcePath + ".json，改用 <场景名>_Base 老规则。");
                return null;
            }
            try
            {
                PAOMusicCatalog catalog = JsonUtility.FromJson<PAOMusicCatalog>(asset.text);
                if (catalog == null || catalog.entries == null || catalog.entries.Length == 0)
                {
                    Debug.LogWarning("[PAOMusicCatalog] " + ResourcePath + ".json 里没有 entries。");
                    return null;
                }
                return catalog;
            }
            catch (Exception error)
            {
                Debug.LogWarning("[PAOMusicCatalog] " + ResourcePath + ".json 解析失败：" + error.Message);
                return null;
            }
        }

        /// <summary>先找精确场景名，再找 "*" 兜底。</summary>
        public Entry Resolve(string sceneName)
        {
            if (entries == null) return null;
            Entry fallback = null;
            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.scene)) continue;
                if (entry.scene == sceneName) return entry;
                if (entry.scene == "*") fallback = entry;
            }
            return fallback;
        }
    }
}

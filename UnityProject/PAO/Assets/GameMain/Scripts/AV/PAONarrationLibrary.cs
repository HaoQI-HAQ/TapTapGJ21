using System;
using UnityEngine;

namespace PAO.AV
{
    [CreateAssetMenu(menuName = "PAO/Audio/Narration Library", fileName = "NarrationLibrary")]
    public sealed class PAONarrationLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Stable key, e.g. Map4/Narration_Start")]
            public string id;
            [TextArea(2, 4)] public string transcript;
            public AudioClip clip;
        }

        public Entry[] entries = new Entry[0];

        public AudioClip Find(string id)
        {
            if (string.IsNullOrEmpty(id) || entries == null) return null;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i] != null && string.Equals(entries[i].id, id, StringComparison.Ordinal))
                    return entries[i].clip;
            return null;
        }
    }
}

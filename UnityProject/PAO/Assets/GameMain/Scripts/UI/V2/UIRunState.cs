using System;
using System.Collections.Generic;
using UnityEngine;

namespace PAO.UI.V2
{
    [Serializable]
    public sealed class MemoryEntry
    {
        public string id;
        public string chapter;
        public string title;
        [TextArea] public string body;
    }

    [Serializable]
    public sealed class RunProgress
    {
        public List<string> completed = new List<string>();
        public List<MemoryEntry> memories = new List<MemoryEntry>();
        public List<string> tutorialsSeen = new List<string>();
        public string ending = "";
        public float playSeconds;
        public int bubbleType;
        public int buildTokens = 6;
        public List<BuiltItem> builds = new List<BuiltItem>();
        public void Validate()
        {
            if (completed == null) completed = new List<string>();
            if (memories == null) memories = new List<MemoryEntry>();
            if (tutorialsSeen == null) tutorialsSeen = new List<string>();
            if (builds == null) builds = new List<BuiltItem>();
            buildTokens = Mathf.Max(0, buildTokens);
            bubbleType = Mathf.Clamp(bubbleType, 0, 2);
            playSeconds = Mathf.Max(0, playSeconds);
        }
    }

    public static class UIRunState
    {
        public const int AutoSlot = 3;
        public static RunProgress Progress { get; private set; } = new RunProgress();
        public static SaveSlotData PendingLoad { get; set; }
        public static event Action Changed;

        public static void ResetSession()
        {
            Progress = new RunProgress(); PendingLoad = null; Changed = null;
        }

        public static void NewRun() { Progress = new RunProgress(); PendingLoad = null; Notify(); }
        public static void Apply(SaveSlotData data)
        {
            Progress = data.runProgress ?? new RunProgress();
            Progress.Validate();
            Notify();
        }

        public static bool IsUnlocked(UIConfig config, int index)
        {
            return index == 0 || (index > 0 && index < config.chapters.Length &&
                   Progress.completed.Contains(config.chapters[index - 1].scene));
        }

        public static SaveSlotData Latest()
        {
            SaveSlotData latest = null;
            for (int i = 0; i <= AutoSlot; i++)
            {
                SaveSlotData data = SaveSystem.LoadSlot(i);
                if (data == null || !Application.CanStreamedLevelBeLoaded(data.sceneName)) continue;
                if (latest == null || (data.SavedAtLocal ?? DateTime.MinValue) >
                    (latest.SavedAtLocal ?? DateTime.MinValue)) latest = data;
            }
            return latest;
        }

        public static bool AddMemory(MemoryEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id) ||
                Progress.memories.Exists(m => m != null && m.id == entry.id)) return false;
            Progress.memories.Add(entry); Notify(); return true;
        }

        public static void Complete(string scene)
        {
            if (!Progress.completed.Contains(scene)) Progress.completed.Add(scene);
            Notify();
        }
        public static void Notify() { if (Changed != null) Changed(); }
    }
}

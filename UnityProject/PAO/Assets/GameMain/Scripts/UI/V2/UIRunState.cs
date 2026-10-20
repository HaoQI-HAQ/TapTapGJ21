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
        /// <summary>已经解除首周目能力限制的关卡（通关，或从章节地图进入过）。</summary>
        public List<string> abilityTutorialsCleared = new List<string>();
        public string ending = "";
        public float playSeconds;
        public int bubbleType;
        public int buildTokens = 6;
        public List<BuiltItem> builds = new List<BuiltItem>();
        public RunProgress Copy()
        {
            RunProgress copy = JsonUtility.FromJson<RunProgress>(JsonUtility.ToJson(this));
            copy.Validate(); return copy;
        }
        public void Validate()
        {
            if (completed == null) completed = new List<string>();
            if (memories == null) memories = new List<MemoryEntry>();
            if (tutorialsSeen == null) tutorialsSeen = new List<string>();
            if (abilityTutorialsCleared == null) abilityTutorialsCleared = new List<string>();
            if (builds == null) builds = new List<BuiltItem>();
            NormalizeIds(completed); NormalizeIds(tutorialsSeen); NormalizeIds(abilityTutorialsCleared);
            var memoryIds = new HashSet<string>();
            memories.RemoveAll(m => m == null || string.IsNullOrEmpty(m.id) || !memoryIds.Add(m.id));
            var buildIds = new HashSet<string>();
            builds.RemoveAll(b => b == null || string.IsNullOrEmpty(b.site) || string.IsNullOrEmpty(b.scene) ||
                string.IsNullOrEmpty(b.recipe) || b.cost < 0 || !IsFinite(b.rotation) || !buildIds.Add(b.scene + ":" + b.site));
            buildTokens = Mathf.Max(0, buildTokens);
            bubbleType = Mathf.Clamp(bubbleType, 0, 2);
            playSeconds = IsFinite(playSeconds) ? Mathf.Clamp(playSeconds, 0, 315360000) : 0;
            if (ending == null) ending = "";
        }
        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static void NormalizeIds(List<string> values)
        {
            var ids = new HashSet<string>();
            values.RemoveAll(id => string.IsNullOrEmpty(id) || !ids.Add(id));
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
            Progress = data != null && data.runProgress != null ? data.runProgress.Copy() : new RunProgress();
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
            Progress.memories.Add(new MemoryEntry { id = entry.id, chapter = entry.chapter, title = entry.title, body = entry.body });
            Notify(); return true;
        }

        public static void Complete(string scene)
        {
            if (!Progress.completed.Contains(scene)) Progress.completed.Add(scene);
            // 通关即视为这一关的能力教学结束：下次再进来开放全部泡泡。
            MarkAbilityTutorialCleared(scene);
            Notify();
        }

        /// <summary>解除某关的首周目能力限制（从章节地图进入时调用，规则见 BubbleAbilityRules）。</summary>
        public static void ClearAbilityTutorial(string scene)
        {
            if (MarkAbilityTutorialCleared(scene)) Notify();
        }

        /// <summary>只改数据、不派发事件；返回是否真的改动了。</summary>
        private static bool MarkAbilityTutorialCleared(string scene)
        {
            if (string.IsNullOrEmpty(scene)) return false;
            if (Progress.abilityTutorialsCleared == null)
                Progress.abilityTutorialsCleared = new List<string>();
            if (Progress.abilityTutorialsCleared.Contains(scene)) return false;
            Progress.abilityTutorialsCleared.Add(scene);
            return true;
        }

        public static void Notify() { if (Changed != null) Changed(); }
    }
}

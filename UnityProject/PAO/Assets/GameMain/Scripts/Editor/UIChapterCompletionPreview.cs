#if UNITY_EDITOR
using System.Reflection;
using PAO.UI.V2;
using UnityEditor;
using UnityEngine;

namespace PAO.EditorTools
{
    [InitializeOnLoad]
    public static class UIChapterCompletionPreview
    {
        private const string Requested = "PAO.UI.ChapterCompletionPreview";
        private static double deadline;
        static UIChapterCompletionPreview() { EditorApplication.update += TryShow; }

        [MenuItem("PAO/UI/V2/预览章节完成跳转页", false, 46)]
        public static void Open()
        {
            SessionState.SetBool(Requested, true);
            deadline = EditorApplication.timeSinceStartup + 60;
            if (!EditorApplication.isPlaying) EditorApplication.isPlaying = true;
        }

        private static void TryShow()
        {
            if (!SessionState.GetBool(Requested, false)) return;
            if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 60;
            if (EditorApplication.timeSinceStartup > deadline)
            {
                SessionState.SetBool(Requested, false);
                Debug.LogWarning("章节完成页预览等待超时，请确认当前场景已启用 PAO UI V2。");
                return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            UIRuntime runtime = UIRuntime.Instance;
            if (runtime == null || runtime.Config == null) return;
            FieldInfo loading = typeof(UIRuntime).GetField("loading", BindingFlags.Instance | BindingFlags.NonPublic);
            if (loading != null && (bool)loading.GetValue(runtime)) return;
            SessionState.SetBool(Requested, false);
            runtime.ShowChapterCompletionStylePreview();
        }
    }
}
#endif

using UnityEditor;
using UnityEngine;

namespace PAO.EditorTools
{
    /// <summary>Keep speaker debug icons out of gameplay previews without changing audio.</summary>
    [InitializeOnLoad]
    public static class PAOAudioIconVisibility
    {
        static PAOAudioIconVisibility()
        {
            EditorApplication.delayCall += HideIcons;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) HideIcons();
            };
        }

        [MenuItem("Tools/PAO/环境氛围/隐藏音频调试图标")]
        public static void HideIcons()
        {
            HideIcon(typeof(AudioSource));
            HideIcon(typeof(AudioListener));
            SceneView.RepaintAll();
        }

        static void HideIcon(System.Type type)
        {
            if (GizmoUtility.TryGetGizmoInfo(type,out var info) && info.hasIcon)
                GizmoUtility.SetIconEnabled(type,false);
        }

        static bool IconHidden(System.Type type)
        {
            return !GizmoUtility.TryGetGizmoInfo(type,out var info) || !info.hasIcon || !info.iconEnabled;
        }

        public static bool IconsHidden()
        {
            return GizmoUtility.TryGetGizmoInfo(typeof(AudioSource),out var source) &&
                (!source.hasIcon || !source.iconEnabled) && IconHidden(typeof(AudioListener));
        }
    }
}

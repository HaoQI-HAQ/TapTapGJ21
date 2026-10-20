using PAO.UI.V2;
using UnityEngine.SceneManagement;

namespace PAO
{
    /// <summary>Map1 首次通关的教学限制；通关后再次进入时开放全部泡泡。</summary>
    public static class BubbleAbilityRules
    {
        private const string TutorialScene = "Map1";
        private static int sceneHandle;
        private static RunProgress progressAtEntry;
        private static bool bouncyOnly;

        public static bool IsAvailable(BubbleType type)
        {
            if (type < BubbleType.Sticky || type > BubbleType.Bomb) return false;

            var scene = SceneManager.GetActiveScene();
            RunProgress progress = UIRunState.Progress;
            // 进入另一场景或载入另一份存档时重算；本次通关不会在原地提前解除教学限制。
            if (scene.handle != sceneHandle || !ReferenceEquals(progressAtEntry, progress))
            {
                sceneHandle = scene.handle;
                progressAtEntry = progress;
                bouncyOnly = scene.name == TutorialScene &&
                    (progress == null || progress.completed == null || !progress.completed.Contains(TutorialScene));
            }

            return !bouncyOnly || type == BubbleType.Bouncy;
        }

        public static BubbleType Resolve(BubbleType requested)
        {
            return IsAvailable(requested) ? requested : BubbleType.Bouncy;
        }
    }
}

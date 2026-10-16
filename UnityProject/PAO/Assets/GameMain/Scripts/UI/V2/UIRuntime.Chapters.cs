using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.UI.V2
{
    public sealed partial class UIRuntime
    {
        private Transform chapterGoal;

        private void PrepareChapter(Scene scene)
        {
            chapterGoal = null;
            int index = Config.IndexOf(scene.name);
            if (index < 0) return;
            ChapterDefinition chapter = Config.chapters[index];
            Transform start = FindAnchor(scene, chapter.spawnAnchor);
            Transform goal = FindAnchor(scene, chapter.goalAnchor);
            if (FindFirstObjectByType<PlayerController>() == null && start != null && Config.defaultPlayer != null)
            {
                // Whitebox cameras are replaced by the player's own camera only when supplying a missing player.
                foreach (Camera oldCamera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                {
                    if (oldCamera.gameObject.scene != scene) continue;
                    oldCamera.enabled = false;
                    AudioListener listener = oldCamera.GetComponent<AudioListener>();
                    if (listener != null) listener.enabled = false;
                }
                Vector3 position = SurfacePoint(start);
                CharacterController controller = Config.defaultPlayer.GetComponent<CharacterController>();
                if (controller != null)
                    position.y += (controller.height * 0.5f - controller.center.y) * Config.defaultPlayer.transform.localScale.y + 0.15f;
                Vector3 direction = goal != null ? SurfacePoint(goal) - position : Vector3.forward;
                direction.y = 0;
                Quaternion rotation = direction.sqrMagnitude > 0.01f ? Quaternion.LookRotation(direction) : Quaternion.identity;
                GameObject instance = Instantiate(Config.defaultPlayer, position, rotation);
                instance.name = "Chapter Player";
                SceneManager.MoveGameObjectToScene(instance, scene);
            }

            ChapterGoalTrigger authored = FindFirstObjectByType<ChapterGoalTrigger>();
            tiaozhuan legacy = FindFirstObjectByType<tiaozhuan>();
            if (authored != null) chapterGoal = authored.transform;
            else if (legacy != null) chapterGoal = legacy.transform;
            else if (goal != null && FindFirstObjectByType<PAO.BubbleTide.BubbleTideDirector>() == null)
            {
                var exit = new GameObject("Chapter goal · " + chapter.goalAnchor);
                SceneManager.MoveGameObjectToScene(exit, scene);
                exit.transform.position = SurfacePoint(goal);
                ChapterGoalTrigger trigger = exit.AddComponent<ChapterGoalTrigger>();
                trigger.requireInteract = true;
                trigger.nextScene = index + 1 < Config.chapters.Length ? Config.chapters[index + 1].scene : "";
                BoxCollider box = exit.GetComponent<BoxCollider>();
                box.center = new Vector3(0, 1.5f, 0); box.size = new Vector3(3, 3, 3);
                exit.AddComponent<UIChapterBeacon>();
                chapterGoal = exit.transform;
            }
        }

        private static Transform FindAnchor(Scene scene, string anchor)
        {
            if (string.IsNullOrEmpty(anchor)) return null;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                    if (candidate.name == anchor) return candidate;
            return null;
        }

        private static Vector3 SurfacePoint(Transform anchor)
        {
            Collider collider = anchor.GetComponent<Collider>();
            Renderer renderer = anchor.GetComponent<Renderer>();
            Bounds bounds = collider != null ? collider.bounds : renderer != null ? renderer.bounds : new Bounds(anchor.position, Vector3.zero);
            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }
    }
}

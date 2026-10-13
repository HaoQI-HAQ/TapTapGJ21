using UnityEngine;

namespace PAO.UI.V2
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ChapterGoalTrigger : MonoBehaviour
    {
        public string nextScene;
        private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
        private void Awake() { GetComponent<BoxCollider>().isTrigger = true; }
        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() != null && UIRuntime.Instance != null)
                UIRuntime.Instance.CompleteChapter(nextScene);
        }
    }
}

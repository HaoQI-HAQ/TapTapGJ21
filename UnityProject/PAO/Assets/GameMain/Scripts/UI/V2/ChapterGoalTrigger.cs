using UnityEngine;

namespace PAO.UI.V2
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ChapterGoalTrigger : MonoBehaviour
    {
        public string nextScene;
        public bool requireInteract;
        private bool nearby;
        private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
        private void Awake() { GetComponent<BoxCollider>().isTrigger = true; }
        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            nearby = true;
            if (!requireInteract) TryComplete();
            else if (UIRuntime.Instance != null)
                UIRuntime.Instance.Toast(UISettings.Text("抵达章节终点 · ", "Chapter goal · ") + UIInput.Prompt(UIInput.Interact), false);
        }
        private void OnTriggerExit(Collider other)
        { if (other.GetComponentInParent<PlayerController>() != null) nearby = false; }
        private void Update()
        { if (requireInteract && UIInput.Down(UIInput.Interact)) TryComplete(); }
        public bool TryComplete()
        {
            UIRuntime runtime = UIRuntime.Instance;
            if (!nearby || runtime == null || runtime.IsBlockingGameplay) return false;
            if (requireInteract) runtime.ConfirmChapterGoal(nextScene);
            else return runtime.CompleteChapter(nextScene);
            return true;
        }
    }
}

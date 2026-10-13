using UnityEngine;

namespace PAO.UI.V2
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class MemoryTrigger : MonoBehaviour
    {
        public MemoryEntry memory = new MemoryEntry();
        public DialogueAsset dialogue;
        public bool requireInteract = true;
        private bool nearby;
        private bool consumed;
        private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
        private void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            if (string.IsNullOrEmpty(memory.chapter)) memory.chapter = gameObject.scene.name;
            if (string.IsNullOrEmpty(memory.id) && dialogue == null)
                Debug.LogWarning("[UI] MemoryTrigger 需要唯一且稳定的碎片 id。", this);
        }
        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            nearby = true;
            if (!requireInteract) Collect();
            else if (!IsCollected && UIRuntime.Instance != null)
                UIRuntime.Instance.Toast(UISettings.Text("拾起意识碎片 · ", "Collect a memory · ") + UIInput.Prompt(UIInput.Interact), false);
        }
        private void OnTriggerExit(Collider other)
        { if (other.GetComponentInParent<PlayerController>() != null) nearby = false; }
        private void Update()
        { if (nearby && requireInteract && UIInput.Down(UIInput.Interact)) Collect(); }
        private bool IsCollected { get { return consumed || UIRunState.Progress.memories.Exists(m => m != null &&
            m.id == (dialogue != null ? dialogue.id + (dialogue.choices != null && dialogue.choices.Length > 0 ? ":choice" : "") : memory.id)); } }
        public void Collect()
        {
            UIRuntime runtime = UIRuntime.Instance;
            if (IsCollected || runtime == null || runtime.IsBlockingGameplay) return;
            consumed = dialogue == null;
            if (dialogue != null) runtime.ShowDialogue(dialogue);
            else runtime.CollectMemory(memory);
        }
    }
}

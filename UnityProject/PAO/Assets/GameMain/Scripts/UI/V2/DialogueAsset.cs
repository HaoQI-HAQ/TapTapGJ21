using System;
using UnityEngine;

namespace PAO.UI.V2
{
    [Serializable]
    public sealed class DialogueLine { public string speaker; [TextArea] public string text; }
    [Serializable]
    public sealed class DialogueChoice { public string text; public DialogueAsset next; }
    [CreateAssetMenu(menuName = "PAO/UI/Dialogue", fileName = "Dialogue")]
    public sealed class DialogueAsset : ScriptableObject
    {
        public string id;
        public string title;
        public DialogueLine[] lines;
        public DialogueChoice[] choices;
    }
}

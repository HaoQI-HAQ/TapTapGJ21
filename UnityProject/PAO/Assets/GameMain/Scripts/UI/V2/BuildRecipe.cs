using UnityEngine;

namespace PAO.UI.V2
{
    [CreateAssetMenu(menuName = "PAO/UI/Build Recipe", fileName = "BuildRecipe")]
    public sealed class BuildRecipe : ScriptableObject
    {
        public string id;
        public string title;
        [TextArea] public string description;
        public GameObject prefab;
        [Min(0)] public int cost = 1;
    }
}

using UnityEngine;

namespace PAO
{
    /// <summary>Editor-authored art-pass provenance and reversible source renderer states.</summary>
    [DisallowMultipleComponent]
    public sealed class Map2PresetArtState : MonoBehaviour
    {
        public string backupPath;
        public string generatedUtc;
        public Renderer[] sourceRenderers;
        public bool[] originalEnabled;
        public int sourceActors;
        public int placedModules;
    }
}

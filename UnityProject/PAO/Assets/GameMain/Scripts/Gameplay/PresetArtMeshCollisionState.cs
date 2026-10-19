using UnityEngine;

namespace PAO
{
    /// <summary>Records the collision changes of an editor-authored art replacement.</summary>
    [DisallowMultipleComponent]
    public sealed class PresetArtMeshCollisionState : MonoBehaviour
    {
        public int bindingVersion = 1;
        public string generatedUtc;
        public string backupPath;
        public Collider[] sourceColliders;
        public bool[] originalEnabled;
        public MeshCollider[] generatedMeshColliders;
        public int boundMeshFilters;
    }
}

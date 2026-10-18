using UnityEngine;

namespace PAO
{
    /// <summary>Provenance of a blockout proxy; does not implement the source game's logic.</summary>
    public sealed class ShrineWhiteboxReference : MonoBehaviour
    {
        public string sourceMap;
        public string sourceActor;
        public string sourceHash;
        public Vector3 sourcePosition;
        public Vector3 sourceEulerDegrees;
        public Vector3 modelBoundsMin;
        public Vector3 modelBoundsMax;
        [TextArea(2, 6)] public string geometryNote;
    }
}

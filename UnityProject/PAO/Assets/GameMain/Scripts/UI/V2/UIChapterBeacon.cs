using UnityEngine;

namespace PAO.UI.V2
{
    // A visible marker for configurable chapter goals in the existing whitebox maps.
    public sealed class UIChapterBeacon : MonoBehaviour
    {
        private Material material;
        private void Start()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null) return;
            material = new Material(shader);
            material.color = new Color32(94, 211, 196, 255);
            Make(PrimitiveType.Cylinder, "Goal column", new Vector3(0, 1.3f, 0), new Vector3(0.12f, 1.3f, 0.12f));
            Make(PrimitiveType.Sphere, "Goal light", new Vector3(0, 2.6f, 0), Vector3.one * 0.5f);
        }
        private void Make(PrimitiveType shape, string label, Vector3 position, Vector3 scale)
        {
            GameObject marker = GameObject.CreatePrimitive(shape);
            marker.name = label; marker.transform.SetParent(transform, false);
            marker.transform.localPosition = position; marker.transform.localScale = scale;
            Collider collider = marker.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            marker.GetComponent<Renderer>().sharedMaterial = material;
        }
        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}

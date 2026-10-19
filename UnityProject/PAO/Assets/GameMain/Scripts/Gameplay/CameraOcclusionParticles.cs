using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PAO
{
    /// <summary>Turns only camera-obstructing sandbox solids into soft particles; colliders stay intact.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(MeshRenderer))]
    public sealed class CameraOcclusionParticles : MonoBehaviour
    {
        private static readonly List<CameraOcclusionParticles> Active = new List<CameraOcclusionParticles>();
        private static RaycastHit[] hits = new RaycastHit[32];
        private static Collider[] overlaps = new Collider[16];
        private static Shader particleShader;
        private static readonly int FadeId = Shader.PropertyToID("_OcclusionFade");
        private MeshRenderer target;
        private Material[] originals, particles;
        private MaterialPropertyBlock originalProperties, properties;
        private ShadowCastingMode originalShadows;
        private float fade, lastObstructed = float.NegativeInfinity;
        public float Fade => fade;
        public bool IsObstructing => enabled && Time.unscaledTime - lastObstructed < .15f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ReleaseAll();
            particleShader = null;
        }

        public static void Attach(GameObject solid)
        {
            if (solid.GetComponent<MeshRenderer>() != null && solid.GetComponent<Collider>() != null &&
                solid.GetComponent<CameraOcclusionParticles>() == null)
                solid.AddComponent<CameraOcclusionParticles>();
        }

        /// <summary>Called before camera collision, using its intended position to prevent fade/pull-in oscillation.</summary>
        public static void Prepare(Vector3 focus, Vector3 cameraPosition, float radius, int mask, Transform player, Transform anchor)
        {
            Vector3 offset = cameraPosition - focus;
            float distance = offset.magnitude;
            if (distance > .001f)
            {
                int count;
                while ((count = Physics.SphereCastNonAlloc(focus, radius, offset / distance, hits,
                    distance, mask, QueryTriggerInteraction.Ignore)) == hits.Length)
                    Array.Resize(ref hits, hits.Length * 2);
                for (int i = 0; i < count; i++) Mark(hits[i].collider, player, anchor);
            }
            // Sphere casts do not reliably report a collider containing their start or end point.
            MarkOverlaps(focus, radius, mask, player, anchor);
            MarkOverlaps(cameraPosition, radius, mask, player, anchor);
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                CameraOcclusionParticles item = Active[i];
                if (item == null) { Active.RemoveAt(i); continue; }
                float goal = item.IsObstructing ? 1f : 0f;
                item.fade = Mathf.MoveTowards(item.fade, goal, Time.unscaledDeltaTime * (goal > 0 ? 9f : 3f));
                item.properties.SetFloat(FadeId, item.fade);
                item.target.SetPropertyBlock(item.properties);
                if (item.fade == 0f) { item.Restore(); Active.RemoveAt(i); }
            }
        }

        private static void MarkOverlaps(Vector3 point, float radius, int mask, Transform player, Transform anchor)
        {
            int count;
            while ((count = Physics.OverlapSphereNonAlloc(point, radius, overlaps, mask,
                QueryTriggerInteraction.Ignore)) == overlaps.Length)
                Array.Resize(ref overlaps, overlaps.Length * 2);
            for (int i = 0; i < count; i++) Mark(overlaps[i], player, anchor);
        }

        private static void Mark(Collider collider, Transform player, Transform anchor)
        {
            if (collider == null || collider.transform.IsChildOf(player) ||
                (anchor != null && collider.transform.IsChildOf(anchor))) return;
            CameraOcclusionParticles item = collider.GetComponent<CameraOcclusionParticles>();
            if (item == null || !item.isActiveAndEnabled || !item.Begin()) return;
            item.lastObstructed = Time.unscaledTime;
        }

        private bool Begin()
        {
            if (particles != null) return true;
            if (particleShader == null) particleShader = Resources.Load<Shader>("CameraOcclusionParticles");
            if (particleShader == null || !particleShader.isSupported) return false;
            target = GetComponent<MeshRenderer>();
            if (!target.enabled) return false;
            originals = target.sharedMaterials;
            particles = new Material[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                Material source = originals[i];
                Material material = new Material(particleShader) { name = "Camera particles (runtime)", hideFlags = HideFlags.DontSave };
                if (source != null)
                {
                    material.CopyMatchingPropertiesFromMaterial(source);
                    material.SetFloat("_Grid", source.shader.name == "PAO/SandboxGrid" ? 1 : 0);
                }
                particles[i] = material;
            }
            originalProperties = new MaterialPropertyBlock();
            properties = new MaterialPropertyBlock();
            target.GetPropertyBlock(originalProperties);
            target.GetPropertyBlock(properties);
            originalShadows = target.shadowCastingMode;
            target.sharedMaterials = particles;
            target.shadowCastingMode = ShadowCastingMode.Off;
            Active.Add(this);
            return true;
        }

        private void Restore()
        {
            if (particles == null) return;
            if (target != null)
            {
                target.sharedMaterials = originals;
                target.SetPropertyBlock(originalProperties);
                target.shadowCastingMode = originalShadows;
            }
            foreach (Material material in particles) if (material != null) Destroy(material);
            particles = null;
            fade = 0f;
            lastObstructed = float.NegativeInfinity;
        }

        public static void ReleaseAll()
        {
            for (int i = Active.Count - 1; i >= 0; i--) if (Active[i] != null) Active[i].Restore();
            Active.Clear();
        }

        private void OnDisable() { Restore(); Active.Remove(this); }
    }
}

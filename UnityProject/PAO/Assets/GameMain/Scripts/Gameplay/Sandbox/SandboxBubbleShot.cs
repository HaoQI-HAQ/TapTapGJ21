using UnityEngine;

namespace PAO.Sandbox
{
    public sealed class SandboxBubbleShot : MonoBehaviour
    {
        private BubbleSandbox world;
        private Vector3Int cell;
        private Vector3 start, end;
        private BubbleType ability;
        private int diameter, color, revision;
        private SandboxShape shape;
        private bool paint;
        private Color? tint;
        private float charge, elapsed, duration;
        public void Initialize(BubbleSandbox owner, Vector3Int target, Vector3 point, BubbleType type,
            int size, SandboxShape form, int palette, bool recolor, float power, Color? customColor = null)
        {
            world = owner; cell = target; end = point; ability = type; diameter = size; shape = form;
            color = palette; tint = customColor; paint = recolor; charge = power; start = transform.position;
            revision = owner.ShotGeneration;
            duration = Mathf.Clamp(Vector3.Distance(start, end) / 32, .12f, .65f);
        }
        private void Update()
        {
            if (world == null) { Destroy(gameObject); return; }
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.position = Vector3.Lerp(start, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .35f);
            if (t < 1) return;
            // World replacement/undo cancels travelling shots; normal consecutive strokes can coexist.
            if (revision == world.ShotGeneration)
            {
                if (ability == BubbleType.Bouncy) world.Build(cell, diameter, shape, color, paint, tint);
                else if (ability == BubbleType.Sticky) world.Lift(cell, 1 + Mathf.FloorToInt(Mathf.Clamp01(charge) * 2));
                else world.DestroyBrush(cell, diameter, shape);
            }
            Destroy(gameObject);
        }
    }
}

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
        private bool lower;
        private SandboxPiece[] selection;
        private Color? tint;
        private float charge, elapsed, duration;
        public void Initialize(BubbleSandbox owner, Vector3Int target, Vector3 point, BubbleType type,
            int size, SandboxShape form, int palette, bool recolor, float power, Color? customColor = null,
            bool descend = false, SandboxPiece[] selected = null)
        {
            world = owner; cell = target; end = point; ability = type; diameter = size; shape = form;
            color = palette; tint = customColor; paint = recolor; charge = power; start = transform.position;
            lower = descend;
            // Capture the exact selection at release; later selection changes do not redirect a travelling shot.
            selection = selected != null ? (SandboxPiece[])selected.Clone() : null;
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
                else if (ability == BubbleType.Sticky)
                {
                    int levels = 1 + Mathf.FloorToInt(Mathf.Clamp01(charge) * 2);
                    if (selection != null) world.MoveSelection(selection, lower ? -levels : levels);
                    else if (lower) world.Lower(cell, levels);
                    else world.Lift(cell, levels);
                }
                else if (selection != null) world.DestroySelection(selection);
                else world.DestroyBrush(cell, diameter, shape);
            }
            Destroy(gameObject);
        }
    }
}

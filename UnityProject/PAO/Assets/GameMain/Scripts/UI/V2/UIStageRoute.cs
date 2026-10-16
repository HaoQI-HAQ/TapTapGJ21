using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    // Draws the connections behind the live node positions. Keeping the path in
    // one Graphic makes its fine, square elbows remain attached while nodes float.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UIStageRouteLine : MaskableGraphic
    {
        private RectTransform[] nodes;
        private bool[] reachable;

        public void SetNodes(RectTransform[] value)
        {
            nodes = value;
            SetVerticesDirty();
        }

        public void SetReachable(bool[] value)
        {
            reachable = value;
            SetVerticesDirty();
        }

        public void Redraw() { SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (nodes == null) return;
            for (int i = 0; i + 1 < nodes.Length; i++)
            {
                if (nodes[i] == null || nodes[i + 1] == null) continue;
                RectTransform left = nodes[i], right = nodes[i + 1];
                Vector2 from = transform.InverseTransformPoint(left.TransformPoint(
                    new Vector3(left.rect.xMax, left.rect.center.y, 0)));
                Vector2 to = transform.InverseTransformPoint(right.TransformPoint(
                    new Vector3(right.rect.xMin, right.rect.center.y, 0)));
                float elbowX = Mathf.Lerp(from.x, to.x, .5f);
                Vector2 elbowA = new Vector2(elbowX, from.y);
                Vector2 elbowB = new Vector2(elbowX, to.y);
                Color ink = reachable != null && i + 1 < reachable.Length && reachable[i + 1] ?
                    Color.black : new Color(.62f, .62f, .62f);
                Stroke(mesh, from, elbowA, 1.8f, ink);
                Stroke(mesh, elbowA, elbowB, 1.8f, ink);
                Stroke(mesh, elbowB, to, 1.8f, ink);
                Square(mesh, elbowA, 4.5f, ink);
                Square(mesh, elbowB, 4.5f, ink);
            }
        }

        private static void Stroke(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color ink)
        {
            Vector2 direction = b - a;
            if (direction.sqrMagnitude < .01f) return;
            Vector2 normal = new Vector2(-direction.y, direction.x).normalized * width * .5f;
            int first = mesh.currentVertCount;
            mesh.AddVert(a - normal, ink, Vector2.zero);
            mesh.AddVert(a + normal, ink, Vector2.zero);
            mesh.AddVert(b + normal, ink, Vector2.zero);
            mesh.AddVert(b - normal, ink, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }

        private static void Square(VertexHelper mesh, Vector2 center, float width, Color ink)
        {
            float half = width * .5f;
            int first = mesh.currentVertCount;
            mesh.AddVert(center + new Vector2(-half, -half), ink, Vector2.zero);
            mesh.AddVert(center + new Vector2(-half, half), ink, Vector2.zero);
            mesh.AddVert(center + new Vector2(half, half), ink, Vector2.zero);
            mesh.AddVert(center + new Vector2(half, -half), ink, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }
    }

    public sealed class UIStageRouteNodeMotion : MonoBehaviour, ISelectHandler
    {
        private RectTransform rect;
        private UIStageRouteLine route;
        private Action focused;
        private Vector2 origin;
        private float phase;
        private float amplitude;

        public void Configure(UIStageRouteLine line, float offset, float distance, Action onFocus)
        {
            rect = (RectTransform)transform;
            route = line; phase = offset; amplitude = distance; focused = onFocus;
            origin = rect.anchoredPosition;
            ApplyMotion(Time.unscaledTime, UISettings.Current.reducedMotion);
        }

        public void ApplyMotion(float seconds, bool reducedMotion)
        {
            if (rect == null) return;
            float displacement = reducedMotion ? 0 : Mathf.Sin(seconds * 1.25f + phase) * amplitude;
            Vector2 position = origin + new Vector2(0, displacement);
            if ((rect.anchoredPosition - position).sqrMagnitude < .0001f) return;
            rect.anchoredPosition = position;
            if (route != null) route.Redraw();
        }

        private void Update() { ApplyMotion(Time.unscaledTime, UISettings.Current.reducedMotion); }
        public void OnSelect(BaseEventData data) { if (focused != null) focused(); }

        private void OnDisable()
        {
            if (rect == null) return;
            rect.anchoredPosition = origin;
            if (route != null) route.Redraw();
        }
    }
}

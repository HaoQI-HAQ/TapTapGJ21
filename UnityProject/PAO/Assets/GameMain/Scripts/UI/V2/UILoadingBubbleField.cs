using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    /// <summary>Monochrome bubble membranes in a shallow, buoyant current. Uses unscaled time.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UILoadingBubbleField : MaskableGraphic
    {
        private const int Count = 24, Segments = 64;
        private const float StepTime = 1f / 120f;
        private sealed class Bubble
        {
            public Vector2 position, velocity, shape, shapeVelocity;
            public float radius, phase, brightness;
        }

        private readonly Bubble[] bubbles = new Bubble[Count];
        private System.Random random;
        private float width, elapsed, accumulator;

        public void ResetField()
        {
            random = new System.Random(731);
            width = 0; elapsed = accumulator = 0;
            EnsureField(); SetVerticesDirty();
        }

        private float Range(float a, float b) { return Mathf.Lerp(a, b, (float)random.NextDouble()); }

        private void EnsureField()
        {
            Rect rect = rectTransform.rect;
            if (rect.height <= 0 || rect.width <= 0) return;
            float nextWidth = 900 * rect.width / rect.height;
            if (width > 0)
            {
                if (!Mathf.Approximately(width, nextWidth))
                    foreach (Bubble b in bubbles) b.position.x *= nextWidth / width;
                width = nextWidth; return;
            }
            width = nextWidth;
            if (random == null) random = new System.Random(731);
            for (int i = 0; i < Count; i++)
            {
                var b = new Bubble { radius = i % 4 == 0 ? Range(8, 15) : Range(22, 54),
                    phase = Range(0, Mathf.PI * 2), brightness = Range(.48f, .95f) };
                // Keep the middle open; loose streams travel on either side of the screen.
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    float side = i % 2 == 0 ? -1 : 1;
                    b.position = new Vector2(side * Range(.10f, .40f) * width, Range(-480, 490));
                    bool clear = true;
                    for (int j = 0; j < i; j++)
                        if (Vector2.Distance(b.position, bubbles[j].position) < b.radius + bubbles[j].radius + 8) { clear = false; break; }
                    if (clear) break;
                }
                b.velocity = new Vector2(Range(-10, 10), RiseSpeed(b.radius));
                bubbles[i] = b;
            }
        }

        private static float RiseSpeed(float radius) { return 32 + Mathf.Sqrt(radius) * 8; }

        private void Update()
        {
            EnsureField();
            if (width <= 0 || UISettings.Current.reducedMotion) return;
            // Fixed substeps keep soft contacts stable; discard long loading stalls rather than teleport.
            accumulator += Mathf.Min(Time.unscaledDeltaTime, .066f);
            while (accumulator >= StepTime) { Step(StepTime); accumulator -= StepTime; }
            SetVerticesDirty();
        }

        private void Step(float dt)
        {
            elapsed += dt;
            foreach (Bubble b in bubbles)
            {
                float flow = 19 * Mathf.Sin(b.position.y * .004f + elapsed * .55f) +
                    9 * Mathf.Sin(elapsed * 1.15f + b.phase);
                b.velocity.x += (flow - b.velocity.x) * 1.2f * dt;
                // Buoyancy is balanced by fluid drag, giving different terminal speeds by size.
                b.velocity.y += (RiseSpeed(b.radius) - b.velocity.y) * 1.7f * dt;
                b.position += b.velocity * dt;
                float wobble = .018f * (b.radius / 54) * Mathf.Sin(elapsed * 3.2f + b.phase);
                Vector2 rest = new Vector2(.025f + wobble, .012f * Mathf.Sin(elapsed * 2 + b.phase));
                b.shapeVelocity += ((rest - b.shape) * 65 - b.shapeVelocity * 9) * dt;
                b.shape += b.shapeVelocity * dt;
                b.shape = Vector2.ClampMagnitude(b.shape, .16f);
                float edge = width * .5f - b.radius - 8;
                if (Mathf.Abs(b.position.x) > edge)
                {
                    b.position.x = Mathf.Sign(b.position.x) * edge;
                    b.velocity.x *= -.35f;
                }
                if (b.position.y > 450 + b.radius + 65)
                {
                    b.position.y = -450 - b.radius - Range(30, 110);
                    b.position.x = Range(-.4f, .4f) * width;
                    b.shape = b.shapeVelocity = Vector2.zero;
                }
            }
            for (int i = 0; i < Count; i++)
                for (int j = i + 1; j < Count; j++) Contact(bubbles[i], bubbles[j], dt);
        }

        private static void Contact(Bubble a, Bubble b, float dt)
        {
            Vector2 delta = b.position - a.position;
            float distance = delta.magnitude, restDistance = a.radius + b.radius;
            if (distance >= restDistance || distance < .001f) return;
            Vector2 normal = delta / distance;
            float overlap = restDistance - distance;
            float inverseA = 1 / (a.radius * a.radius), inverseB = 1 / (b.radius * b.radius);
            float shareA = inverseA / (inverseA + inverseB), shareB = 1 - shareA;
            // A small compliant overlap resembles a membrane; impulses dissipate rather than bounce hard.
            float correction = Mathf.Max(0, overlap - restDistance * .025f) * .6f;
            a.position -= normal * correction * shareA; b.position += normal * correction * shareB;
            float approach = Vector2.Dot(b.velocity - a.velocity, normal);
            float impulse = Mathf.Max(0, -approach) * 1.12f + overlap * 20 * dt;
            a.velocity -= normal * impulse * shareA; b.velocity += normal * impulse * shareB;
            Vector2 squash = new Vector2(normal.x * normal.x - normal.y * normal.y, 2 * normal.x * normal.y);
            float stress = Mathf.Min(.8f, overlap / restDistance * 4 + Mathf.Max(0, -approach) * .002f);
            a.shapeVelocity -= squash * stress; b.shapeVelocity -= squash * stress;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); EnsureField();
            if (width <= 0) return;
            float scale = rectTransform.rect.height / 900;
            foreach (Bubble b in bubbles)
            {
                float stroke = b.radius < 18 ? 1 : 1.65f;
                // A faint outer rim, antialiased membrane and a short white reflection; no coloured glow.
                Ring(mesh, b, scale, stroke + 2.8f, stroke, .035f, .06f);
                Ring(mesh, b, scale, stroke, stroke * .25f, 0, b.brightness);
                Ring(mesh, b, scale, stroke * .25f, -stroke * .7f, b.brightness, b.brightness * .6f);
                Ring(mesh, b, scale, -stroke * .7f, -stroke * 1.5f, b.brightness * .6f, 0);
            }
        }

        private void Ring(VertexHelper mesh, Bubble b, float scale, float outer, float inner, float alphaOuter, float alphaInner)
        {
            for (int k = 0; k < Segments; k++)
            {
                float a = k * Mathf.PI * 2 / Segments, next = (k + 1) * Mathf.PI * 2 / Segments;
                int index = mesh.currentVertCount;
                AddVertex(mesh, b, a, outer, scale, alphaOuter);
                AddVertex(mesh, b, next, outer, scale, alphaOuter);
                AddVertex(mesh, b, next, inner, scale, alphaInner);
                AddVertex(mesh, b, a, inner, scale, alphaInner);
                mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
            }
        }

        private void AddVertex(VertexHelper mesh, Bubble b, float angle, float offset, float scale, float alpha)
        {
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float shape = 1 + b.shape.x * Mathf.Cos(2 * angle) + b.shape.y * Mathf.Sin(2 * angle);
            // Normalize the polar deformation to retain membrane area while it squashes and recovers.
            float radius = b.radius * shape / Mathf.Sqrt(1 + b.shape.sqrMagnitude * .5f) + offset;
            float reflection = Mathf.Pow(Mathf.Max(0, Vector2.Dot(direction, new Vector2(-.6f, .8f))), 10);
            float rim = .28f + reflection * .72f + .15f * Mathf.Pow(Mathf.Max(0, -direction.y), 4);
            Vector2 position = rectTransform.rect.center + (b.position + direction * radius) * scale;
            mesh.AddVert(position, new Color(1, 1, 1, alpha * rim * color.a), Vector2.zero);
        }
    }
}

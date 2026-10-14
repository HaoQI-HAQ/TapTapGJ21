using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    // A deterministic vector illustration: facets remain crisp without bitmap textures.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UILowPolyOcean : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect r = rectTransform.rect;
            bool narrow = r.width < 1300;
            float opacity = UISettings.Current.highContrast ? .12f : narrow ? .16f : 1;
            Vector2 origin = new Vector2(r.xMin + r.width * .78f, r.yMin + r.height * .60f);
            float unit = Mathf.Min(r.width * .23f, r.height * .36f);
            Color deep = new Color32(28, 86, 101, 255), sea = new Color32(60, 137, 153, 255);
            Vector2[] rim = { new Vector2(-1.25f, .9f), new Vector2(-.15f, 1.25f), new Vector2(1.1f, 1.04f),
                new Vector2(1.5f, -.4f), new Vector2(.65f, -1.52f), new Vector2(-.75f, -1.55f), new Vector2(-1.3f, -.45f) };
            for (int i = 0; i < rim.Length; i++)
            {
                Color face = Color.Lerp(deep, sea, (i % 3) * .34f); face.a = opacity * .92f * color.a;
                UILowPolyMesh.Triangle(mesh, origin + new Vector2(.12f, -.24f) * unit,
                    origin + rim[i] * unit, origin + rim[(i + 1) % rim.Length] * unit, face);
            }
            // Angular caustics and suspended bubbles frame the character.
            Face(mesh, origin, unit, new Vector2(-1.25f, .9f), new Vector2(-.15f, 1.25f), new Vector2(-.8f, -.4f), new Color(.48f, .85f, .82f, .18f * opacity * color.a));
            Face(mesh, origin, unit, new Vector2(.45f, 1.14f), new Vector2(.85f, 1.08f), new Vector2(.12f, -1.2f), new Color(.63f, .90f, .84f, .12f * opacity * color.a));
            FacetBubble(mesh, origin + new Vector2(-.87f, -.54f) * unit, unit * .16f, sea, opacity * color.a);
            FacetBubble(mesh, origin + new Vector2(.78f, .56f) * unit, unit * .10f, UIDesignPalette.Gold, opacity * color.a);
            FacetBubble(mesh, origin + new Vector2(.90f, -.75f) * unit, unit * .22f, sea, opacity * color.a);
            // Faceted bell, a scalloped rim and ribbon-like tentacles form the jellyfish.
            Vector2[] bell = { new Vector2(-.65f, -.08f), new Vector2(-.57f, .40f), new Vector2(-.27f, .70f),
                new Vector2(.18f, .76f), new Vector2(.56f, .48f), new Vector2(.70f, -.08f) };
            Color[] tones = { new Color32(121, 202, 184, 255), new Color32(174, 228, 199, 255),
                new Color32(219, 245, 211, 255), new Color32(143, 218, 194, 255), new Color32(90, 182, 174, 255) };
            Vector2 center = new Vector2(.06f, .24f);
            for (int i = 0; i < bell.Length - 1; i++) { Color c = tones[i]; c.a = opacity * color.a; Face(mesh, origin, unit, center, bell[i], bell[i + 1], c); }
            for (int i = 0; i < 5; i++)
            {
                float x = -.65f + i * .27f;
                Color c = tones[i]; c.a = opacity * color.a;
                Face(mesh, origin, unit, center, new Vector2(x, -.08f), new Vector2(x + .135f, -.21f), c);
                Face(mesh, origin, unit, center, new Vector2(x + .135f, -.21f), new Vector2(x + .27f, -.08f), c);
            }
            for (int i = 0; i < 5; i++)
            {
                float x = -.45f + i * .22f;
                Vector2 last = new Vector2(x, -.14f);
                for (int j = 1; j <= 4; j++)
                {
                    Vector2 next = new Vector2(x + Mathf.Sin(i * 1.7f + j) * .15f, -.14f - j * .22f);
                    Color c = Color.Lerp(tones[i], sea, j * .12f); c.a = opacity * color.a;
                    Ribbon(mesh, origin + last * unit, origin + next * unit, unit * (.05f - j * .006f), c); last = next;
                }
            }
            FacetBubble(mesh, origin + new Vector2(-.20f, .25f) * unit, unit * .035f, deep, opacity * color.a);
            FacetBubble(mesh, origin + new Vector2(.29f, .25f) * unit, unit * .035f, deep, opacity * color.a);
            // Foreground seabed is intentionally low detail.
            Face(mesh, origin, unit, new Vector2(-1.3f, -1.34f), new Vector2(-.6f, -1.13f), new Vector2(.65f, -1.52f), new Color(.10f, .26f, .31f, opacity * color.a));
            Face(mesh, origin, unit, new Vector2(-.6f, -1.13f), new Vector2(.28f, -1.26f), new Vector2(.65f, -1.52f), new Color(.17f, .38f, .42f, opacity * color.a));
        }
        private static void Face(VertexHelper mesh, Vector2 origin, float unit, Vector2 a, Vector2 b, Vector2 c, Color color)
            => UILowPolyMesh.Triangle(mesh, origin + a * unit, origin + b * unit, origin + c * unit, color);
        private static void Ribbon(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * width;
            UILowPolyMesh.Triangle(mesh, a - normal, a + normal, b + normal, color);
            UILowPolyMesh.Triangle(mesh, a - normal, b + normal, b - normal, color);
        }
        private static void FacetBubble(VertexHelper mesh, Vector2 center, float radius, Color tint, float opacity)
        {
            for (int i = 0; i < 8; i++)
            {
                Color c = Color.Lerp(tint, new Color(.84f, .96f, .88f), (i % 3) * .28f); c.a = opacity;
                UILowPolyMesh.Triangle(mesh, center + Vector2.one * radius * .12f,
                    center + UILowPolyMesh.Direction(i * 45 + 22.5f) * radius, center + UILowPolyMesh.Direction((i + 1) * 45 + 22.5f) * radius, c);
            }
        }
        private void OnSettingsChanged() => SetVerticesDirty();
        protected override void OnEnable() { base.OnEnable(); UISettings.Changed += OnSettingsChanged; }
        protected override void OnDisable() { UISettings.Changed -= OnSettingsChanged; base.OnDisable(); }
    }
}

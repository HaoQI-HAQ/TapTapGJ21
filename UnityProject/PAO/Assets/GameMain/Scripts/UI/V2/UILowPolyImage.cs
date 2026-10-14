using UnityEngine;
using UnityEngine.UI;

namespace PAO.UI.V2
{
    // Image keeps Selectable tint, masking and existing UI bindings; geometry supplies the skin.
    public sealed class UILowPolyImage : Image
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect r = GetPixelAdjustedRect();
            float cut = Mathf.Min(12, Mathf.Min(r.width, r.height) * .22f);
            Vector2[] points = {
                new Vector2(r.xMin + cut, r.yMin), new Vector2(r.xMax - cut, r.yMin),
                new Vector2(r.xMax, r.yMin + cut), new Vector2(r.xMax, r.yMax - cut),
                new Vector2(r.xMax - cut, r.yMax), new Vector2(r.xMin + cut, r.yMax),
                new Vector2(r.xMin, r.yMax - cut), new Vector2(r.xMin, r.yMin + cut)
            };
            Vector2 center = new Vector2(r.xMin + r.width * .65f, r.yMin + r.height * .58f);
            for (int i = 0; i < points.Length; i++)
            {
                Color face = color;
                float shade = UISettings.Current.highContrast ? 1 : i < 3 ? .96f : i < 6 ? 1 : .985f;
                face.r *= shade; face.g *= shade; face.b *= shade;
                UILowPolyMesh.Triangle(mesh, center, points[i], points[(i + 1) % points.Length], face);
            }
        }
    }

    public sealed class UILowPolyRingImage : Image
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect r = rectTransform.rect;
            float radius = Mathf.Min(r.width, r.height) * .5f;
            for (int i = 0; i < 12; i++)
            {
                Vector2 a = UILowPolyMesh.Direction(i * 30 + 15), b = UILowPolyMesh.Direction((i + 1) * 30 + 15);
                Vector2 p = r.center + a * radius, q = r.center + b * radius;
                Vector2 u = r.center + a * (radius - 3), v = r.center + b * (radius - 3);
                UILowPolyMesh.Triangle(mesh, u, p, q, color); UILowPolyMesh.Triangle(mesh, u, q, v, color);
            }
        }
    }

    internal static class UILowPolyMesh
    {
        public static Vector2 Direction(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
        public static void Triangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int v = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero); mesh.AddVert(c, color, Vector2.zero);
            mesh.AddTriangle(v, v + 1, v + 2);
        }
    }
}

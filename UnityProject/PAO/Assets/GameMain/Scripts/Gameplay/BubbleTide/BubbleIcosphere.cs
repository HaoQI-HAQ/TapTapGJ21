using System.Collections.Generic;
using UnityEngine;

namespace PAO.BubbleTide
{
    /// <summary>
    /// 运行时生成 icosphere（正二十面体细分球）。
    ///
    /// 【为什么不用 GameObject.CreatePrimitive(PrimitiveType.Sphere)】
    /// Unity 内置球在极点处顶点密集、赤道处稀疏，而且有接缝（UV 拆开了顶点），
    /// 对顶点位移（我们的扰动是在顶点上做的）来说极点会挤成一团。
    /// icosphere 的顶点分布是均匀的，细分级别也精确可控 —— 这一点在大泡泡上很重要：
    /// 一颗 10 米的泡泡，顶点不均匀会直接看成一棱一棱的。
    ///
    /// 【为什么顶点里不放 UV】
    /// 这个材质一次贴图都不采（见 BubbleTideToonCore.hlsl 的取舍 3），
    /// 所以 UV 是纯浪费：省下的带宽和顶点缓存全喂给实例化。
    /// 法线也不需要存 —— 单位球的法线就等于归一化的顶点位置，着色器里现算。
    ///
    /// 缓存起来按级别复用，别每次生成都重新算。
    /// </summary>
    public static class BubbleIcosphere
    {
        private static readonly Dictionary<int, Mesh> s_Cache = new Dictionary<int, Mesh>(4);

        /// <summary>每个细分级别对应的三角形数。见 GetTriangleCount 的说明。</summary>
        private static readonly Dictionary<int, int> s_TriangleCounts = new Dictionary<int, int>(4);

        /// <summary>
        /// 拿一个细分球。subdivisions = 1 得到 80 面（推荐起点），
        /// 0 就是原始二十面体（20 面，最近距离的 LOD）。
        /// </summary>
        public static Mesh Get(int subdivisions)
        {
            subdivisions = Mathf.Clamp(subdivisions, 0, 4);

            Mesh cached;
            if (s_Cache.TryGetValue(subdivisions, out cached) && cached != null)
            {
                return cached;
            }

            Mesh mesh = Build(subdivisions);
            s_Cache[subdivisions] = mesh;
            return mesh;
        }

        /// <summary>
        /// 三角形数。
        ///
        /// 【为什么要有这个方法，而不是让调用方读 mesh.triangles】
        /// Build 的最后一步是 UploadMeshData(true)，它把网格标成「不可读」以省掉
        /// 一份 CPU 端副本。之后任何对 mesh.triangles 的访问都会抛
        /// 「Not allowed to access triangles/indices on mesh ... isReadable is false」。
        ///
        /// 所以面数在生成时就被记下来了 —— HUD、日志、调试都从这里读。
        /// **别再往 mesh.triangles 上加调用。**
        /// </summary>
        public static int GetTriangleCount(int subdivisions)
        {
            subdivisions = Mathf.Clamp(subdivisions, 0, 4);

            int count;
            if (s_TriangleCounts.TryGetValue(subdivisions, out count))
            {
                return count;
            }

            // 没见过就先造一个（Build 会顺手把面数记下来）
            Get(subdivisions);

            return s_TriangleCounts.TryGetValue(subdivisions, out count) ? count : 0;
        }

        private static Mesh Build(int subdivisions)
        {
            // ---- 正二十面体的 12 个顶点 ----
            // 用黄金比例构造，这是唯一能保证 20 个面全等的摆法
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;

            List<Vector3> vertices = new List<Vector3>(12)
            {
                new Vector3(-1f,  t, 0f), new Vector3( 1f,  t, 0f),
                new Vector3(-1f, -t, 0f), new Vector3( 1f, -t, 0f),
                new Vector3( 0f, -1f,  t), new Vector3( 0f,  1f,  t),
                new Vector3( 0f, -1f, -t), new Vector3( 0f,  1f, -t),
                new Vector3( t, 0f, -1f), new Vector3( t, 0f,  1f),
                new Vector3(-t, 0f, -1f), new Vector3(-t, 0f,  1f),
            };

            for (int i = 0; i < vertices.Count; i++)
            {
                // 单位球：半径固定 1，实际大小交给实例矩阵的缩放
                vertices[i] = vertices[i].normalized;
            }

            List<int> triangles = new List<int>(60)
            {
                0, 11, 5,   0, 5, 1,    0, 1, 7,    0, 7, 10,   0, 10, 11,
                1, 5, 9,    5, 11, 4,   11, 10, 2,  10, 7, 6,   7, 1, 8,
                3, 9, 4,    3, 4, 2,    3, 2, 6,    3, 6, 8,    3, 8, 9,
                4, 9, 5,    2, 4, 11,   6, 2, 10,   8, 6, 7,    9, 8, 1,
            };

            // ---- 逐级细分 ----
            // 每条边在中点切开一分为二，中点推到球面上。
            // 用「边的顶点对」当 key 做缓存，避免共享边上的中点被算两次
            // （算两次不仅浪费，还会因为浮点误差产生裂缝）。
            for (int level = 0; level < subdivisions; level++)
            {
                Dictionary<long, int> midpoints = new Dictionary<long, int>(triangles.Count);
                List<int> next = new List<int>(triangles.Count * 4);

                int triangleCount = triangles.Count / 3;
                for (int i = 0; i < triangleCount; i++)
                {
                    int a = triangles[i * 3 + 0];
                    int b = triangles[i * 3 + 1];
                    int c = triangles[i * 3 + 2];

                    int ab = Midpoint(vertices, midpoints, a, b);
                    int bc = Midpoint(vertices, midpoints, b, c);
                    int ca = Midpoint(vertices, midpoints, c, a);

                    next.Add(a); next.Add(ab); next.Add(ca);
                    next.Add(b); next.Add(bc); next.Add(ab);
                    next.Add(c); next.Add(ca); next.Add(bc);
                    next.Add(ab); next.Add(bc); next.Add(ca);
                }

                triangles = next;
            }

            Mesh mesh = new Mesh();
            mesh.name = "BubbleIcosphere_L" + subdivisions;

            // 顶点数最多 4 级 2562 个，远超 65535 才会需要 32 位索引 ——
            // 提前标上，免得以后有人调到 5 级才发现顶点被截断
            mesh.indexFormat = vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);

            // 法线不存：单位球上法线 == 归一化位置，着色器里现算更省带宽。
            // 但 Unity 的渲染需要 mesh 有合法的包围盒做剔除，所以这里要 RecalculateBounds。
            mesh.RecalculateBounds();

            // 面数先记下来，再标成不可读 —— 反过来的话就取不到了
            s_TriangleCounts[subdivisions] = triangles.Count / 3;

            // 标成不可读，省一份 CPU 端副本。
            // 代价是不能再用 mesh.triangles / mesh.vertices，要面数请用 GetTriangleCount()。
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static int Midpoint(List<Vector3> vertices, Dictionary<long, int> cache, int a, int b)
        {
            // 把两个顶点索引压成一个 long 当 key。小的在前，保证 (a,b) 和 (b,a) 命中同一个
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            int index;
            if (cache.TryGetValue(key, out index))
            {
                return index;
            }

            Vector3 mid = (vertices[a] + vertices[b]) * 0.5f;
            mid = mid.normalized;      // 推到球面上 —— 这一步决定了它「圆不圆」

            index = vertices.Count;
            vertices.Add(mid);
            cache[key] = index;
            return index;
        }
    }
}

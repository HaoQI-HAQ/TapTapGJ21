using UnityEngine;

namespace PAO.Underwater
{
    /// <summary>
    /// 生成水面网格。
    ///
    /// 【为什么是运行时类，不是编辑器专属】
    ///   放在这里两边都能用：
    ///     · 编辑器工具用它建一个 .asset，给美术在 Scene 视图里拖
    ///     · 万一 .asset 丢了或者场景是代码生成的，运行时也能自己补一张
    ///   如果只写在 Editor 里，第二条路就断了。
    ///
    /// 【网格本身做了什么】
    ///   很少。波光、焦散反光、掠射亮边**全在片元着色器里算**，
    ///   网格只负责「有一块平的、足够大的面」。
    ///   所以段数给够就行，给多了只是白白多几千个顶点。
    ///
    /// 【坐标约定】
    ///   网格建在 XZ 平面上，y = 0，范围是 [-1, 1] × [-1, 1] —— 
    ///   也就是说**半边长 = 1**。
    ///   于是场景里 scale = (waterSize, 1, waterSize) 就把半边长设成了 waterSize，
    ///   改大小只要拖 Scale，不用重新生成网格。
    /// </summary>
    public static class WaterSurfaceMeshBuilder
    {
        /// <summary>建一张 (segments+1)² 个顶点的平面。segments 会被夹到 [1, 512]。</summary>
        public static Mesh Build(int segments)
        {
            segments = Mathf.Clamp(segments, 1, 512);

            int side = segments + 1;
            int vertexCount = side * side;

            var positions = new Vector3[vertexCount];
            var normals   = new Vector3[vertexCount];
            var uvs       = new Vector2[vertexCount];

            for (int z = 0; z < side; z++)
            {
                float tz = (float)z / segments;             // 0 ~ 1

                for (int x = 0; x < side; x++)
                {
                    float tx = (float)x / segments;

                    int i = z * side + x;

                    // 半边长 = 1
                    positions[i] = new Vector3(tx * 2f - 1f, 0f, tz * 2f - 1f);
                    normals[i]   = Vector3.up;
                    uvs[i]       = new Vector2(tx, tz);
                }
            }

            var triangles = new int[segments * segments * 6];

            int t = 0;
            for (int z = 0; z < segments; z++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int bl = z * side + x;
                    int br = bl + 1;
                    int tl = bl + side;
                    int tr = tl + 1;

                    // 【绕序】Unity 用顺时针为正面。
                    // 从上方（+Y 往下看）看，这两个三角形的顶点是顺时针的，
                    // 所以法线朝上，Unity 会正确识别正面。
                    //
                    // 水面 shader 里写的是 Cull Front —— 因为玩家永远在水下，
                    // 永远从**下方**看它。绕序对了，Cull Front 才能正确工作。
                    triangles[t++] = bl;
                    triangles[t++] = tl;
                    triangles[t++] = tr;

                    triangles[t++] = bl;
                    triangles[t++] = tr;
                    triangles[t++] = br;
                }
            }

            var mesh = new Mesh();
            mesh.name = "WaterSurface";

            // 顶点数超过 65535 时需要 32 位索引，Unity 会自动处理，
            // 但显式声明一下更清楚（默认段数远达不到，留着防以后调大）。
            mesh.indexFormat = vertexCount > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.vertices  = positions;
            mesh.normals   = normals;
            mesh.uv        = uvs;
            mesh.triangles = triangles;

            mesh.RecalculateBounds();

            return mesh;
        }
    }
}

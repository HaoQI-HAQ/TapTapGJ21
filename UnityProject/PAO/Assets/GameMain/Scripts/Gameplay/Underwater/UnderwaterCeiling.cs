using UnityEngine;
using UnityEngine.Rendering;

namespace PAO.Underwater
{
    /// <summary>
    /// 水下"天"：玩家永远在水面之下时，抬头看到的那层水。
    ///
    /// 它生成的不是一张平面，而是一个朝内翻的盒子壳：
    ///   顶面 = 水面，四边各往下垂一圈裙边。
    /// 这样从关卡里任何位置、任何角度看过去都只会看到壳的内表面，
    /// 不存在"看到网格边界外面"的情况，也就不需要靠雾或者后处理去遮。
    ///
    /// 为什么整块水不是一分为二的透明层：
    ///   见 UnderwaterCeiling.shader 顶部注释（Opaque 队列、双面、不采样 Scene Color）。
    ///
    /// 网格是运行时生成的（HideFlags.DontSave），不落在场景文件里；
    /// 场景里只保存 HalfSize / SkirtDepth 两个数字。
    /// 摆放位置由 Editor 工具按关卡真实包围盒算好（Tools/PAO/水下/…），
    /// 也可以直接手动拖到想要的高度。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class UnderwaterCeiling : MonoBehaviour
    {
        // 顶点顺序是刻意排的：每个四边形都按"叉积法线朝向玩家"的顺序写，
        // 于是朝内那一面是正面。现在 shader 是 Cull Off，绕序其实不影响显示，
        // 但万一下次有人改回 Cull Back，这里也不至于突然整片消失。
        private const int VertexCount = 20;   // 顶面 4 + 四片裙边 4×4
        private const int TriangleCount = 10; // 顶面 2 + 四片裙边 2×4

        private static readonly int ShellParamChannel = 1;

        [Header("尺寸（米）")]
        [Tooltip("水面从中心向外的半边长。做远大于关卡即可 —— 顶面只有两个三角形，再大也不贵。")]
        [SerializeField, Min(1f)] private float m_HalfSize = 260f;

        [Tooltip("水面边缘往下垂的裙边深度。从任何角度都看不到网格的边，靠的就是它。")]
        [SerializeField, Min(0f)] private float m_SkirtDepth = 80f;

        private Mesh m_Mesh;
        private float m_BuiltHalfSize = float.NaN;
        private float m_BuiltSkirtDepth = float.NaN;

        /// <summary>水面半边长（米）。Editor 工具摆放时会读它。</summary>
        public float HalfSize => m_HalfSize;

        /// <summary>裙边深度（米）。</summary>
        public float SkirtDepth => m_SkirtDepth;

        /// <summary>Editor 工具用它一次性设定尺寸并重建。</summary>
        public void Configure(float halfSize, float skirtDepth)
        {
            m_HalfSize = Mathf.Max(1f, halfSize);
            m_SkirtDepth = Mathf.Max(0f, skirtDepth);
            Rebuild();
        }

        private void OnEnable()
        {
            RebuildIfNeeded();
        }

        private void OnValidate()
        {
            // 在 Inspector 里改数字要立刻看到结果
            m_HalfSize = Mathf.Max(1f, m_HalfSize);
            m_SkirtDepth = Mathf.Max(0f, m_SkirtDepth);
            RebuildIfNeeded();
        }

        private void OnDestroy()
        {
            ReleaseMesh();
        }

        private void RebuildIfNeeded()
        {
            if (Mathf.Approximately(m_BuiltHalfSize, m_HalfSize)
                && Mathf.Approximately(m_BuiltSkirtDepth, m_SkirtDepth)
                && m_Mesh != null)
            {
                return;
            }

            Rebuild();
        }

        private void Rebuild()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null)
                return;

            if (m_Mesh == null)
            {
                m_Mesh = new Mesh
                {
                    name = "UnderwaterCeilingShell",
                    // 不进场景、不进 AssetBundle：每次加载/进播放模式由 OnEnable 重建
                    hideFlags = HideFlags.DontSave,
                };
            }
            else
            {
                m_Mesh.Clear();
            }

            var vertices = new Vector3[VertexCount];
            var normals = new Vector3[VertexCount];
            var shellParams = new Vector2[VertexCount];
            var triangles = new int[TriangleCount * 3];

            float h = Mathf.Max(1f, m_HalfSize);
            float d = Mathf.Max(0f, m_SkirtDepth);

            int v = 0;
            int t = 0;

            // ---- 顶面：法线朝下（朝水体内部） ----
            // 从下往上看它是正面，也就是玩家实际会看到的那一面。
            AddQuad(vertices, normals, shellParams, ref v,
                    new Vector3(-h, 0f, -h),
                    new Vector3(h, 0f, -h),
                    new Vector3(h, 0f, h),
                    new Vector3(-h, 0f, h),
                    new Vector3(0f, -1f, 0f),
                    0f, 0f, 0f, 0f);

            AddQuadTriangles(triangles, ref t, 0);

            // ---- 四片裙边：法线都朝内 ----
            // shellT 从 0（贴水面）渐变到 1（裙边底部），shader 靠它把颜色压深。
            int baseIndex = v;

            AddQuad(vertices, normals, shellParams, ref v,
                    new Vector3(h, 0f, -h),
                    new Vector3(h, -d, -h),
                    new Vector3(h, -d, h),
                    new Vector3(h, 0f, h),
                    new Vector3(-1f, 0f, 0f),
                    0f, 1f, 1f, 0f);
            AddQuadTriangles(triangles, ref t, baseIndex);

            baseIndex = v;
            AddQuad(vertices, normals, shellParams, ref v,
                    new Vector3(-h, 0f, h),
                    new Vector3(-h, -d, h),
                    new Vector3(-h, -d, -h),
                    new Vector3(-h, 0f, -h),
                    new Vector3(1f, 0f, 0f),
                    0f, 1f, 1f, 0f);
            AddQuadTriangles(triangles, ref t, baseIndex);

            baseIndex = v;
            AddQuad(vertices, normals, shellParams, ref v,
                    new Vector3(h, 0f, h),
                    new Vector3(h, -d, h),
                    new Vector3(-h, -d, h),
                    new Vector3(-h, 0f, h),
                    new Vector3(0f, 0f, -1f),
                    0f, 1f, 1f, 0f);
            AddQuadTriangles(triangles, ref t, baseIndex);

            baseIndex = v;
            AddQuad(vertices, normals, shellParams, ref v,
                    new Vector3(-h, 0f, -h),
                    new Vector3(-h, -d, -h),
                    new Vector3(h, -d, -h),
                    new Vector3(h, 0f, -h),
                    new Vector3(0f, 0f, 1f),
                    0f, 1f, 1f, 0f);
            AddQuadTriangles(triangles, ref t, baseIndex);

            m_Mesh.SetVertices(vertices);
            m_Mesh.SetNormals(normals);
            m_Mesh.SetUVs(ShellParamChannel, shellParams);
            m_Mesh.SetTriangles(triangles, 0, true);
            m_Mesh.RecalculateBounds();

            filter.sharedMesh = m_Mesh;

            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                // 水面不投影、不接光、不参与探针和遮挡剔除：
                // 它是这个世界的"天"，不该出现在任何光照计算里。
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                meshRenderer.allowOcclusionWhenDynamic = false;
            }

            m_BuiltHalfSize = m_HalfSize;
            m_BuiltSkirtDepth = m_SkirtDepth;
        }

        private void ReleaseMesh()
        {
            if (m_Mesh == null)
                return;

            if (Application.isPlaying)
                Destroy(m_Mesh);
            else
                DestroyImmediate(m_Mesh);

            m_Mesh = null;
        }

        private static void AddQuad(Vector3[] vertices, Vector3[] normals, Vector2[] shellParams, ref int cursor,
                                    Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                                    Vector3 normal,
                                    float t0, float t1, float t2, float t3)
        {
            vertices[cursor] = p0;
            vertices[cursor + 1] = p1;
            vertices[cursor + 2] = p2;
            vertices[cursor + 3] = p3;

            normals[cursor] = normal;
            normals[cursor + 1] = normal;
            normals[cursor + 2] = normal;
            normals[cursor + 3] = normal;

            shellParams[cursor] = new Vector2(t0, 0f);
            shellParams[cursor + 1] = new Vector2(t1, 0f);
            shellParams[cursor + 2] = new Vector2(t2, 0f);
            shellParams[cursor + 3] = new Vector2(t3, 0f);

            cursor += 4;
        }

        private static void AddQuadTriangles(int[] triangles, ref int cursor, int baseIndex)
        {
            triangles[cursor] = baseIndex;
            triangles[cursor + 1] = baseIndex + 1;
            triangles[cursor + 2] = baseIndex + 2;
            triangles[cursor + 3] = baseIndex;
            triangles[cursor + 4] = baseIndex + 2;
            triangles[cursor + 5] = baseIndex + 3;
            cursor += 6;
        }
    }
}

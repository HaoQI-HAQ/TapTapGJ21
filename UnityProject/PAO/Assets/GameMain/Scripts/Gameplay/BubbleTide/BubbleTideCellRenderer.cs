using UnityEngine;

namespace PAO.BubbleTide
{
    /// <summary>
    /// 泡泡潮的可视化。**它不生成任何泡泡物体** —— 潮水的外观全部由
    /// BubbleTideRenderFeature 的一次全屏光线步进画出来。
    ///
    /// 【为什么不做成几千个泡泡 GameObject】
    /// 一个能淹掉 43×13×33 米房间的潮水，用 0.5 米的泡泡去填是几十万颗。
    /// 就算每颗只有 1 KB，也是几百 MB 内存 + 每帧几十万次 Transform 写入，
    /// CPU 单核直接跑满，手机更是不可能。而换成「把场烘成 3D 纹理 + 光线步进」之后：
    ///
    ///   潮水大小     →  对帧率没有任何影响
    ///   CPU 每帧     →  只是往 shader 里写一个 float
    ///   内存         →  150 KB 的纹理
    ///
    /// 这个组件负责的就是「把那个 float 写进去」，外加给 Inspector 提供调参与统计。
    /// 真正画画的是 BubbleTideRenderFeature + BubbleTide.shader。
    ///
    /// 所以这个类名虽然叫 CellRenderer，但它**不画格子** ——
    /// 保留这个名字是为了和「潮水由体素场驱动」这件事对上。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/泡泡潮/潮水可视化")]
    public sealed class BubbleTideCellRenderer : MonoBehaviour
    {
        [Header("渲染")]
        [Tooltip("关掉后潮水完全不画，但计时与判定照常 —— 用来单独确认玩法手感。")]
        [SerializeField] private bool m_RenderVisuals = true;

        [Tooltip("潮水的潮位偏移（米）。正值让潮水面看起来更靠前，用来微微提前压迫感。")]
        [SerializeField, Range(-2f, 2f)] private float m_FrontBias = 0f;

        [Header("调试")]
        [Tooltip("在 Scene 视图里用线框画出潮水面附近的一小块，方便确认潮水确实在推进。")]
        [SerializeField] private bool m_DrawFrontGizmo = true;

        [Tooltip("线框数量。太多会拖慢 Scene 视图。")]
        [SerializeField, Range(8, 400)] private int m_GizmoSampleCount = 64;

        // ---------------- 运行时 ----------------

        private BubbleTideField m_Field;
        private BubbleTideAnchor[] m_Anchors;

        private float m_NormalizedElapsed;
        private int m_NewlyFloodedThisFrame;
        private bool m_FieldDirty = true;

        // 最近一次画出来的潮水面中心，给 Gizmo 用
        private Vector3 m_FrontCenter;
        private bool m_HasFrontCenter;

        private static readonly int s_ElapsedId = Shader.PropertyToID("_BubbleTideElapsed");
        private static readonly int s_EnabledId = Shader.PropertyToID("_BubbleTideEnabled");
        private static readonly int s_NormalizeScaleId = Shader.PropertyToID("_BubbleTideNormalizeScale");

        /// <summary>归一化后的潮水时间（0~1，1 = 整个房间淹满）。shader 直接读它。</summary>
        public float NormalizedElapsed { get { return m_NormalizedElapsed; } }

        /// <summary>这一帧新淹到的格子数。</summary>
        public int NewlyFloodedThisFrame { get { return m_NewlyFloodedThisFrame; } }

        /// <summary>可视化是否开启。</summary>
        public bool RenderVisuals { get { return m_RenderVisuals; } set { m_RenderVisuals = value; } }

        /// <summary>
        /// 由 Director 在烤制完成后调用。
        /// </summary>
        public void Bind(BubbleTideField field, BubbleTideAnchor[] anchors)
        {
            m_Field = field;
            m_Anchors = anchors;
            m_FieldDirty = true;
        }

        /// <summary>
        /// 场被局部改过（炸弹炸洞、黏浮泡泡筑墙），需要让渲染端知道要刷新。
        /// </summary>
        public void MarkFieldDirty()
        {
            m_FieldDirty = true;
        }

        /// <summary>
        /// 每帧由 Director 调用，推进可视化。
        /// </summary>
        /// <param name="elapsedSeconds">潮水已经推进的秒数。</param>
        /// <param name="newlyFlooded">这一帧新淹到的格子数。</param>
        public void UpdateFlood(float elapsedSeconds, int newlyFlooded)
        {
            m_NewlyFloodedThisFrame = newlyFlooded;

            if (m_Field == null || !m_Field.IsBaked)
            {
                return;
            }

            m_NormalizedElapsed = m_Field.NormalizeScale > 1e-4f
                ? elapsedSeconds / m_Field.NormalizeScale
                : 0f;

            // 唯一一件必须每帧做的事：把一个 float 写给 shader。
            // 没有 CPU 端的逐格遍历，没有网格重建，没有 GameObject。
            Shader.SetGlobalFloat(s_ElapsedId, m_NormalizedElapsed + NormalizedBias);
            Shader.SetGlobalFloat(s_EnabledId, m_RenderVisuals ? 1f : 0f);
            Shader.SetGlobalFloat(s_NormalizeScaleId, m_Field.NormalizeScale);

            // Gizmo 用：从玩家往上游退，找到还没被淹的那一格，就是潮水面
            UpdateFrontCenter(elapsedSeconds);

            if (m_FieldDirty)
            {
                RefreshTexture();
                m_FieldDirty = false;
            }
        }

        /// <summary>
        /// 把 FrontBias（米）换算成归一化时间上的偏移，用来微微提前或推后潮水面。
        /// 只是手感微调，不做精确的速度场反查。
        /// </summary>
        private float NormalizedBias
        {
            get
            {
                if (Mathf.Approximately(m_FrontBias, 0f) || m_Field == null || m_Field.CellSize < 1e-4f)
                {
                    return 0f;
                }

                // 用「一格要走多久」当尺度：15 万格的场上这个近似完全够用
                return m_FrontBias / m_Field.CellSize * 0.002f;
            }
        }

        /// <summary>
        /// 场被局部修改之后重新上传纹理。
        /// 这是唯一会重新上传整张纹理的路径，炸洞/筑墙时才会走到，不是每帧。
        /// </summary>
        private void RefreshTexture()
        {
            if (BubbleTideDirector.FieldTexture == null || m_Field == null)
            {
                return;
            }

            byte[] volume = m_Field.BuildNormalizedVolume();
            if (volume == null || volume.Length != BubbleTideDirector.FieldTexture.width
                * BubbleTideDirector.FieldTexture.height * BubbleTideDirector.FieldTexture.depth)
            {
                return;
            }

            BubbleTideDirector.FieldTexture.SetPixelData(volume, 0);
            BubbleTideDirector.FieldTexture.Apply(false, false);
        }

        private void UpdateFrontCenter(float elapsedSeconds)
        {
            if (m_Field == null || !m_Field.IsBaked)
            {
                return;
            }

            // 没有玩家就退化成「场里第一个可达格子」
            Vector3 reference = transform.position;
            PAO.PlayerController controller = FindObjectOfType<PAO.PlayerController>();
            if (controller != null)
            {
                reference = controller.transform.position;
            }

            // 从参考点往潮水来的方向退，直到退出淹没区
            Vector3 flow = m_Field.SampleFlowDirection(reference);
            if (flow.sqrMagnitude < 1e-5f)
            {
                m_HasFrontCenter = false;
                return;
            }

            Vector3 probe = reference;
            float step = Mathf.Max(0.25f, m_Field.CellSize);
            for (int i = 0; i < 240; i++)
            {
                if (m_Field.SampleArrivalTime(probe) > elapsedSeconds)
                {
                    break;
                }

                probe -= flow * step;
            }

            m_FrontCenter = probe;
            m_HasFrontCenter = true;
        }

        private void OnDrawGizmosSelected()
        {
            if (!m_DrawFrontGizmo || m_Field == null || !m_Field.IsBaked || !m_HasFrontCenter)
            {
                return;
            }

            // 在潮水面附近撒一圈点。用确定性哈希而不是随机，
            // 这样 Scene 视图不会每帧乱抖。
            Gizmos.color = new Color(0.45f, 0.85f, 1f, 0.55f);

            int count = Mathf.Clamp(m_GizmoSampleCount, 8, 400);
            float radius = Mathf.Max(1f, m_Field.CellSize * 6f);
            float elapsedSeconds = m_NormalizedElapsed * m_Field.NormalizeScale;

            for (int i = 0; i < count; i++)
            {
                float a = i * 2.39996f;              // 黄金角，做个均匀的螺旋分布
                float r = radius * Mathf.Sqrt((i + 0.5f) / count);

                Vector3 offset = new Vector3(
                    Mathf.Cos(a) * r,
                    Hash(i, 17) * radius * 0.6f - radius * 0.3f,
                    Mathf.Sin(a) * r);

                Vector3 p = m_FrontCenter + offset;

                // 已经被淹的画大一点，还没淹的用更小的点
                bool flooded = elapsedSeconds >= m_Field.SampleArrivalTime(p);
                Gizmos.DrawWireSphere(p, flooded ? 0.22f : 0.1f);
            }
        }

        /// <summary>确定性哈希，Gizmo 用来避免每帧抖动。</summary>
        private static float Hash(int index, int salt)
        {
            unchecked
            {
                uint h = (uint)(index * 73856093) ^ (uint)(salt * 19349663);
                h ^= h >> 13;
                h *= 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }
    }
}

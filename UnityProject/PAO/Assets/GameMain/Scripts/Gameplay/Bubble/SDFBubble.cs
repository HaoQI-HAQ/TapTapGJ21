using UnityEngine;

namespace PAO.BubbleFX
{
    /// <summary>
    /// 挂在任意 GameObject 上，把这颗物体登记成场景里的一颗泡泡。
    ///
    /// 它本身【不渲染任何东西】—— 物体上挂不挂 MeshRenderer / SpriteRenderer
    /// 都无所谓，甚至可以不挂。真正的画面由 BubbleSDFRenderFeature 插进 URP
    /// 的那个全屏 Pass 统一画出来。
    ///
    /// 所以：Rigidbody / Collider 继续管物理，SDFBubble 只管「长什么样」。
    /// 泡泡之间的融合是着色器里的距离场平滑并集，不需要物理接触。
    ///
    /// 用法：
    ///   1. 场景里建个空物体，Add Component -> SDF Bubble
    ///   2. 或者让生成器拿到泡泡后 bubble.AddComponent&lt;SDFBubble&gt;()
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/Bubble/SDF Bubble")]
    public sealed class SDFBubble : MonoBehaviour
    {
        [Header("形状")]
        [Tooltip("球半径（米）。会再乘上 transform 的 lossyScale 里最大的那个轴。")]
        [SerializeField, Min(0.001f)] private float m_Radius = 0.5f;

        [Tooltip("壳厚占半径的比例。越小越接近真实肥皂泡，0.02~0.15 比较好看。")]
        [SerializeField, Range(0.004f, 0.5f)] private float m_Thickness = 0.06f;

        [Header("外观")]
        [Tooltip("泡泡固有色。中心几乎透明，主要影响掠射边缘的着色。")]
        [SerializeField] private Color m_Color = new Color(0.72f, 0.88f, 1f, 1f);

        [Tooltip("与其他泡泡的融合（粘连）强度，单位是米。0 = 硬碰硬，0.2 左右有肥皂泡黏连感。")]
        [SerializeField, Range(0f, 2f)] private float m_FuseStrength = 0.22f;

        [Header("表面不安定感")]
        [Tooltip("表面抖动幅度（相对半径）。0 = 完美球，0.1 左右就有明显晃动。")]
        [SerializeField, Range(0f, 1f)] private float m_Distortion = 0.08f;

        [Tooltip("抖动频率。越大表面的波纹越碎。")]
        [SerializeField, Range(0f, 30f)] private float m_DistortionFrequency = 3f;

        [Header("渲染")]
        [Tooltip("关掉后这颗泡泡不参与 SDF，完全不可见。")]
        [SerializeField] private bool m_Visible = true;

        /// <summary>
        /// 打包好的 GPU 数据。刻意不加 [SerializeField]：
        /// 它字段名和 MonoBehaviour 基类容易撞，而且本来就是每帧重算的。
        /// </summary>
        private BubblePayload m_Payload;

        /// <summary>本帧打包好的 GPU 数据（值类型，取到的是快照）。</summary>
        public BubblePayload Payload
        {
            get { return m_Payload; }
        }

        /// <summary>颜色（运行时可改，例如按类型染色）。</summary>
        public Color Color
        {
            get { return m_Color; }
            set { m_Color = value; }
        }

        /// <summary>球半径（世界单位，不含 transform 缩放）。</summary>
        public float Radius
        {
            get { return m_Radius; }
            set { m_Radius = Mathf.Max(0.001f, value); }
        }

        /// <summary>融合强度（米）。</summary>
        public float FuseStrength
        {
            get { return m_FuseStrength; }
            set { m_FuseStrength = value; }
        }

        /// <summary>壳厚占半径的比例。</summary>
        public float Thickness
        {
            get { return m_Thickness; }
            set { m_Thickness = Mathf.Clamp(value, 0.004f, 0.5f); }
        }

        /// <summary>抖动幅度（相对半径）。</summary>
        public float Distortion
        {
            get { return m_Distortion; }
            set { m_Distortion = Mathf.Clamp01(value); }
        }

        /// <summary>
        /// 用于视锥剔除的世界包围盒。
        /// 半径额外放大 1.35 倍以容纳表面抖动与融合处的凸起。
        /// </summary>
        public Bounds WorldBounds
        {
            get
            {
                float r = EffectiveRadius() * 1.35f;
                return new Bounds(transform.position, new Vector3(r * 2f, r * 2f, r * 2f));
            }
        }

        private void OnEnable()
        {
            Refresh();
            BubbleSDFManager.Register(this);
        }

        private void OnDisable()
        {
            BubbleSDFManager.Unregister(this);
        }

        private void Update()
        {
            Refresh();
        }

        private void OnValidate()
        {
            // 编辑器里拖参数时也能立刻看到结果
            Refresh();
        }

        /// <summary>把当前 transform 与参数重新打包进 Payload。</summary>
        public void Refresh()
        {
            float worldRadius = EffectiveRadius();

            m_Payload.shapeType = 1f;      // 1 = 球
            m_Payload.operation = -1f;

            m_Payload.positionX = transform.position.x;
            m_Payload.positionY = transform.position.y;
            m_Payload.positionZ = transform.position.z;

            // size 传直径
            m_Payload.sizeX = worldRadius * 2f;
            m_Payload.sizeY = worldRadius * 2f;
            m_Payload.sizeZ = worldRadius * 2f;

            m_Payload.colorR = m_Color.r;
            m_Payload.colorG = m_Color.g;
            m_Payload.colorB = m_Color.b;
            // alpha = 0 时 shader 不会画出这颗泡泡
            m_Payload.colorA = m_Visible ? m_Color.a : 0f;

            m_Payload.shellRatio     = m_Thickness;
            m_Payload.fuseStrength   = m_FuseStrength;
            m_Payload.distortion     = m_Distortion;
            m_Payload.distortionFreq = m_DistortionFrequency;

            m_Payload.blendStrength = m_FuseStrength;
            m_Payload.numChildren   = 0f;
            m_Payload.phase         = StablePhase();
            m_Payload.pad0          = 0f;
        }

        /// <summary>半径 × transform 缩放里最大的那根轴。</summary>
        private float EffectiveRadius()
        {
            Vector3 scale = transform.lossyScale;
            float scaleMax = Mathf.Max(Mathf.Abs(scale.x),
                              Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            return Mathf.Max(m_Radius * scaleMax, 1e-4f);
        }

        /// <summary>
        /// 每颗泡泡一个稳定的相位，用来错开彩虹的起始颜色。
        /// 用 instanceID 而不是 Random：编辑器和运行时一致，也不需要序列化。
        /// </summary>
        private float StablePhase()
        {
            unchecked
            {
                uint h = (uint)GetInstanceID() * 2654435761u;
                return (h & 0xFFFF) / 65535f;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 scale = transform.lossyScale;
            float scaleMax = Mathf.Max(Mathf.Abs(scale.x),
                              Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            float r = m_Radius * scaleMax;

            Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, r);

            // 融合作用范围
            Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, r + m_FuseStrength);
        }
#endif
    }
}

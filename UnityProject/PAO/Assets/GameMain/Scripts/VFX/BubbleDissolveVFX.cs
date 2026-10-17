using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 泡泡逐渐溶解消失 —— **保持泡泡原本的样子**被一点点吃掉。
    ///
    /// 【这一版改了什么，为什么改】
    /// 早先的做法是：到点消失时把泡泡的材质**换成** PAO/BubbleDissolve
    /// （一个只有"底色 + 发光边"的简化着色器），然后推进它的 _DissolveAmount。
    ///
    /// 问题在于 BubbleBall 里有彩虹虹彩、卡通色阶、双高光、边缘光、透射、描边，
    /// 而 BubbleDissolve 里一个都没有。所以溶解的那一两秒里，
    /// 泡泡会**突然变成一块扁平的蓝色碎片** —— 玩家看到的是
    /// "泡泡变成了另一个东西"，而不是"泡泡正在消失"。
    ///
    /// 现在改成：溶解直接做在 BubbleBall 着色器里（见 BubbleBallCore.hlsl 的
    /// DissolveEdgeAmount）。它只做一件事 —— **裁掉像素**，
    /// 其余像素的着色公式一模一样。所以溶解全程泡泡都还是原来那个泡泡。
    ///
    /// 【不再创建新材质】
    /// 因为不用换材质了，这里只是往目标 Renderer 上写 MaterialPropertyBlock：
    ///   _DissolveEnabled = 1   （打开裁剪）
    ///   _DissolveAmount  = 0→1 （进度）
    ///
    /// 【为什么必须用 PropertyBlock 而不是改材质】
    /// 泡泡的材质是**按类型共享**的（BubbleLauncher.m_RuntimeMaterials 按索引缓存）——
    /// 直接改材质会让场上所有同类泡泡一起开始溶解。
    /// PropertyBlock 挂在单个 Renderer 上，互不影响。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/VFX/Bubble Dissolve VFX")]
    public sealed class BubbleDissolveVFX : MonoBehaviour
    {
        /// <summary>溶解必须依赖 BubbleBall 着色器 —— 它是唯一带裁剪逻辑的那个。</summary>
        private const string kBubbleShader = "PAO/BubbleBall";

        // 属性 ID 一次算好。每帧用字符串找属性很贵，而且写错名字不会报错、
        // 只会静默不生效 —— 用 PropertyToID 至少能让拼写集中一处。
        private static readonly int kEnabledID  = Shader.PropertyToID("_DissolveEnabled");
        private static readonly int kProgressID = Shader.PropertyToID("_DissolveAmount");
        private static readonly int kNoiseID    = Shader.PropertyToID("_DissolveNoise");

        // ---- 按类型区分溶解手感 ----
        //
        // 溶解本来就该慢（"慢慢溶解"），所以三个时长都比火烧那版长得多。
        // 差异保留，但差异体现在节奏的"轻重"而不是"快慢到看不见"。

        /// <summary>浮粘：黏住、留下 —— 溶解最慢、最舍不得</summary>
        private const float kStickyDuration = 2.10f;

        /// <summary>弹力：干脆 —— 溶解中等</summary>
        private const float kBouncyDuration = 1.55f;

        /// <summary>爆炸：引信烧完就没了 —— 溶解最快</summary>
        private const float kBombDuration   = 1.15f;

        private Renderer m_TargetRenderer;
        private MaterialPropertyBlock m_Block;

        private float m_Duration;
        private float m_Elapsed;
        private bool m_Running;
        private System.Action m_OnFinished;

        /// <summary>是否正在溶解。</summary>
        public bool IsDissolving { get { return m_Running; } }

        /// <summary>溶解进度 0~1。</summary>
        public float Progress
        {
            get { return m_Duration > 0.001f ? Mathf.Clamp01(m_Elapsed / m_Duration) : 1f; }
        }

        // ==================== 对外接口 ====================

        /// <summary>
        /// 让一个泡泡开始溶解。
        /// </summary>
        /// <param name="bubble">要溶解的泡泡物体（用它的 Renderer）</param>
        /// <param name="type">泡泡类型，决定溶解时长</param>
        /// <param name="onFinished">溶解完成时回调。调用方一般在这里 Destroy 泡泡</param>
        public static BubbleDissolveVFX Begin(GameObject bubble, BubbleType type,
                                              System.Action onFinished = null)
        {
            if (bubble == null)
            {
                if (onFinished != null) { onFinished(); }
                return null;
            }

            var root = new GameObject("Bubble Dissolve VFX");
            root.transform.position = bubble.transform.position;
            root.transform.rotation = bubble.transform.rotation;

            var fx = root.AddComponent<BubbleDissolveVFX>();
            fx.Setup(bubble, type, onFinished);
            return fx;
        }

        private void Setup(GameObject bubble, BubbleType type, System.Action onFinished)
        {
            m_OnFinished = onFinished;
            m_Duration = DurationFor(type);

            m_TargetRenderer = bubble.GetComponentInChildren<Renderer>();

            if (m_TargetRenderer == null)
            {
                Debug.LogWarning("[BubbleDissolveVFX] 泡泡上没有 Renderer，溶解无法进行，直接结束。");
                Finish();
                return;
            }

            // 确认泡泡用的是 BubbleBall。用别的着色器（比如代码生成的
            // URP Lit 占位材质）就没有裁剪逻辑，_DissolveAmount 写进去也不理你 ——
            // 那种情况下直通结束，至少不会留下一个永远不消失的泡泡。
            Material mat = m_TargetRenderer.sharedMaterial;

            if (mat == null || !mat.HasProperty(kProgressID))
            {
                Debug.LogWarning("[BubbleDissolveVFX] 泡泡材质不是 " + kBubbleShader +
                                 "（没有 _DissolveAmount 属性），溶解不会生效，直接结束。" +
                                 "当前材质：" + (mat != null ? mat.name : "null") +
                                 "，着色器：" + (mat != null && mat.shader != null ? mat.shader.name : "null"));
                Finish();
                return;
            }

            m_Block = new MaterialPropertyBlock();

            // 噪声密度按泡泡尺寸反比 ——
            // 大泡泡用同样密度，溶解斑会显得太大块；
            // 小泡泡密度不够，边缘会糊成一片没有形状。
            Vector3 lossy = bubble.transform.lossyScale;
            float diameter = Mathf.Max(lossy.x, Mathf.Max(lossy.y, lossy.z));
            float noise = diameter > 0.01f ? Mathf.Clamp(5f / diameter, 2f, 14f) : 5f;

            m_Running = true;

            // 先写 0 进度 + 打开开关。
            // 注意顺序：先 SetFloat 全部写完，再 SetPropertyBlock 一次 ——
            // 每次 SetPropertyBlock 都是一次上传，分开写会多传几遍。
            Apply(0f, noise);
        }

        private void Update()
        {
            if (!m_Running)
            {
                return;
            }

            // 用 unscaledDeltaTime：暂停时（timeScale = 0）溶解也该正常推进，
            // 否则暂停期间泡泡会卡在溶一半的状态
            m_Elapsed += Time.unscaledDeltaTime;

            float p = Progress;
            Apply(p, -1f);   // -1 = 不改噪声密度

            if (p >= 1f)
            {
                Finish();
            }
        }

        /// <summary>
        /// 把开关和进度写进 PropertyBlock。只影响这一个 Renderer。
        /// </summary>
        /// <param name="progress">溶解进度 0~1</param>
        /// <param name="noise">&gt; 0 时同时写噪声密度；传 -1 表示不动它</param>
        private void Apply(float progress, float noise)
        {
            if (m_TargetRenderer == null || m_Block == null)
            {
                return;
            }

            m_TargetRenderer.GetPropertyBlock(m_Block);

            // 开关：只有它是 1，着色器里才会执行 clip
            // （正常泡泡 _DissolveEnabled = 0，不会为溶解付任何开销）
            m_Block.SetFloat(kEnabledID, 1f);
            m_Block.SetFloat(kProgressID, progress);

            if (noise > 0f)
            {
                m_Block.SetFloat(kNoiseID, noise);
            }

            m_TargetRenderer.SetPropertyBlock(m_Block);
        }

        private void Finish()
        {
            m_Running = false;

            // 交还控制权之前先断开引用，避免调用方 Destroy 时还挂着我们的 block
            m_TargetRenderer = null;

            if (m_OnFinished != null)
            {
                System.Action cb = m_OnFinished;
                m_OnFinished = null;
                cb();
            }

            Destroy(gameObject);
        }

        // ==================== 辅助 ====================

        private static float DurationFor(BubbleType type)
        {
            switch (type)
            {
                case BubbleType.Bouncy: return kBouncyDuration;
                case BubbleType.Bomb:   return kBombDuration;
                default:                return kStickyDuration;
            }
        }
    }
}

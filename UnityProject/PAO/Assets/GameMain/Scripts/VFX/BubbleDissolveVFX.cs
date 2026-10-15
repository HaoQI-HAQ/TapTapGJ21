using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 泡泡逐渐溶解消失。
    ///
    /// 【参考效果】
    /// 一个实体被慢慢"吃掉"：一条发亮的边缘沿有机噪声边界推进，
    /// 边缘内侧真的少掉一块，整体逐渐消失 —— 不是整体调透明度变淡。
    ///
    /// 【和前两版的关系（这两版都是弯路，留在这里避免再走回去）】
    ///   1. 最初的 BubbleDissolvePuff：着色器里有 `_Erode`（边缘侵蚀）参数，
    ///      但**从没随时间驱动过**。所以实际效果只是"整体淡出"，
    ///      泡泡并没有被吃掉。这是那一版最根本的 bug。
    ///   2. 后来的 BubbleDissolveBurn：做成了火烧 —— 橘色火苗、焦黑、火星飞溅。
    ///      那是"燃烧"，不是"溶解"。火是一个强烈的事件；
    ///      溶解应该是安静的。所以这一版把火全部去掉了。
    ///
    /// 【现在用什么】
    ///   PAO/BubbleDissolve 着色器 + MaterialPropertyBlock 每帧推进 _DissolveAmount。
    ///
    /// 【为什么用 MaterialPropertyBlock 而不是改材质】
    /// 泡泡的材质是**按类型共享**的（BubbleLauncher.m_RuntimeMaterials 按索引缓存）——
    /// 直接改材质会让场上所有同类泡泡一起开始溶解。
    /// PropertyBlock 挂在单个 Renderer 上，互不影响。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/VFX/Bubble Dissolve VFX")]
    public sealed class BubbleDissolveVFX : MonoBehaviour
    {
        private const string kDissolveShader = "PAO/BubbleDissolve";

        /// <summary>溶解进度属性名。</summary>
        private static readonly int kDissolveID = Shader.PropertyToID("_DissolveAmount");

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
        private Material m_DissolveMaterial;

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

            Shader shader = Shader.Find(kDissolveShader);

            if (shader == null)
            {
                Debug.LogWarning("[BubbleDissolveVFX] 找不到着色器 " + kDissolveShader +
                                 "，泡泡会直接消失（没有溶解效果）。请确认 " +
                                 "Assets/GameMain/Art/Shaders/BubbleDissolve.shader 存在且编译通过。");
                Finish();
                return;
            }

            m_DissolveMaterial = new Material(shader);
            m_DissolveMaterial.hideFlags = HideFlags.DontSave;

            // 继承泡泡原来的底色，溶解起点不会突变
            Color baseColor = ReadBaseColor(m_TargetRenderer);

            if (m_DissolveMaterial.HasProperty("_BaseColor"))
            {
                m_DissolveMaterial.SetColor("_BaseColor", baseColor);
            }

            // 溶解中的颜色：把底色往白里提一点，看起来像"材质在变淡"
            if (m_DissolveMaterial.HasProperty("_FadeTint"))
            {
                Color fade = Color.Lerp(baseColor, Color.white, 0.35f);
                fade.a = baseColor.a;
                m_DissolveMaterial.SetColor("_FadeTint", fade);
            }

            // 噪声密度按泡泡尺寸反比 ——
            // 大泡泡用同样密度，火痕/溶解斑会显得太大块
            Vector3 lossy = bubble.transform.lossyScale;
            float diameter = Mathf.Max(lossy.x, Mathf.Max(lossy.y, lossy.z));

            if (diameter > 0.01f && m_DissolveMaterial.HasProperty("_NoiseScale"))
            {
                m_DissolveMaterial.SetFloat("_NoiseScale", Mathf.Clamp(7f / diameter, 3f, 22f));
            }

            m_TargetRenderer.sharedMaterial = m_DissolveMaterial;

            m_Block = new MaterialPropertyBlock();
            m_Running = true;

            ApplyProgress(0f);
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
            ApplyProgress(p);

            if (p >= 1f)
            {
                Finish();
            }
        }

        /// <summary>把进度写进 PropertyBlock。只影响这一个 Renderer。</summary>
        private void ApplyProgress(float progress)
        {
            if (m_TargetRenderer == null || m_Block == null)
            {
                return;
            }

            m_TargetRenderer.GetPropertyBlock(m_Block);
            m_Block.SetFloat(kDissolveID, progress);
            m_TargetRenderer.SetPropertyBlock(m_Block);
        }

        private void Finish()
        {
            m_Running = false;

            // 交还控制权之前先断开引用，避免调用方 Destroy 时还挂着我们的材质
            m_TargetRenderer = null;

            if (m_OnFinished != null)
            {
                System.Action cb = m_OnFinished;
                m_OnFinished = null;
                cb();
            }

            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (m_DissolveMaterial != null)
            {
                Destroy(m_DissolveMaterial);
            }
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

        /// <summary>从泡泡原来的材质里取底色。</summary>
        private static Color ReadBaseColor(Renderer renderer)
        {
            if (renderer == null || renderer.sharedMaterial == null)
            {
                return new Color(0.55f, 0.85f, 1f, 1f);
            }

            Material mat = renderer.sharedMaterial;

            if (mat.HasProperty("_BaseColor")) { return mat.GetColor("_BaseColor"); }
            if (mat.HasProperty("_Color"))     { return mat.GetColor("_Color"); }

            return mat.color;
        }
    }
}

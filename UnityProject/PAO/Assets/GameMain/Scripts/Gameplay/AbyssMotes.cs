using UnityEngine;

namespace PAO
{
    /// <summary>
    /// 深渊浮尘：运行时在脚下生成一层缓慢上升的冷色微粒。
    ///
    /// 【为什么用代码生成，而不是在预制体里摆一个 ParticleSystem】
    /// ParticleSystem 的序列化数据又长又脆，改一个参数就要动一大段 YAML。
    /// 这里只保留"数量 / 范围 / 大小 / 速度"几个字段，其余全部在 Build 里写死，
    /// 预制体只需要挂这个组件 + 一个材质。
    ///
    /// 【为什么粒子挂在一个反缩放的子物体上】
    /// 深渊预制体整体是 20 倍缩放的。粒子如果直接挂在下面，形状和大小都会被放大 20 倍。
    /// 这里生成一个 localScale = 1 / 父级缩放 的子物体，把世界缩放抵消回 1，
    /// 于是 m_WorldRadius / m_Size 填的就是真实米数。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/关卡/深渊浮尘（Abyss Motes）")]
    public sealed class AbyssMotes : MonoBehaviour
    {
        [Header("外观")]
        [Tooltip("粒子材质（用 PAO/Environment/AbyssMote）")]
        [SerializeField] private Material m_Material = null;

        [SerializeField] private Color m_Color = new Color(0.45f, 0.85f, 1f, 0.8f);

        [Tooltip("单个粒子大小（米）")]
        [SerializeField] private float m_Size = 0.35f;

        [Header("范围")]
        [Tooltip("生成范围半径（米）")]
        [SerializeField] private float m_WorldRadius = 35f;

        [Tooltip("生成范围高度（米），粒子会在这一层里出生")]
        [SerializeField] private float m_WorldHeight = 12f;

        [Tooltip("同时存在的最大粒子数")]
        [SerializeField] private int m_MaxParticles = 220;

        [Tooltip("每秒生成数量")]
        [SerializeField] private float m_EmissionRate = 26f;

        [Header("运动")]
        [Tooltip("粒子存活时间（秒）")]
        [SerializeField] private float m_Lifetime = 9f;

        [Tooltip("上升速度（米/秒）")]
        [SerializeField] private float m_RiseSpeed = 0.5f;

        private ParticleSystem m_System;

        private void Awake()
        {
            Build();
        }

        [ContextMenu("重建浮尘（Play 模式下）")]
        private void Rebuild()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[AbyssMotes] 运行时才会生成粒子；进 Play 后再试。", this);
                return;
            }

            if (m_System != null)
            {
                Destroy(m_System.gameObject);
                m_System = null;
            }

            Build();
        }

        private void Build()
        {
            if (m_System != null)
            {
                return;
            }

            // 域重载关闭时，重进 Play 不会清掉上一次生成的子物体
            m_System = GetComponentInChildren<ParticleSystem>();

            if (m_System != null)
            {
                return;
            }

            if (m_Material == null)
            {
                Debug.LogWarning("[AbyssMotes] 没有指定粒子材质，浮尘不会显示。", this);
                return;
            }

            GameObject moteObject = new GameObject("Abyss Motes (Generated)");
            moteObject.transform.SetParent(transform, false);

            // 抵消父级缩放，让 m_WorldRadius / m_Size 是真实米数
            Vector3 lossy = transform.lossyScale;
            moteObject.transform.localScale = new Vector3(
                1f / Mathf.Max(0.0001f, lossy.x),
                1f / Mathf.Max(0.0001f, lossy.y),
                1f / Mathf.Max(0.0001f, lossy.z));

            m_System = moteObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = m_System.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = m_Lifetime;
            main.startSpeed = 0f;
            main.startSize = m_Size;
            main.startColor = m_Color;
            main.maxParticles = Mathf.Max(1, m_MaxParticles);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = m_System.emission;
            emission.enabled = true;
            emission.rateOverTime = Mathf.Max(0f, m_EmissionRate);

            ParticleSystem.ShapeModule shape = m_System.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(m_WorldRadius * 2f, Mathf.Max(0.1f, m_WorldHeight), m_WorldRadius * 2f);
            shape.position = new Vector3(0f, m_WorldHeight * 0.5f, 0f);

            ParticleSystem.VelocityOverLifetimeModule velocity = m_System.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.10f, 0.10f);
            velocity.y = new ParticleSystem.MinMaxCurve(m_RiseSpeed * 0.35f, m_RiseSpeed);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.10f, 0.10f);

            ParticleSystem.NoiseModule noise = m_System.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.15f;
            noise.scrollSpeed = 0.04f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.18f),
                    new GradientAlphaKey(0f, 1f),
                });

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = m_System.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = gradient;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = m_System.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.35f),
                    new Keyframe(0.25f, 1f),
                    new Keyframe(1f, 0.55f)));

            ParticleSystemRenderer particleRenderer = m_System.GetComponent<ParticleSystemRenderer>();

            if (particleRenderer == null)
            {
                particleRenderer = m_System.gameObject.AddComponent<ParticleSystemRenderer>();
            }

            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.alignment = ParticleSystemRenderSpace.View;
            particleRenderer.sortMode = ParticleSystemSortMode.Distance;
            particleRenderer.sharedMaterial = m_Material;
            particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
            particleRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            m_System.Play();
        }
    }
}

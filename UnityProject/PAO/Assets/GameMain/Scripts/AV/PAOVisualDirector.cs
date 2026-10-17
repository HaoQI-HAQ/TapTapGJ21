using System;
using UnityEngine;

namespace PAO.AV
{
    /// <summary>
    /// Map-independent visual response layer. Install one instance in a scene. All
    /// gameplay hooks arrive through FeedbackBus, the same bus used by audio.
    /// Particle systems and billboards are built at runtime; the two material
    /// references keep the shader in player builds and are set by the installer.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PAO/AV/Visual Director")]
    public sealed class PAOVisualDirector : MonoBehaviour
    {
        private static readonly int s_MoodId = Shader.PropertyToID("_PAO_Mood");
        private static readonly int s_EnergyId = Shader.PropertyToID("_PAO_Energy");
        private static readonly int s_ImpulseId = Shader.PropertyToID("_PAO_Impulse");

        [SerializeField] private Material m_RingMaterial;
        [SerializeField] private Material m_PearlMaterial;
        [SerializeField, Range(0f, 2f)] private float m_EffectScale = 1f;
        [SerializeField] private bool m_AmbientMembranes = true;
        [SerializeField, Range(0f, 2f)] private float m_AmbientRate = 0.85f;

        private ParticleSystem m_Rings;
        private ParticleSystem m_Pearls;
        private ParticleSystem m_Ambient;
        private Material m_RuntimeRingMaterial;
        private Material m_RuntimePearlMaterial;
        private Camera m_Camera;
        private float m_EnergyEnvelope;
        private float m_ImpulseEnvelope;
        private float m_ImpulseRadius = 4f;
        private Vector3 m_ImpulsePosition;
        private float m_Mood;
        private float m_AmbientClock;
        private float m_OverrideMood;
        private float m_OverrideRemaining;
        private readonly float[] m_LastCueTime = new float[Enum.GetValues(typeof(FeedbackCue)).Length];

        public static PAOVisualDirector Instance { get; private set; }

        public float CurrentEnergy { get { return Mathf.Clamp01(m_EnergyEnvelope); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            Shader.SetGlobalFloat(s_MoodId, 0f);
            Shader.SetGlobalFloat(s_EnergyId, 0f);
            Shader.SetGlobalVector(s_ImpulseId, Vector4.zero);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[PAO Visual] More than one visual director is active; disabling the duplicate.", this);
                enabled = false;
                return;
            }

            Instance = this;
            if (!BuildParticleSystems())
            {
                enabled = false;
                return;
            }

            for (int i = 0; i < m_LastCueTime.Length; i++) m_LastCueTime[i] = -100f;
            FeedbackBus.Raised += OnFeedback;
        }

        private void OnDisable()
        {
            FeedbackBus.Raised -= OnFeedback;
            if (Instance == this)
            {
                Instance = null;
                Shader.SetGlobalFloat(s_MoodId, 0f);
                Shader.SetGlobalFloat(s_EnergyId, 0f);
                Shader.SetGlobalVector(s_ImpulseId, Vector4.zero);
            }
            DestroyParticleSystem(ref m_Rings);
            DestroyParticleSystem(ref m_Pearls);
            DestroyParticleSystem(ref m_Ambient);
            DestroyMaterial(ref m_RuntimeRingMaterial);
            DestroyMaterial(ref m_RuntimePearlMaterial);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            m_EnergyEnvelope = Mathf.MoveTowards(m_EnergyEnvelope, 0f, dt * 0.85f);
            m_ImpulseEnvelope = Mathf.MoveTowards(m_ImpulseEnvelope, 0f, dt * 1.4f);
            m_OverrideRemaining = Mathf.Max(0f, m_OverrideRemaining - dt);

            PAOSceneAVState state = PAOSceneAVState.Instance;
            float stateEnergy = state != null ? Mathf.Clamp01(state.Energy) : 0f;
            float energy = Mathf.Clamp01(Mathf.Max(m_EnergyEnvelope, stateEnergy));
            float targetMood = m_OverrideRemaining > 0f
                ? m_OverrideMood
                : (state != null ? MoodValue(state.Mood) : 0f);
            m_Mood = Mathf.MoveTowards(m_Mood, targetMood, dt * 0.7f);

            Shader.SetGlobalFloat(s_MoodId, m_Mood);
            Shader.SetGlobalFloat(s_EnergyId, energy);
            Shader.SetGlobalVector(s_ImpulseId,
                new Vector4(m_ImpulsePosition.x, m_ImpulsePosition.y, m_ImpulsePosition.z,
                    m_ImpulseEnvelope * m_ImpulseRadius));

            if (!m_AmbientMembranes || m_Ambient == null || m_AmbientRate <= 0f) return;
            m_AmbientClock += dt * m_AmbientRate * (0.8f + energy * 0.8f);
            if (m_AmbientClock >= 1f)
            {
                m_AmbientClock -= 1f;
                EmitAmbient();
            }
        }

        private static float MoodValue(SceneMood mood)
        {
            switch (mood)
            {
                case SceneMood.Drift: return 0.25f;
                case SceneMood.Resonance: return 0.65f;
                case SceneMood.Danger: return -0.85f;
                case SceneMood.Resolution: return 1f;
                default: return 0f;
            }
        }

        private bool BuildParticleSystems()
        {
            Shader shader = Shader.Find("PAO/FeedbackVFX");
            if (m_RingMaterial == null && shader != null)
            {
                m_RuntimeRingMaterial = new Material(shader) { name = "PAO Feedback Ring (runtime)", hideFlags = HideFlags.DontSave };
                m_RuntimeRingMaterial.SetFloat("_Shape", 1f);
                m_RuntimeRingMaterial.SetFloat("_Softness", 0.12f);
            }
            if (m_PearlMaterial == null && shader != null)
            {
                m_RuntimePearlMaterial = new Material(shader) { name = "PAO Feedback Pearl (runtime)", hideFlags = HideFlags.DontSave };
                m_RuntimePearlMaterial.SetFloat("_Shape", 0f);
            }

            Material ring = m_RingMaterial != null ? m_RingMaterial : m_RuntimeRingMaterial;
            Material pearl = m_PearlMaterial != null ? m_PearlMaterial : m_RuntimePearlMaterial;
            if (ring == null || pearl == null)
            {
                Debug.LogError("[PAO Visual] PAO/FeedbackVFX shader or materials are missing.", this);
                return false;
            }

            m_Rings = MakeSystem("Membrane Ripples", ring, 96, true);
            m_Pearls = MakeSystem("Interaction Pearls", pearl, 256, false);
            m_Ambient = MakeSystem("Ambient Drift", pearl, 64, false);
            return true;
        }

        private ParticleSystem MakeSystem(string objectName, Material material, int capacity, bool expanding)
        {
            GameObject child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            ParticleSystem system = child.AddComponent<ParticleSystem>();
            // AddComponent starts a ParticleSystem immediately. Duration can only be
            // changed after it has stopped, including in batch-mode Play tests.
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.duration = 10f;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.startSize = 1f;
            main.maxParticles = capacity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(expanding ? 0.45f : 0.30f, 0f),
                    new GradientAlphaKey(1f, 0.18f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = fade;

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, expanding
                ? new AnimationCurve(new Keyframe(0f, 0.42f), new Keyframe(1f, 1.4f))
                : new AnimationCurve(new Keyframe(0f, 0.75f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.18f)));

            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = material;
            system.Play();
            return system;
        }

        private void OnFeedback(FeedbackEvent feedback)
        {
            int cueIndex = (int)feedback.Cue;
            if (cueIndex < 0 || cueIndex >= m_LastCueTime.Length) return;
            float cooldown = feedback.Cue == FeedbackCue.BubbleCharge ? 0.09f :
                feedback.Cue == FeedbackCue.TideRising ? 0.5f :
                feedback.Cue == FeedbackCue.PlayerStep ? 0.2f :
                feedback.Cue == FeedbackCue.UIHover ? 0.15f : 0f;
            if (Time.time - m_LastCueTime[cueIndex] < cooldown) return;
            m_LastCueTime[cueIndex] = Time.time;

            float strength = Mathf.Clamp01(feedback.Intensity);
            if (strength <= 0.001f) return;
            Vector3 position = feedback.Position;
            if (feedback.Cue == FeedbackCue.UIHover || feedback.Cue == FeedbackCue.UIConfirm ||
                feedback.Cue == FeedbackCue.Narration)
            {
                // UI and voice events have no meaningful world-space position.
                return;
            }
            bool screenSpaceFinale = feedback.Cue == FeedbackCue.ChapterComplete ||
                                     feedback.Cue == FeedbackCue.ChapterFailure;
            bool unspecifiedTidePosition = position == Vector3.zero &&
                (feedback.Cue == FeedbackCue.TideRising ||
                 feedback.Cue == FeedbackCue.TideWin ||
                 feedback.Cue == FeedbackCue.TideLose);
            if (screenSpaceFinale || unspecifiedTidePosition)
            {
                Camera camera = FindGameplayCamera();
                if (camera != null)
                    position = camera.ViewportToWorldPoint(new Vector3(0.5f,
                        feedback.Cue == FeedbackCue.TideRising ? 0.32f : 0.5f,
                        Mathf.Max(camera.nearClipPlane + 1f, 4f)));
            }

            m_EnergyEnvelope = Mathf.Max(m_EnergyEnvelope, strength * 0.85f);
            m_ImpulseEnvelope = Mathf.Max(m_ImpulseEnvelope, strength);
            m_ImpulsePosition = position;
            m_ImpulseRadius = 3.5f;

            switch (feedback.Cue)
            {
                case FeedbackCue.BubbleCharge:
                    EmitPearls(position, 2, 0.07f, 0.35f, new Color(0.63f, 0.94f, 1f, 0.65f), strength);
                    break;
                case FeedbackCue.BubbleLaunch:
                    EmitRing(position, 0.72f, 0.58f, new Color(0.59f, 0.97f, 1f, 0.85f), strength);
                    EmitPearls(position, 9, 0.12f, 0.75f, new Color(0.89f, 0.78f, 1f, 0.85f), strength);
                    break;
                case FeedbackCue.BubbleCollision:
                case FeedbackCue.BubbleBounce:
                case FeedbackCue.PlayerLand:
                    EmitRing(position, 0.58f, 0.42f, new Color(0.68f, 0.92f, 1f, 0.7f), strength);
                    EmitPearls(position, 6, 0.09f, 0.48f, new Color(0.91f, 0.85f, 1f, 0.8f), strength);
                    break;
                case FeedbackCue.BubbleMerge:
                    EmitRing(position, 1.3f, 1.0f, new Color(0.96f, 0.70f, 0.94f, 0.85f), strength);
                    EmitPearls(position, 12, 0.14f, 0.85f, new Color(0.73f, 1f, 0.94f, 0.8f), strength);
                    break;
                case FeedbackCue.BubbleBurst:
                case FeedbackCue.BubblePop:
                    // The existing BubbleBurstVFX owns the large splash. This is its
                    // thinner luminous wake, so the two effects do not obscure each other.
                    EmitRing(position, 1.2f, 0.55f, new Color(1f, 0.67f, 0.83f, 0.9f), strength);
                    EmitPearls(position, 8, 0.12f, 0.6f, new Color(1f, 0.88f, 0.93f, 0.8f), strength);
                    break;
                case FeedbackCue.TideRising:
                    m_ImpulseRadius = 5f;
                    EmitPearls(position, 7, 0.18f, 1.2f, new Color(0.75f, 0.68f, 1f, 0.45f), strength);
                    break;
                case FeedbackCue.TideWin:
                    m_OverrideMood = 1f;
                    m_OverrideRemaining = 3f;
                    EmitRing(position, 3f, 1.6f, new Color(0.72f, 1f, 0.88f, 0.95f), strength);
                    EmitPearls(position, 26, 0.20f, 1.8f, new Color(0.85f, 1f, 0.92f, 0.9f), strength);
                    break;
                case FeedbackCue.TideLose:
                    m_OverrideMood = -1f;
                    m_OverrideRemaining = 3f;
                    EmitRing(position, 2.2f, 1.1f, new Color(1f, 0.45f, 0.7f, 0.85f), strength);
                    break;
                case FeedbackCue.ChapterComplete:
                    m_OverrideMood = 1f;
                    m_OverrideRemaining = 5f;
                    m_EnergyEnvelope = 1f;
                    m_ImpulseRadius = 7f;
                    EmitRing(position, 3.6f, 2f, new Color(0.70f, 1f, 0.86f, 0.95f), strength);
                    EmitRing(position, 2.3f, 1.45f, new Color(0.84f, 0.76f, 1f, 0.68f), strength);
                    EmitPearls(position, 32, 0.19f, 2.2f, new Color(0.88f, 1f, 0.96f, 0.92f), strength);
                    break;
                case FeedbackCue.ChapterFailure:
                    m_OverrideMood = -1f;
                    m_OverrideRemaining = 4f;
                    m_EnergyEnvelope = 1f;
                    m_ImpulseRadius = 7f;
                    EmitRing(position, 3f, 1.65f, new Color(1f, 0.48f, 0.70f, 0.84f), strength);
                    EmitPearls(position, 18, 0.16f, 1.3f, new Color(0.77f, 0.63f, 1f, 0.75f), strength);
                    break;
                case FeedbackCue.PlayerStep:
                    EmitPearls(position, 2, 0.055f, 0.34f, new Color(0.77f, 0.90f, 1f, 0.42f), strength);
                    break;
                case FeedbackCue.BubbleSwitch:
                case FeedbackCue.PlayerJump:
                case FeedbackCue.Pickup:
                    EmitPearls(position, 7, 0.10f, 0.65f, new Color(0.83f, 0.97f, 1f, 0.78f), strength);
                    break;
            }
        }

        private void EmitRing(Vector3 position, float size, float life, Color color, float strength)
        {
            if (m_Rings == null || m_EffectScale <= 0f) return;
            ParticleSystem.EmitParams particle = new ParticleSystem.EmitParams();
            particle.position = position;
            particle.startSize = Mathf.Max(0.04f, size * m_EffectScale * Mathf.Lerp(0.6f, 1.4f, strength));
            particle.startLifetime = life;
            particle.startColor = color;
            m_Rings.Emit(particle, 1);
        }

        private void EmitPearls(Vector3 position, int count, float size, float life, Color color, float strength)
        {
            if (m_Pearls == null || m_EffectScale <= 0f) return;
            count = Mathf.Clamp(Mathf.CeilToInt(count * Mathf.Lerp(0.5f, 1f, strength)), 1, 32);
            for (int i = 0; i < count; i++)
            {
                ParticleSystem.EmitParams particle = new ParticleSystem.EmitParams();
                particle.position = position + UnityEngine.Random.insideUnitSphere * size * 1.5f;
                particle.velocity = (UnityEngine.Random.onUnitSphere + Vector3.up * 0.7f) *
                                    Mathf.Lerp(0.45f, 1.8f, strength);
                particle.startSize = size * m_EffectScale * UnityEngine.Random.Range(0.65f, 1.4f);
                particle.startLifetime = life * UnityEngine.Random.Range(0.8f, 1.2f);
                particle.startColor = color;
                m_Pearls.Emit(particle, 1);
            }
        }

        private void EmitAmbient()
        {
            Camera camera = FindGameplayCamera();
            if (camera == null) return;
            float depth = Mathf.Min(9f, Mathf.Max(camera.nearClipPlane + 0.5f, camera.farClipPlane * 0.06f));
            Vector3 point = camera.ViewportToWorldPoint(new Vector3(
                UnityEngine.Random.Range(0.05f, 0.95f), UnityEngine.Random.Range(0.05f, 0.95f), depth));
            ParticleSystem.EmitParams particle = new ParticleSystem.EmitParams();
            particle.position = point;
            particle.velocity = camera.transform.up * UnityEngine.Random.Range(0.07f, 0.18f);
            particle.startSize = UnityEngine.Random.Range(0.045f, 0.12f) * m_EffectScale;
            particle.startLifetime = UnityEngine.Random.Range(3.5f, 6f);
            particle.startColor = new Color(0.66f, 0.92f, 1f, 0.14f);
            m_Ambient.Emit(particle, 1);
        }

        private Camera FindGameplayCamera()
        {
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
                if (m_Camera == null && Camera.allCamerasCount > 0)
                    m_Camera = Camera.allCameras[0];
            }
            return m_Camera;
        }

        private static void DestroyParticleSystem(ref ParticleSystem system)
        {
            if (system == null) return;
            if (Application.isPlaying) Destroy(system.gameObject);
            else DestroyImmediate(system.gameObject);
            system = null;
        }

        private static void DestroyMaterial(ref Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
            material = null;
        }
    }
}

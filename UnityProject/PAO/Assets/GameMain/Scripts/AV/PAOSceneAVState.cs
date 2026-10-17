using PAO.BubbleTide;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PAO.AV
{
    public enum SceneMood { Drift, Explore, Resonance, Danger, Resolution }
    public enum SceneRegion { OpenWater, Reef, Vent, Trench }

    /// <summary>Tracks narrative space and activity for all audiovisual layers.</summary>
    [DefaultExecutionOrder(-90)]
    public sealed class PAOSceneAVState : MonoBehaviour
    {
        public static PAOSceneAVState Instance { get; private set; }
        public SceneMood Mood { get; private set; }
        public SceneRegion Region { get; private set; }
        public float Energy { get; private set; }

        private PlayerController player;
        private BubbleTideDirector tide;
        private Transform resonanceCore;
        private Transform memoryShard;
        private Transform ventAnchor;
        private Vector3 levelStart;
        private bool hasLevelStart;
        private bool resolved;
        private bool failed;
        private float impulse;
        private float nextSearch;
        private string sceneName;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            FeedbackBus.Raised += OnFeedback;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            FeedbackBus.Raised -= OnFeedback;
            Instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive) return;
            sceneName = scene.name;
            player = null;
            tide = null;
            resonanceCore = null;
            memoryShard = null;
            ventAnchor = null;
            hasLevelStart = false;
            resolved = false;
            failed = false;
            impulse = 0f;
            Mood = sceneName == "Menu" ? SceneMood.Drift : SceneMood.Explore;
            Region = sceneName == "Map4" ? SceneRegion.Reef : SceneRegion.OpenWater;
            Energy = 0f;
            SearchScene();
        }

        private void SearchScene()
        {
            player = FindFirstObjectByType<PlayerController>();
            if (player != null && !hasLevelStart)
            {
                levelStart = player.transform.position;
                hasLevelStart = true;
            }
            tide = BubbleTideDirector.Instance;
            if (sceneName == "Map4")
            {
                GameObject core = GameObject.Find("Resonance_Core_Visual");
                if (core == null) core = GameObject.Find("Map4_Hanging_Coral_Eye_Core");
                GameObject shard = GameObject.Find("Trench_Memory_Shard");
                GameObject vent = GameObject.Find("East_Vent_Coral_Ridge");
                if (vent == null) vent = GameObject.Find("East_Vent_Cavern_Cavern_Floor");
                resonanceCore = core != null ? core.transform : null;
                memoryShard = shard != null ? shard.transform : null;
                ventAnchor = vent != null ? vent.transform : null;
            }
            nextSearch = Time.unscaledTime + 2f;
        }

        private void OnFeedback(FeedbackEvent e)
        {
            switch (e.Cue)
            {
                case FeedbackCue.TideWin:
                case FeedbackCue.ChapterComplete: resolved = true; break;
                case FeedbackCue.TideLose:
                case FeedbackCue.ChapterFailure: failed = true; break;
                case FeedbackCue.BubbleBurst: impulse = Mathf.Max(impulse, .9f * e.Intensity); break;
                case FeedbackCue.BubbleMerge: impulse = Mathf.Max(impulse, .58f * e.Intensity); break;
                case FeedbackCue.BubbleLaunch: impulse = Mathf.Max(impulse, .38f * e.Intensity); break;
                case FeedbackCue.Pickup: impulse = Mathf.Max(impulse, .55f); break;
            }
        }

        private void Update()
        {
            if (tide == null) tide = BubbleTideDirector.Instance;
            if ((player == null || sceneName == "Map4" && resonanceCore == null) && Time.unscaledTime >= nextSearch)
                SearchScene();
            impulse = Mathf.MoveTowards(impulse, 0f, Time.unscaledDeltaTime * .22f);

            SceneMood target = sceneName == "Menu" ? SceneMood.Drift : SceneMood.Explore;
            float targetEnergy = impulse;
            if (tide != null)
            {
                if (tide.State == BubbleTideState.Won) resolved = true;
                else if (tide.State == BubbleTideState.Lost) failed = true;
                else if (tide.State == BubbleTideState.Flooding || tide.State == BubbleTideState.GoalFlooded)
                {
                    target = SceneMood.Danger;
                    targetEnergy = Mathf.Max(targetEnergy, .45f + tide.Progress * .55f);
                }
            }
            if (sceneName == "Map4" && player != null && target != SceneMood.Danger)
            {
                Vector3 point = player.transform.position;
                float shardHorizontal = memoryShard != null
                    ? Vector2.Distance(new Vector2(point.x, point.z),
                        new Vector2(memoryShard.position.x, memoryShard.position.z))
                    : float.PositiveInfinity;
                float ventHorizontal = ventAnchor != null
                    ? Vector2.Distance(new Vector2(point.x, point.z),
                        new Vector2(ventAnchor.position.x, ventAnchor.position.z))
                    : float.PositiveInfinity;
                bool belowStart = hasLevelStart && point.y < levelStart.y - 14f;
                Region = belowStart || shardHorizontal < 22f &&
                    Mathf.Abs(point.y - memoryShard.position.y) < 16f
                    ? SceneRegion.Trench : ventHorizontal < 24f &&
                    Mathf.Abs(point.y - ventAnchor.position.y) < 18f
                    ? SceneRegion.Vent : SceneRegion.Reef;
                float coreDistance = resonanceCore != null ? Vector3.Distance(point, resonanceCore.position) : float.PositiveInfinity;
                float shardDistance = memoryShard != null ? Vector3.Distance(point, memoryShard.position) : float.PositiveInfinity;
                if (coreDistance < 15f || shardDistance < 11f)
                {
                    target = SceneMood.Resonance;
                    targetEnergy = Mathf.Max(targetEnergy, Mathf.Clamp01(1f - Mathf.Min(coreDistance / 18f, shardDistance / 13f)) * .75f);
                }
                if (belowStart || shardHorizontal < 25f &&
                    point.y < memoryShard.position.y - 13f)
                {
                    target = SceneMood.Danger;
                    targetEnergy = Mathf.Max(targetEnergy, .6f);
                }
            }
            if (resolved) target = SceneMood.Resolution;
            else if (failed) target = SceneMood.Danger;
            Mood = target;
            Energy = Mathf.MoveTowards(Energy, Mathf.Clamp01(targetEnergy), Time.unscaledDeltaTime * .28f);
        }
    }
}

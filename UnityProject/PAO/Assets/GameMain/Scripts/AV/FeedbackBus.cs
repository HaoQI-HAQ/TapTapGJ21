using System;
using UnityEngine;

namespace PAO.AV
{
    public enum FeedbackCue
    {
        BubbleCharge,
        BubbleLaunch,
        BubbleCollision,
        BubbleMerge,
        BubbleBounce,
        BubbleBurst,
        BubblePop,
        BubbleSwitch,
        PlayerStep,
        PlayerJump,
        PlayerLand,
        Pickup,
        TideRising,
        TideWin,
        TideLose,
        Narration,
        UIHover,
        UIConfirm,
        UIBack,
        UISoftOpen,
        ChapterComplete,
        ChapterFailure
    }

    public struct FeedbackEvent
    {
        public FeedbackCue Cue;
        public Vector3 Position;
        public float Intensity;

        public FeedbackEvent(FeedbackCue cue, Vector3 position, float intensity)
        {
            Cue = cue;
            Position = position;
            Intensity = Mathf.Clamp01(intensity);
        }
    }

    /// <summary>Single runtime signal shared by sound, particles, and shader responses.</summary>
    public static class FeedbackBus
    {
        public static event Action<FeedbackEvent> Raised;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Raised = null; }

        public static void Emit(FeedbackCue cue, Vector3 position, float intensity = 1f)
        {
            Action<FeedbackEvent> handler = Raised;
            if (handler != null) handler(new FeedbackEvent(cue, position, intensity));
        }
    }
}

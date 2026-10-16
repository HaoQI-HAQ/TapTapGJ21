using UnityEngine;

namespace PAO
{
    /// <summary>各关卡共用的泡泡配置；正式配置以 cs 测试场景为基准。</summary>
    [CreateAssetMenu(menuName = "PAO/Bubble Loadout")]
    public sealed class BubbleLoadout : ScriptableObject
    {
        [Min(0)] public float maxChargeTime = 2f;
        [Min(0)] public float holdThreshold = 1f;
        public Vector3 spawnOffset = new Vector3(0, .15f, .3f);
        public float growthPushFactor = 1f;
        public Vector3 growthPushDirection = Vector3.forward;
        public StickyBubbleSettings sticky = new StickyBubbleSettings();
        public BouncyBubbleSettings bouncy = new BouncyBubbleSettings();
        public BombBubbleSettings bomb = new BombBubbleSettings();

        public BubbleSettings SettingsFor(BubbleType type)
        {
            switch (type)
            {
                case BubbleType.Bouncy: return bouncy;
                case BubbleType.Bomb: return bomb;
                default: return sticky;
            }
        }
    }
}
